using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Application.ServiceRecords;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.AracFinans;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;
using S = RentACar.Web.Api.ServiceInsurance.ServiceInsuranceShared;

namespace RentACar.Web.Api.ServiceInsurance;

internal static partial class ServiceRecordApi
{
    private const string OtherOperation =
        "Bu işlem anahtarı başka bir işlemde kullanılmış; kayıt yazılmadı. Kayıtları kontrol edip yeni işlem başlatın.";

    // ------------------------------------------------------------------ oluştur

    private static async Task<Results<Created<ServiceRecordDetail>, ProblemHttpResult>> Create(
        ServiceRecordRequest r, HttpContext http, ServiceRecordService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser user,
        CancellationToken ct)
    {
        var key = IdempotencyBasligi.Anahtar(http);
        if (key is { } k && await ScopedAsync(k, svc, dbf, user, ct) is { } m) throw Duplicate(m, r); // (1) ÖNCE mevcut
        if (r.KusurOrani is { } ko) AracFinansOrtak.EnsureMaxScale(ko, 4, "kusurOrani");
        S.IntRange(r.GirisKm, 0, 10_000_000, "girisKm");
        if (r.Bilgi is { } b) InfoLimits(b, null);
        if (r.Kalem is { } l) LineLimits(l);
        await S.VehicleForWriteAsync(dbf, user, r.VehicleId, "vehicleId", ct);
        var input = new ServiceRecordInput
        {
            Id = key, VehicleId = r.VehicleId!.Value,
            Tip = F5Ortak.EnumAdi<ServiceType>(r.Tip, "tip") ?? ServiceType.Periyodik, GirisKm = r.GirisKm ?? 0,
            GirisTarihi = S.Date(r.GirisTarihi, "girisTarihi"),
            HasarSorumlu = F5Ortak.EnumAdi<DamageResponsible>(r.HasarSorumlu, "hasarSorumlu") ?? DamageResponsible.Yok,
            KusurOrani = r.KusurOrani, Rezervasyon = r.Rezervasyon,
        };
        if (r.Bilgi is { } bi) ApplyInfo(input, bi);
        if (r.Kalem is { } kl && F5Ortak.Nz(kl.Aciklama) is not null) input.Lines.Add(LineInput(kl, null));
        Guid id;
        try
        {
            id = await svc.CreateAsync(input, ct);
        }
        catch (DbUpdateException ex) when (key is { } k2 && S.IsPrimaryKeyViolation(ex))
        {
            if (await ScopedAsync(k2, svc, dbf, user, ct) is { } won) throw Duplicate(won, r);
            throw new DuplicateOperationException(OtherOperation);
        }
        var d = await DetailAsync(id, http, svc, dbf, user, ct);
        return TypedResults.Created($"{Root}/{id}", d!);
    }

    private static DuplicateOperationException Duplicate(ServiceRecord m, ServiceRecordRequest r)
        => new($"Bu servis kaydı zaten eklendi (No {m.No}); yeni kayıt yazılmadı.",
            new MevcutIslem(m.Id, m.No, m.ToplamIscilik, "TRY",
                m.VehicleId == r.VehicleId && m.GirisKm == (r.GirisKm ?? 0)
                && string.Equals(m.Tip.ToString(), r.Tip ?? nameof(ServiceType.Periyodik), StringComparison.OrdinalIgnoreCase)));

    // ------------------------------------------------------------------ bilgi blokları (tam değiştirme)

    private static async Task<Results<Ok<ServiceRecordDetail>, ProblemHttpResult>> UpdateInfo(
        Guid id, ServiceInfoRequest r, HttpContext http, ServiceRecordService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser user, CancellationToken ct)
    {
        if (await ScopedAsync(id, svc, dbf, user, ct) is not { } old) return NotFound();
        var version = AracFinansOrtak.Surum(r.Surum);
        InfoLimits(r, old);
        var input = new ServiceRecordBilgiInput();
        ApplyInfo(input, r);
        if (!await svc.UpdateInfoVersionedAsync(id, input, version, ct)) return NotFound();
        return TypedResults.Ok((await DetailAsync(id, http, svc, dbf, user, ct))!);
    }

