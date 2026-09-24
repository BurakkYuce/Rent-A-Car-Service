using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Application.FaturaDonemleri;
using RentACar.Application.Periods;
using RentACar.Application.Reporting;
using RentACar.Application.TenantSettings;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Finans;
using RentACar.Web.Api.Rezervasyon;

namespace RentACar.Web.Api.FinansHub;

/// <summary>Dönem kapanışı (Blazor <c>/donem-kapanis</c>, E36) ve otomatik tahsilat elle tetik (<c>/otomatik-tahsilat</c>,
/// E20). İkisi de YAPISAL idempotent: işlem anahtarı kullanılmaz (envanter).</summary>
public static partial class FinanceHubApi
{
    private static void MapPeriod(RouteGroupBuilder write)
    {
        write.MapGet("/donem-kapanis", GetPeriodClose);
        write.MapPost("/donem-kapanis/kilitle", PostPeriodClose);
        write.MapPost("/donem-kapanis/ac", PostPeriodUnlock);
        write.MapGet("/otomatik-tahsilat", ListAutoCollection);
        write.MapPost("/otomatik-tahsilat/calistir", PostAutoCollection);
    }

    private static async Task<Ok<PeriodCloseState>> GetPeriodClose(
        DonemKilidiService periods, ReportService reports, CancellationToken ct)
    {
        var closing = await periods.GetClosingDateAsync(ct);
        var trial = await reports.GetMizanAsync(ct: ct);
        // Önizleme GÜNCEL mizan bakiyesinden (kapanışın sıfırlayacağı tutar): Gelir Alacak bakiyeli (negatif) → gelir = −bakiye.
        decimal Balance(LedgerAccountType t) => trial.FirstOrDefault(m => m.Tip == t)?.Bakiye ?? 0m;
        var income = -Balance(LedgerAccountType.Gelir);
        var expense = Balance(LedgerAccountType.Gider);
        return TypedResults.Ok(new PeriodCloseState(
            closing is { } c ? PeriodLock.LocalDay(c) : null,
            [.. trial.Select(m => new TrialBalanceRow(m.Tip.ToString(), m.Ad, m.Borc, m.Alacak, m.Bakiye))],
            trial.Sum(m => m.Borc), trial.Sum(m => m.Alacak), trial.Sum(m => m.Bakiye),
            income, expense, income - expense));
    }

    /// <summary>E36: kapanış fişi + kilit (kiracı danışma kilidi altında). Aynı/önceki tarihe ikinci kapanış 400.
    /// Tarih bugünden (İstanbul) ileri olamaz — gelecek tarihe kilit bugünün tahsilat/faturalarını da durdururdu.</summary>
    private static async Task<NoContent> PostPeriodClose(
        PeriodCloseRequest req, DonemKapanisFisiService closing, CancellationToken ct)
    {
        if (req.KapanisTarihi is not { } day) throw new ValidationException("Kapanış tarihi gerekli.", "kapanisTarihi");
        if (day > TenantGun.Gun(DateTimeOffset.UtcNow))
            throw new ValidationException("Kapanış tarihi bugünden ileri olamaz.", "kapanisTarihi");
        if (day < DateOnly.FromDateTime(TarihPolitikasi.EnErkenBelgeTarihi.UtcDateTime))
            throw new ValidationException("Kapanış tarihi 2000 yılından önce olamaz.", "kapanisTarihi");
        // Kilit İSTANBUL takvim günü granülünde (PeriodLock) — günün İstanbul gece yarısı, UTC olarak gider.
        await closing.KapatAsync(F5Ortak.GunBasi(day), ct);
        return TypedResults.NoContent();
    }

    /// <summary>Kilidi TAMAMEN kaldırır; kesilmiş kapanış fişleri GERİ ALINMAZ (değişmez defter).</summary>
    private static async Task<NoContent> PostPeriodUnlock(DonemKilidiService periods, CancellationToken ct)
    {
        await periods.UnlockAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<AutoCollectionList>> ListAutoCollection(
        string? sozlesmeNo, DateOnly? vadeMin, DateOnly? vadeMax, bool? bakiyeli,
        OtomatikTahsilatService auto, ITenantSettingsRepository settings, IDbContextFactory<AppDbContext> f,
        CancellationToken ct)
    {
        FinansApi.Metin(sozlesmeNo, 64, "sozlesmeNo");
        // L1: uç tarihler (9999-12-31 + 1 gün) DateTimeOffset'i taşırıp 500 üretiyordu.
        foreach (var (d, field) in new[] { (vadeMin, "vadeMin"), (vadeMax, "vadeMax") })
            if (d is { Year: < 2000 or > 2100 })
                throw new ValidationException("Tarih 2000 ile 2100 arasında olmalıdır.", field);
        // Blazor ile aynı: vade günleri UTC takvim günü, bitiş günü DAHİL.
        var candidates = await auto.AdaylarAsync(new OtomatikTahsilatFiltre
        {
            SozlesmeNo = F5Ortak.Nz(sozlesmeNo),
            VadeMin = vadeMin is { } a ? new DateTimeOffset(a.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null,
            VadeMax = vadeMax is { } b ? new DateTimeOffset(b.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(1).AddTicks(-1) : null,
            SadeceBakiyeli = bakiyeli == true,
        }, ct);
        // Ayar okuması doğrudan repo'dan: TenantSettingsService ManageUsers ister; bu yalnız bilgi bayrağıdır.
        var jobOn = (await settings.GetAsync(ct))?.DonemselOtomatikTahsilat ?? false;
        var names = await F5Ortak.CarilerAsync(f, candidates.Select(c => c.CariId), ct);
        return TypedResults.Ok(new AutoCollectionList(jobOn,
            [.. candidates.Select(c => new AutoCollectionCandidate(c.RentalId, c.SozlesmeNo, c.DonemSira, c.DonemBas, c.DonemBit,
                c.CariId, F5Ortak.CariAdi(names, c.CariId), c.Sube, c.Doviz, c.KiraTutar, c.CariBakiye))],
            [.. OtomatikTahsilatService.DovizToplamlari(candidates).Select(t => new CurrencyTotal(t.Doviz, t.Toplam))]));
    }

    /// <summary>E20: yalnız görünür aday (şube kapsamı + vadesi gelmiş + Planlandi) çalışır; tekrar sessiz (kesilen 0,
    /// her dönem atlananlarda). Her dönem bağımsız: birinin hatası diğerlerini durdurmaz.</summary>
    private static async Task<Ok<AutoCollectionResult>> PostAutoCollection(
        AutoCollectionRequest req, OtomatikTahsilatService auto, CancellationToken ct)
    {
        var account = FinansApi.Hesap(req.Hesap, "hesap");
        var selection = req.Secim ?? [];
        if (selection.Count == 0) throw new ValidationException("En az bir dönem seçilmelidir.", "secim");
        if (selection.Count > OtomatikTahsilatService.MaxSecim)
            throw new ValidationException($"Tek seferde en çok {OtomatikTahsilatService.MaxSecim} dönem çalıştırılabilir.", "secim");
        for (var i = 0; i < selection.Count; i++)
            if (selection[i].KiraId == Guid.Empty || selection[i].DonemSira < 1)
                throw new ValidationException("Seçim okunamadı; listeyi yenileyip tekrar deneyin.", $"secim[{i}]");
        var result = await auto.CalistirAsync([.. selection.Select(s => (s.KiraId, s.DonemSira))], req.Tahsilat, account, ct);
        return TypedResults.Ok(new AutoCollectionResult(result.Kesilen, result.Tahsilat, result.Atlananlar));
    }
}
