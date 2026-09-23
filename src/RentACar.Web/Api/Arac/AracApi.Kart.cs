using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Application.Details;
using RentACar.Application.Vehicles;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Api.Arac;

public static partial class AracApi
{
    private static readonly (string, string)[] KmKurallari =
    [
        ("KM negatif", "km"), ("KM geriye gidemez", "km"), ("KM zorunludur", "km"), ("KM tarihi", "tarih"),
    ];

    // ================================================================== kart + detay

    /// <summary>Araç kartı (düzenleme formu). Sürüm alanlardan ÖNCE okunur; kapsam dışı 403, yok 404.</summary>
    private static async Task<Results<Ok<AracKartDto>, ProblemHttpResult>> Kart(Guid id, VehicleService araclar, CancellationToken ct)
        => await KartAsync(id, araclar, ct) is { } k ? TypedResults.Ok(k) : Bulunamadi();

    private static async Task<AracKartDto?> KartAsync(Guid id, VehicleService araclar, CancellationToken ct)
    {
        var surum = await araclar.SurumAsync(id, ct);
        var v = await araclar.GetAsync(id, ct); // şube kapsamı → YetkiYokException (403)
        return v is null ? null : AracGirdi.Kart(v, surum);
    }

    /// <summary>Araç detayı: kira/servis/ceza/hasar geçmişi + son 10 km kaydı. Kapsam kapısı DetailService'ten ÖNCE.</summary>
    private static async Task<Results<Ok<AracDetayDto>, ProblemHttpResult>> Detay(
        Guid id, VehicleService araclar, DetailService detaylar, CancellationToken ct)
    {
        if (await araclar.GetAsync(id, ct) is null) return Bulunamadi();
        var d = await detaylar.GetVehicleAsync(id, ct);
        if (d is null) return Bulunamadi();
        var km = await araclar.KmLoglariAsync(id, 10, ct);
        var v = d.Vehicle;
        return TypedResults.Ok(new AracDetayDto(v.Id, v.Plaka, v.Marka, v.Grup, v.Sube, v.Durum.ToString(), v.Km,
            d.Rentals.Select(r => new AracKiraOzeti(r.Id, r.SozlesmeNo, r.BasTar, r.BitTar, r.Durum.ToString(), r.GenelToplam)).ToList(),
            d.Services.Select(s => new AracServisOzeti(s.Id, s.No, s.Tip.ToString(), s.GirisTarihi, s.Durum.ToString(), s.ToplamIscilik)).ToList(),
            d.Penalties.Select(p => new AracCezaOzeti(p.Id, p.No, p.CezaTuru?.ToString(), p.TebligTarihi, p.Tutar, p.Durum.ToString())).ToList(),
            d.Damages.Select(h => new AracHasarOzeti(h.Id, h.No, h.AcilisTarihi, h.TahminiTutar, h.Durum.ToString())).ToList(),
            km.Select(k => new AracKmKaydi(k.Tarih, k.Km, k.Kaynak.ToString())).ToList()));
    }

    // ================================================================== yazmalar

    private static async Task<VehicleInput> GirdiAsync(
        AracIstegi i, IBranchRepository subeler, ICurrentUser kullanici, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        AracGirdi.Sinirla(i);
        var girdi = AracGirdi.Girdi(i); // enum adları → 400 errors[alan]
        await AracGirdi.HedefSubeKapsamiAsync(subeler, kullanici, i.Sube, ct);
        await AracGirdi.CariVarligiAsync(dbf, i.KiraMusteriId, ct);
        return girdi;
    }

    /// <summary>Plaka çakışması alt tip (<see cref="DuplicatePlakaException"/>) — alanı plaka olarak işaretle.</summary>
    private static async Task<T> PlakaAlaniAsync<T>(Func<Task<T>> is_)
    {
        try { return await is_(); }
        catch (DuplicatePlakaException ex) { throw new ValidationException(ex.Message, "plaka"); }
    }

    private static async Task<Results<Created<AracKartDto>, ProblemHttpResult>> Olustur(
        AracIstegi istek, VehicleService araclar, IBranchRepository subeler, ICurrentUser kullanici,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var girdi = await GirdiAsync(istek, subeler, kullanici, dbf, ct);
        var id = await PlakaAlaniAsync(() => araclar.CreateAsync(girdi, ct));
        var kart = await KartAsync(id, araclar, ct);
        return kart is null ? Bulunamadi() : TypedResults.Created($"{Kok}/{id}", kart);
    }

    /// <summary>Tam değiştirme. Sıra: varlık+kapsam (404/403) → surum zorunlu → girdi → kilit altında sürüm (409).</summary>
    private static async Task<Results<Ok<AracKartDto>, ProblemHttpResult>> Guncelle(
        Guid id, AracGuncelleIstegi istek, VehicleService araclar, IBranchRepository subeler, ICurrentUser kullanici,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await araclar.GetAsync(id, ct) is null) return Bulunamadi();
        if (string.IsNullOrWhiteSpace(istek.Surum))
            throw new ValidationException("Kayıt sürümü (surum) zorunludur; kaydı yeniden açın.", "surum");
        var girdi = await GirdiAsync(istek, subeler, kullanici, dbf, ct);
        if (!await PlakaAlaniAsync(() => araclar.UpdateAsync(id, girdi, istek.Surum, ct))) return Bulunamadi();
        return await KartAsync(id, araclar, ct) is { } k ? TypedResults.Ok(k) : Bulunamadi();
    }

    /// <summary>Kalıcı silme (OperationsDelete). Başka kayıtlarda kullanılan araç FK nedeniyle silinemez → 400.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> Sil(Guid id, VehicleService araclar, CancellationToken ct)
    {
        if (await araclar.GetAsync(id, ct) is null) return Bulunamadi();
        try
        {
            return await araclar.DeleteAsync(id, ct) ? TypedResults.NoContent() : Bulunamadi();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            throw new ValidationException(
                "Araç başka kayıtlarda (kira, rezervasyon, servis…) kullanıldığı için silinemez; filo çıkışı/pasif sebebiyle kapatın.");
        }
    }

    /// <summary>Manuel odometre girişi (km serisi + araç km aynı işlemde; geriye gidemez, tarih gelecekte olamaz).</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> KmGir(
        Guid id, AracKmIstegi istek, VehicleService araclar, CancellationToken ct)
    {
        if (await araclar.GetAsync(id, ct) is null) return Bulunamadi();
        if (istek.Km is not { } km) throw new ValidationException("KM zorunludur.", "km");
        if (km > 10_000_000) throw new ValidationException("KM 10.000.000'dan büyük olamaz.", "km");
        await araclar.ManuelKmGirAsync(id, km, istek.Tarih?.ToUniversalTime(), ct);
        return TypedResults.NoContent();
    }
}
