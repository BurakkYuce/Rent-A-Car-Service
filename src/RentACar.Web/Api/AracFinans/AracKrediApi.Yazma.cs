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

public static partial class AracKrediApi
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public const string KrediZatenKaydedildi = "Bu kredi zaten kaydedildi (No {0}, {1} {2}); yeni kredi yazılmadı.";
    public const string KrediAnahtarFarkli =
        "Bu işlem anahtarıyla başka içerikte bir kredi kaydedilmiş (No {0}, {1} {2}); girdiğiniz kredi YAZILMADI. Kayıtları kontrol edin.";

    private static async Task<Created<AracKrediOlusturYaniti>> Olustur(
        AracKrediIstegi i, HttpContext http, VehicleLoanService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser kullanici, ExchangeRateResolver kurCozucu, CancellationToken ct)
    {
        var anahtar = IdempotencyBasligi.ZorunluAnahtar(http);
        var doviz = AracFinansOrtak.Doviz(i.Doviz);
        // (1) ÖNCE mevcut kayıt (DEVIR §5): kaybolan yanıttan sonraki tekrar "zaten kaydedildi" alır.
        if (await svc.GetAsync(anahtar, ct) is { } m) throw KrediMevcut(m, i, doviz);

        var banka = AracFinansOrtak.Nz(i.BankaAdi) ?? throw new ValidationException("Banka adı zorunludur.", "bankaAdi");
        AracFinansOrtak.Metin(banka, 200, "bankaAdi");
        AracFinansOrtak.Metin(i.DosyaNo, 64, "dosyaNo");
        AracFinansOrtak.Metin(i.Aciklama, 512, "aciklama");
        AracFinansOrtak.Tutar(i.KrediTutari, "krediTutari");
        if (i.KrediTutari > EnFazlaKrediTutari)
            throw new ValidationException($"Kredi tutarı en fazla {EnFazlaKrediTutari:N0} olabilir.", "krediTutari");
        if (i.FaizOran < 0m || i.FaizOran > EnFazlaFaizOrani)
            throw new ValidationException("Faiz oranı 0 ile 10 (yıllık %1000) arasında bir kesir olmalıdır.", "faizOran");
        AracFinansOrtak.EnsureMaxScale(i.FaizOran, 4, "faizOran"); // numeric(9,4) — L1
        if (i.TaksitSayisi is < 1 or > 360)
            throw new ValidationException("Taksit sayısı 1 ile 360 arasında olmalıdır.", "taksitSayisi");
        var bas = F5Ortak.Utc(i.BaslangicTarihi);
        AracFinansOrtak.Kur(i.Kur, doviz); // sınır + TRY'de kur = 1
        decimal kur = 0m;
        await Alanli("kur", async () => kur = await kurCozucu.ResolveAsync(doviz, i.Kur, bas, ct));
        await AracFinansOrtak.CariVarAsync(dbf, i.CariId, "cariId", zorunlu: false, ct);
        await AracFinansOrtak.AracYazimAsync(dbf, kullanici, i.VehicleId, "vehicleId", zorunlu: false, ct);

        try
        {
            await svc.CreateAsync(new AracKrediInput
            {
                BankaAdi = banka, VehicleId = i.VehicleId, CariId = i.CariId, DosyaNo = AracFinansOrtak.Nz(i.DosyaNo),
                KrediTutari = i.KrediTutari, FaizOran = i.FaizOran, TaksitSayisi = i.TaksitSayisi, BaslangicTarihi = bas,
                Doviz = doviz, Kur = kur, Aciklama = AracFinansOrtak.Nz(i.Aciklama), IslemAnahtari = anahtar,
            }, ct);
        }
        catch (DuplicateOperationException ex) when (ex.Existing is null)
        {
            // Yarış: aynı anahtarla eşzamanlı ikinci istek PK'ye çarptı → yazılmış kaydı bildir.
            if (await svc.GetAsync(anahtar, ct) is { } y) throw KrediMevcut(y, i, doviz);
            throw;
        }
        var no = (await svc.GetAsync(anahtar, ct))?.No ?? "";
        return TypedResults.Created($"{Kok}/{anahtar}", new AracKrediOlusturYaniti(anahtar, no));
    }

    /// <summary>Aynı anahtarla yazılmış kredi: içerik birebir aynıysa kendi tekrarı, değilse "YAZILMADI".</summary>
    private static DuplicateOperationException KrediMevcut(AracKredi m, AracKrediIstegi i, string doviz)
    {
        var ayni = string.Equals(m.BankaAdi, AracFinansOrtak.Nz(i.BankaAdi), StringComparison.Ordinal)
                   && m.KrediTutari == i.KrediTutari && m.FaizOran == i.FaizOran && m.TaksitSayisi == i.TaksitSayisi
                   && m.Currency == doviz && m.VehicleId == BosIse(i.VehicleId) && m.CariId == BosIse(i.CariId);
        var tutar = m.KrediTutari.ToString("N2", Tr);
        return new DuplicateOperationException(
            string.Format(Tr, ayni ? KrediZatenKaydedildi : KrediAnahtarFarkli, m.No, tutar, m.Currency),
            new MevcutIslem(m.Id, m.No, m.KrediTutari, m.Currency, ayni));
    }

    private static Guid? BosIse(Guid? g) => g is { } x && x != Guid.Empty ? x : null;

    private static async Task<Results<NoContent, ProblemHttpResult>> Iptal(
        Guid id, VehicleLoanService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, CancellationToken ct)
    {
        if (await KapsamliAsync(id, svc, dbf, kullanici, ct) is null) return Bulunamadi();
        // Yapısal: yalnız Aktif kredi, satır kilidi ARKASINDA (kapanmış kredi iptale düşmez; ödenmiş taksit geri alınmaz).
        if (await svc.CancelInstallmentsAsync([id], ct) == 0)
            throw new ValidationException("Kredi aktif değil (kapanmış ya da zaten iptal); iptal edilecek taksit yok.");
        return TypedResults.NoContent();
    }

    public const int TopluIptalEnFazla = 500;

    private static async Task<Results<Ok<KrediTopluIptalYaniti>, ProblemHttpResult>> TopluIptal(
        KrediTopluIptalIstegi istek, VehicleLoanService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici,
        CancellationToken ct)
    {
        var ids = (istek.Ids ?? []).Where(x => x != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) throw new ValidationException("İptal edilecek kredi seçilmedi.", "ids");
        if (ids.Count > TopluIptalEnFazla)
            throw new ValidationException($"Tek seferde en fazla {TopluIptalEnFazla} kredi iptal edilebilir.", "ids");
        foreach (var id in ids) // hepsi var ve kapsamda olmalı — biri değilse HİÇBİRİ iptal edilmez
            if (await KapsamliAsync(id, svc, dbf, kullanici, ct) is null) return Bulunamadi();
        var n = await svc.CancelInstallmentsAsync(ids, ct);
        if (n == 0) throw new ValidationException("Seçilen kredilerin hiçbiri aktif değil; iptal edilecek taksit yok.", "ids");
        return TypedResults.Ok(new KrediTopluIptalYaniti(n));
    }

    /// <summary>Alan'sız doğrulama hatasını alan'lı yeniden fırlatır (alt tipler korunur).</summary>
    private static async Task Alanli(string alan, Func<Task> dogrula)
    {
        try { await dogrula(); }
        catch (ValidationException ex) when (ex.GetType() == typeof(ValidationException) && ex.Alan is null)
        { throw new ValidationException(ex.Message, alan); }
    }

    private static LedgerAccountType HesapTuru(string? hesap)
        => hesap?.Trim() switch
        {
            { } h when string.Equals(h, "Kasa", StringComparison.OrdinalIgnoreCase) => LedgerAccountType.Kasa,
            { } h when string.Equals(h, "Banka", StringComparison.OrdinalIgnoreCase) => LedgerAccountType.Banka,
            _ => throw new ValidationException("Hesap Kasa ya da Banka olmalıdır.", "hesap"),
        };
}
