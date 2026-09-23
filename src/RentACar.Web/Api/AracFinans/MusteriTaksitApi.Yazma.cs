using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Application.Kur;
using RentACar.Application.MusteriTaksitleri;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;

namespace RentACar.Web.Api.AracFinans;

public static partial class MusteriTaksitApi
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public const string ZatenKaydedildi = "Bu taksit zaten kaydedildi (No {0}, {1} {2}); yeni kayıt yazılmadı.";
    public const string AnahtarFarkliIcerik =
        "Bu işlem anahtarıyla başka içerikte bir taksit kaydı yazılmış (No {0}, {1} {2}); girdiğiniz kayıt YAZILMADI. Kayıtları kontrol edin.";

    /// <summary>Ortak giriş kuralları (oluştur + PUT): varlık, kapsam, sınırlar, TRY'de kur = 1.</summary>
    private static async Task<MusteriTaksitInput> GirdiAsync(MusteriTaksitIstegi i, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser kullanici, KurCozucu kurCozucu, CancellationToken ct)
    {
        AracFinansOrtak.Tutar(i.TaksitTutari, "taksitTutari", scale: 2); // servis 2 haneye yuvarlar — sessiz yuvarlama yerine red (L1)
        if (i.Vade is null) throw new ValidationException("Vade zorunludur.", "vade");
        AracFinansOrtak.Metin(i.Aciklama, 512, "aciklama");
        var durum = F5Ortak.EnumAdi<TaksitDurum>(i.Durum, "durum") ?? TaksitDurum.Bekliyor;
        var odeme = F5Ortak.Utc(i.OdemeTarihi);
        if (durum == TaksitDurum.Odendi) Alanli("odemeTarihi", () => TarihPolitikasi.ParaTarihi(odeme, "Taksit ödeme"));
        var (doviz, kur) = await DovizKurAsync(i.Doviz, i.Kur, F5Ortak.Utc(i.Vade), kurCozucu, ct);
        await VarlikAsync(dbf, kullanici, i.CariId, i.VehicleId, i.VehicleSaleId, ct);
        return new MusteriTaksitInput
        {
            CariId = i.CariId, VehicleId = BosIse(i.VehicleId), VehicleSaleId = BosIse(i.VehicleSaleId),
            Vade = F5Ortak.Utc(i.Vade), TaksitTutari = i.TaksitTutari, Currency = doviz, Kur = kur, Durum = durum,
            OdemeTarihi = durum == TaksitDurum.Odendi ? odeme : null, Aciklama = AracFinansOrtak.Nz(i.Aciklama),
        };
    }

    private static async Task<(string Doviz, decimal Kur)> DovizKurAsync(string? dovizGirdi, decimal? kurGirdi,
        DateTimeOffset? tarih, KurCozucu kurCozucu, CancellationToken ct)
    {
        var doviz = AracFinansOrtak.Doviz(dovizGirdi);
        AracFinansOrtak.Kur(kurGirdi, doviz, scale: 4); // sınır + TRY'de kur = 1; kolon numeric(19,4)
        decimal kur = 1m;
        try { kur = await kurCozucu.CozAsync(doviz, kurGirdi, tarih, ct); }
        catch (ValidationException ex) when (ex.GetType() == typeof(ValidationException) && ex.Alan is null)
        { throw new ValidationException(ex.Message, "kur"); }
        return (doviz, kur);
    }

    private static async Task VarlikAsync(IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, Guid cariId,
        Guid? vehicleId, Guid? vehicleSaleId, CancellationToken ct)
    {
        await AracFinansOrtak.CariVarAsync(dbf, cariId, "cariId", zorunlu: true, ct);
        await AracFinansOrtak.AracYazimAsync(dbf, kullanici, vehicleId, "vehicleId", zorunlu: false, ct);
        if (BosIse(vehicleSaleId) is { } s)
        {
            await using var db = await dbf.CreateDbContextAsync(ct);
            if (!await db.VehicleSales.AsNoTracking().AnyAsync(x => x.Id == s, ct))
                throw new ValidationException("Araç satış kaydı bulunamadı.", "vehicleSaleId");
        }
    }

    private static Guid? BosIse(Guid? g) => g is { } x && x != Guid.Empty ? x : null;

    private static async Task<Created<MusteriTaksitOlusturYaniti>> Olustur(
        MusteriTaksitIstegi i, HttpContext http, MusteriTaksitService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser kullanici, KurCozucu kurCozucu, CancellationToken ct)
    {
        var anahtar = IdempotencyBasligi.ZorunluAnahtar(http);
        if (await svc.GetAsync(anahtar, ct) is { } m) throw TekilMevcut(m, i); // (1) ÖNCE mevcut kayıt
        var girdi = await GirdiAsync(i, dbf, kullanici, kurCozucu, ct);
        girdi.IslemAnahtari = anahtar;
        try { await svc.CreateAsync(girdi, ct); }
        catch (MukerrerIslemException ex) when (ex.Mevcut is null)
        {
            if (await svc.GetAsync(anahtar, ct) is { } y) throw TekilMevcut(y, i);
            throw;
        }
        return TypedResults.Created($"{Kok}/{anahtar}", new MusteriTaksitOlusturYaniti(anahtar));
    }

    private static MukerrerIslemException TekilMevcut(MusteriTaksit m, MusteriTaksitIstegi i)
    {
        var doviz = AracFinansOrtak.Doviz(i.Doviz); // yazımla AYNI normalizasyon (L1)
        var ayni = m.CariId == i.CariId && m.VehicleId == BosIse(i.VehicleId)
                   && m.TaksitTutari == decimal.Round(i.TaksitTutari, 2, MidpointRounding.AwayFromZero)
                   && m.Currency == doviz && AracFinansOrtak.AyniAn(m.Vade, F5Ortak.Utc(i.Vade));
        return Mevcut(m.Id, m.Sira, m.TaksitTutari, m.Currency, ayni);
    }

    private static MukerrerIslemException Mevcut(Guid id, int sira, decimal tutar, string doviz, bool ayni)
        => new(string.Format(Tr, ayni ? ZatenKaydedildi : AnahtarFarkliIcerik, $"#{sira}", tutar.ToString("N2", Tr), doviz),
            new MevcutIslem(id, $"#{sira}", tutar, doviz, ayni));

    /// <summary>Alan'sız doğrulama hatasını alan'lı yeniden fırlatır (alt tipler korunur).</summary>
    private static void Alanli(string alan, Action dogrula)
    {
        try { dogrula(); }
        catch (ValidationException ex) when (ex.GetType() == typeof(ValidationException) && ex.Alan is null)
        { throw new ValidationException(ex.Message, alan); }
    }
}
