using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Application.Kur;
using RentACar.Application.MusteriTaksitleri;
using RentACar.Domain.Common;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;

namespace RentACar.Web.Api.AracFinans;

public static partial class MusteriTaksitApi
{
    /// <summary>Plan: kimlikler anahtardan TÜRETİLİR (1. satır = anahtar); ikinci gönderim hiçbir satır yazmaz.</summary>
    private static async Task<Results<Created<TaksitPlanYaniti>, ProblemHttpResult>> Plan(
        TaksitPlanIstegi i, HttpContext http, MusteriTaksitService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser kullanici, KurCozucu kurCozucu, CancellationToken ct)
    {
        var anahtar = IdempotencyBasligi.ZorunluAnahtar(http);
        if (i.TaksitSayisi is < 1 or > MusteriTaksitService.MaxTaksit)
            throw new ValidationException($"Taksit sayısı 1 ile {MusteriTaksitService.MaxTaksit} arasında olmalıdır.", "taksitSayisi");
        await PlanMevcutAsync(anahtar, i, svc, dbf, ct); // (1) ÖNCE mevcut plan

        AracFinansOrtak.Tutar(i.ToplamTutar, "toplamTutar");
        AracFinansOrtak.Metin(i.Aciklama, 512, "aciklama");
        var ilk = F5Ortak.Utc(i.IlkVade);
        var (doviz, kur) = await DovizKurAsync(i.Doviz, i.Kur, ilk, kurCozucu, ct);
        await VarlikAsync(dbf, kullanici, i.CariId, i.VehicleId, i.VehicleSaleId, ct);
        int adet;
        try
        {
            adet = await svc.PlanUretAsync(new TaksitPlanInput
            {
                CariId = i.CariId, VehicleId = BosIse(i.VehicleId), VehicleSaleId = BosIse(i.VehicleSaleId),
                ToplamTutar = i.ToplamTutar, TaksitSayisi = i.TaksitSayisi, IlkVade = ilk, Currency = doviz, Kur = kur,
                Aciklama = AracFinansOrtak.Nz(i.Aciklama), IslemAnahtari = anahtar,
            }, ct);
        }
        catch (MukerrerIslemException ex) when (ex.Mevcut is null)
        {
            await PlanMevcutAsync(anahtar, i, svc, dbf, ct);
            throw;
        }
        var ids = Enumerable.Range(1, adet).Select(s => MusteriTaksitService.PlanSatirId(anahtar, s)).ToList();
        return TypedResults.Created($"{Kok}/{anahtar}", new TaksitPlanYaniti(adet, ids));
    }

    /// <summary>Aynı anahtarla yazılmış plan: <c>ayniIcerik</c> = aynı cari/araç/döviz, aynı adet ve aynı toplam.</summary>
    private static async Task PlanMevcutAsync(Guid anahtar, TaksitPlanIstegi i, MusteriTaksitService svc,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await svc.GetAsync(anahtar, ct) is not { } ilk) return;
        var olasi = Enumerable.Range(1, MusteriTaksitService.MaxTaksit)
            .Select(s => MusteriTaksitService.PlanSatirId(anahtar, s)).ToList();
        await using var db = await dbf.CreateDbContextAsync(ct);
        var satirlar = await db.MusteriTaksitleri.AsNoTracking().Where(t => olasi.Contains(t.Id))
            .Select(t => t.TaksitTutari).ToListAsync(ct);
        var toplam = satirlar.Sum();
        var doviz = string.IsNullOrWhiteSpace(i.Doviz) ? "TRY" : i.Doviz.Trim().ToUpperInvariant();
        var ayni = ilk.CariId == i.CariId && ilk.VehicleId == BosIse(i.VehicleId) && ilk.Currency == doviz
                   && satirlar.Count == i.TaksitSayisi
                   && toplam == decimal.Round(i.ToplamTutar, 2, MidpointRounding.AwayFromZero);
        throw Mevcut(ilk.Id, ilk.Sira, toplam, ilk.Currency, ayni);
    }

    private static async Task<Results<Ok<MusteriTaksitSatiri>, ProblemHttpResult>> Guncelle(
        Guid id, MusteriTaksitIstegi i, MusteriTaksitService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser kullanici, KurCozucu kurCozucu, CancellationToken ct)
    {
        if (await KapsamliAsync(id, svc, dbf, kullanici, ct) is null) return Bulunamadi(); // kapsam durumdan ÖNCE
        var surum = AracFinansOrtak.Surum(i.Surum);
        var girdi = await GirdiAsync(i, dbf, kullanici, kurCozucu, ct); // yeni araç da kapsamda olmalı
        if (!await svc.UpdateSurumluAsync(id, girdi, surum, ct)) return Bulunamadi();
        return await DtoAsync(id, svc, dbf, kullanici, ct) is { } d ? TypedResults.Ok(d) : Bulunamadi();
    }

    private static async Task<Results<Ok<MusteriTaksitSatiri>, ProblemHttpResult>> Odendi(
        Guid id, TaksitOdendiIstegi? istek, MusteriTaksitService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser kullanici, CancellationToken ct)
    {
        if (await KapsamliAsync(id, svc, dbf, kullanici, ct) is null) return Bulunamadi();
        var tarih = F5Ortak.Utc(istek?.OdemeTarihi);
        Alanli("odemeTarihi", () => TarihPolitikasi.ParaTarihi(tarih, "Taksit ödeme"));
        if (!await svc.OdemeIsaretleKilitliAsync(id, true, tarih, ct)) return Bulunamadi();
        return await DtoAsync(id, svc, dbf, kullanici, ct) is { } d ? TypedResults.Ok(d) : Bulunamadi();
    }

    private static async Task<Results<Ok<MusteriTaksitSatiri>, ProblemHttpResult>> GeriAl(
        Guid id, MusteriTaksitService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, CancellationToken ct)
    {
        if (await KapsamliAsync(id, svc, dbf, kullanici, ct) is null) return Bulunamadi();
        if (!await svc.OdemeIsaretleKilitliAsync(id, false, null, ct)) return Bulunamadi();
        return await DtoAsync(id, svc, dbf, kullanici, ct) is { } d ? TypedResults.Ok(d) : Bulunamadi();
    }
}
