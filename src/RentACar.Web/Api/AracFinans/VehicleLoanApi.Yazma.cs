using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.AracKredileri;
using RentACar.Application.Common;
using RentACar.Application.Kur;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;

namespace RentACar.Web.Api.AracFinans;

public static partial class VehicleLoanApi
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public const string LoanAlreadySaved = "Bu kredi zaten kaydedildi (No {0}, {1} {2}); yeni kredi yazılmadı.";
    public const string LoanKeyMismatch =
        "Bu işlem anahtarıyla başka içerikte bir kredi kaydedilmiş (No {0}, {1} {2}); girdiğiniz kredi YAZILMADI. Kayıtları kontrol edin.";

    private static async Task<Created<AracKrediOlusturYaniti>> Create(
        AracKrediIstegi i, HttpContext http, VehicleLoanService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser kullanici, ExchangeRateResolver kurCozucu, CancellationToken ct)
    {
        var key = IdempotencyHeader.RequiredKey(http);
        var currency = VehicleFinanceShared.Currency(i.Doviz);
        // (1) ÖNCE mevcut kayıt (DEVIR §5): kaybolan yanıttan sonraki tekrar "zaten kaydedildi" alır.
        if (await svc.GetAsync(key, ct) is { } m) throw LoanExists(m, i, currency);

        var bank = VehicleFinanceShared.Nz(i.BankaAdi) ?? throw new ValidationException("Banka adı zorunludur.", "bankaAdi");
        VehicleFinanceShared.Text(bank, 200, "bankaAdi");
        VehicleFinanceShared.Text(i.DosyaNo, 64, "dosyaNo");
        VehicleFinanceShared.Text(i.Aciklama, 512, "aciklama");
        VehicleFinanceShared.Amount(i.KrediTutari, "krediTutari");
        if (i.KrediTutari > MaxLoanAmount)
            throw new ValidationException($"Kredi tutarı en fazla {MaxLoanAmount:N0} olabilir.", "krediTutari");
        if (i.FaizOran < 0m || i.FaizOran > MaxInterestRate)
            throw new ValidationException("Faiz oranı 0 ile 10 (yıllık %1000) arasında bir kesir olmalıdır.", "faizOran");
        VehicleFinanceShared.EnsureMaxScale(i.FaizOran, 4, "faizOran"); // numeric(9,4) — L1
        if (i.TaksitSayisi is < 1 or > 360)
            throw new ValidationException("Taksit sayısı 1 ile 360 arasında olmalıdır.", "taksitSayisi");
        var start = F5Shared.Utc(i.BaslangicTarihi);
        VehicleFinanceShared.Setup(i.Kur, currency); // sınır + TRY'de kur = 1
        decimal exchangeRate = 0m;
        await WithFields("kur", async () => exchangeRate = await kurCozucu.ResolveAsync(currency, i.Kur, start, ct));
        await VehicleFinanceShared.CustomerExistsAsync(dbf, i.CariId, "cariId", required: false, ct);
        await VehicleFinanceShared.VehicleWriteAsync(dbf, kullanici, i.VehicleId, "vehicleId", required: false, ct);

        try
        {
            await svc.CreateAsync(new AracKrediInput
            {
                BankaAdi = bank, VehicleId = i.VehicleId, CariId = i.CariId, DosyaNo = VehicleFinanceShared.Nz(i.DosyaNo),
                KrediTutari = i.KrediTutari, FaizOran = i.FaizOran, TaksitSayisi = i.TaksitSayisi, BaslangicTarihi = start,
                Doviz = currency, Kur = exchangeRate, Aciklama = VehicleFinanceShared.Nz(i.Aciklama), IslemAnahtari = key,
            }, ct);
        }
        catch (DuplicateOperationException ex) when (ex.Existing is null)
        {
            // Yarış: aynı anahtarla eşzamanlı ikinci istek PK'ye çarptı → yazılmış kaydı bildir.
            if (await svc.GetAsync(key, ct) is { } y) throw LoanExists(y, i, currency);
            throw;
        }
        var no = (await svc.GetAsync(key, ct))?.No ?? "";
        return TypedResults.Created($"{Root}/{key}", new AracKrediOlusturYaniti(key, no));
    }

    /// <summary>Aynı anahtarla yazılmış kredi: içerik birebir aynıysa kendi tekrarı, değilse "YAZILMADI".</summary>
    private static DuplicateOperationException LoanExists(AracKredi m, AracKrediIstegi i, string currency)
    {
        var same = string.Equals(m.BankaAdi, VehicleFinanceShared.Nz(i.BankaAdi), StringComparison.Ordinal)
                   && m.KrediTutari == i.KrediTutari && m.FaizOran == i.FaizOran && m.TaksitSayisi == i.TaksitSayisi
                   && m.Currency == currency && m.VehicleId == IfEmpty(i.VehicleId) && m.CariId == IfEmpty(i.CariId);
        var amount = m.KrediTutari.ToString("N2", Tr);
        return new DuplicateOperationException(
            string.Format(Tr, same ? LoanAlreadySaved : LoanKeyMismatch, m.No, amount, m.Currency),
            new MevcutIslem(m.Id, m.No, m.KrediTutari, m.Currency, same));
    }

    private static Guid? IfEmpty(Guid? g) => g is { } x && x != Guid.Empty ? x : null;

    private static async Task<Results<NoContent, ProblemHttpResult>> Cancel(
        Guid id, VehicleLoanService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, CancellationToken ct)
    {
        if (await ComprehensiveAsync(id, svc, dbf, kullanici, ct) is null) return NotFoundProblem();
        // Yapısal: yalnız Aktif kredi, satır kilidi ARKASINDA (kapanmış kredi iptale düşmez; ödenmiş taksit geri alınmaz).
        if (await svc.CancelInstallmentsAsync([id], ct) == 0)
            throw new ValidationException("Kredi aktif değil (kapanmış ya da zaten iptal); iptal edilecek taksit yok.");
        return TypedResults.NoContent();
    }

    public const int MaxBulkCancel = 500;

    private static async Task<Results<Ok<KrediTopluIptalYaniti>, ProblemHttpResult>> BulkCancel(
        KrediTopluIptalIstegi istek, VehicleLoanService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici,
        CancellationToken ct)
    {
        var ids = (istek.Ids ?? []).Where(x => x != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) throw new ValidationException("İptal edilecek kredi seçilmedi.", "ids");
        if (ids.Count > MaxBulkCancel)
            throw new ValidationException($"Tek seferde en fazla {MaxBulkCancel} kredi iptal edilebilir.", "ids");
        foreach (var id in ids) // hepsi var ve kapsamda olmalı — biri değilse HİÇBİRİ iptal edilmez
            if (await ComprehensiveAsync(id, svc, dbf, kullanici, ct) is null) return NotFoundProblem();
        var n = await svc.CancelInstallmentsAsync(ids, ct);
        if (n == 0) throw new ValidationException("Seçilen kredilerin hiçbiri aktif değil; iptal edilecek taksit yok.", "ids");
        return TypedResults.Ok(new KrediTopluIptalYaniti(n));
    }

    /// <summary>Alan'sız doğrulama hatasını alan'lı yeniden fırlatır (alt tipler korunur).</summary>
    private static async Task WithFields(string alan, Func<Task> validate)
    {
        try { await validate(); }
        catch (ValidationException ex) when (ex.GetType() == typeof(ValidationException) && ex.Alan is null)
        { throw new ValidationException(ex.Message, alan); }
    }

    private static LedgerAccountType AccountType(string? account)
        => account?.Trim() switch
        {
            { } h when string.Equals(h, "Kasa", StringComparison.OrdinalIgnoreCase) => LedgerAccountType.Kasa,
            { } h when string.Equals(h, "Banka", StringComparison.OrdinalIgnoreCase) => LedgerAccountType.Banka,
            _ => throw new ValidationException("Hesap Kasa ya da Banka olmalıdır.", "hesap"),
        };
}