    /// <summary>Column limits; amount/rate scale checked only when the value changed (legacy rows stay editable).</summary>
    private static void InfoLimits(ServiceInfoRequest r, ServiceRecord? o)
    {
        S.Text(r.AtolyeAdi, 128, "atolyeAdi"); S.Text(r.Aciklama, 1024, "aciklama"); S.Text(r.BeyanTuru, 64, "beyanTuru");
        S.Text(r.KarsiPlaka, 32, "karsiPlaka"); S.Text(r.KarsiTrafikSigortasi, 128, "karsiTrafikSigortasi");
        S.Text(r.KazaSorumlusu, 128, "kazaSorumlusu"); S.Text(r.HasarDosyaNo, 64, "hasarDosyaNo"); S.Text(r.FaturaNo, 64, "faturaNo");
        S.Text(r.OdemeDoviz, 3, "odemeDoviz"); S.Text(r.KasaKodu, 32, "kasaKodu"); S.Text(r.HesapNo, 64, "hesapNo");
        S.CatalogAmount(r.DegerKaybi, o?.DegerKaybi, "degerKaybi"); S.CatalogAmount(r.FaturaTutar, o?.FaturaTutar, "faturaTutar");
        S.CatalogAmount(r.FaturaKdv, o?.FaturaKdv, "faturaKdv"); S.CatalogAmount(r.Odeme, o?.Odeme, "odeme");
        if (r.OdemeKur is { } k && r.OdemeKur != o?.OdemeKur)
        {
            if (k >= 1_000_000m) throw new ValidationException("Kur çok büyük.", "odemeKur");
            AracFinansOrtak.EnsureMaxScale(k, 6, "odemeKur");
        }
        F5Ortak.EnumAdi<PaymentMethod>(r.OdemeTuru, "odemeTuru");
    }

    private static void ApplyInfo(ServiceRecordBilgiInput b, ServiceInfoRequest r)
    {
        b.AtolyeAdi = r.AtolyeAdi; b.Aciklama = r.Aciklama; b.BeyanTuru = r.BeyanTuru; b.KarsiPlaka = r.KarsiPlaka;
        b.KarsiTrafikSigortasi = r.KarsiTrafikSigortasi; b.KazaTarihi = S.Date(r.KazaTarihi, "kazaTarihi");
        b.KazaSorumlusu = r.KazaSorumlusu; b.HasarDosyaNo = r.HasarDosyaNo; b.DegerKaybi = r.DegerKaybi;
        b.FaturaTarihi = S.Date(r.FaturaTarihi, "faturaTarihi"); b.FaturaNo = r.FaturaNo; b.FaturaTutar = r.FaturaTutar;
        b.FaturaKdv = r.FaturaKdv; b.OdemeTarihi = S.Date(r.OdemeTarihi, "odemeTarihi"); b.Odeme = r.Odeme;
        b.OdemeDoviz = r.OdemeDoviz; b.OdemeKur = r.OdemeKur; b.OdemeTuru = F5Ortak.EnumAdi<PaymentMethod>(r.OdemeTuru, "odemeTuru");
        b.KasaKodu = r.KasaKodu; b.HesapNo = r.HesapNo; b.CikisYakit = r.CikisYakit; b.DonusYakit = r.DonusYakit;
        b.PlanBasTarihi = S.Date(r.PlanBasTarihi, "planBasTarihi"); b.PlanBitTarihi = S.Date(r.PlanBitTarihi, "planBitTarihi");
    }

    // ------------------------------------------------------------------ durum akışı (satır kilidi altında)

    private static async Task<Results<Ok<ServiceRecordDetail>, ProblemHttpResult>> Transition(Guid id, HttpContext http,
        ServiceRecordService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser user, Func<Task<bool>> run, CancellationToken ct)
    {
        if (await ScopedAsync(id, svc, dbf, user, ct) is null) return NotFound(); // kapsam durumdan ÖNCE
        if (!await run()) return NotFound();
        return TypedResults.Ok((await DetailAsync(id, http, svc, dbf, user, ct))!);
    }

    private static Task<Results<Ok<ServiceRecordDetail>, ProblemHttpResult>> Intake(Guid id, ServiceIntakeRequest r, HttpContext http,
        ServiceRecordService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        S.IntRange(r.GirisKm, 0, 10_000_000, "girisKm");
        return Transition(id, http, svc, dbf, user, () => svc.TakeIntoServiceAsync(id, r.GirisKm, ct), ct);
    }

    private static Task<Results<Ok<ServiceRecordDetail>, ProblemHttpResult>> Start(Guid id, HttpContext http,
        ServiceRecordService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
        => Transition(id, http, svc, dbf, user, () => svc.StartAsync(id, ct), ct);

    private static Task<Results<Ok<ServiceRecordDetail>, ProblemHttpResult>> Complete(Guid id, ServiceCompleteRequest r,
        HttpContext http, ServiceRecordService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        if (r.CikisKm is null) throw new ValidationException("Çıkış KM zorunludur.", "cikisKm");
        S.IntRange(r.CikisKm, 0, 10_000_000, "cikisKm");
        S.IntRange(r.SonrakiBakimKm, 0, 10_000_000, "sonrakiBakimKm");
        return Transition(id, http, svc, dbf, user, () => svc.CompleteAsync(id, r.CikisKm.Value, r.SonrakiBakimKm, ct), ct);
    }

    private static Task<Results<Ok<ServiceRecordDetail>, ProblemHttpResult>> Cancel(Guid id, HttpContext http,
        ServiceRecordService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
        => Transition(id, http, svc, dbf, user, () => svc.CancelAsync(id, ct), ct);
}
