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

public static partial class VehicleApi
{
    private static readonly (string, string)[] KmRules =
    [
        ("KM negatif", "km"), ("KM geriye gidemez", "km"), ("KM zorunludur", "km"), ("KM tarihi", "tarih"),
    ];

    // ================================================================== kart + detay

    /// <summary>Araç kartı (düzenleme formu). Sürüm alanlardan ÖNCE okunur; kapsam dışı 403, yok 404.</summary>
    private static async Task<Results<Ok<AracKartDto>, ProblemHttpResult>> Card(Guid id, VehicleService araclar, CancellationToken ct)
        => await CardAsync(id, araclar, ct) is { } k ? TypedResults.Ok(k) : NotFoundProblem();

    private static async Task<AracKartDto?> CardAsync(Guid id, VehicleService vehicles, CancellationToken ct)
    {
        var version = await vehicles.VersionAsync(id, ct);
        var v = await vehicles.GetAsync(id, ct); // şube kapsamı → YetkiYokException (403)
        return v is null ? null : VehicleFormInput.Card(v, version);
    }

    /// <summary>Araç detayı: kira/servis/ceza/hasar geçmişi + son 10 km kaydı. Kapsam kapısı DetailService'ten ÖNCE.</summary>
    private static async Task<Results<Ok<AracDetayDto>, ProblemHttpResult>> Detail(
        Guid id, VehicleService araclar, DetailService detaylar, CancellationToken ct)
    {
        if (await araclar.GetAsync(id, ct) is null) return NotFoundProblem();
        var d = await detaylar.GetVehicleAsync(id, ct);
        if (d is null) return NotFoundProblem();
        var km = await araclar.KmLogsAsync(id, 10, ct);
        var v = d.Vehicle;
        return TypedResults.Ok(new AracDetayDto(v.Id, v.Plaka, v.Marka, v.Grup, v.Sube, v.Durum.ToString(), v.Km,
            d.Rentals.Select(r => new AracKiraOzeti(r.Id, r.SozlesmeNo, r.BasTar, r.BitTar, r.Durum.ToString(), r.GenelToplam)).ToList(),
            d.Services.Select(s => new AracServisOzeti(s.Id, s.No, s.Tip.ToString(), s.GirisTarihi, s.Durum.ToString(), s.ToplamIscilik)).ToList(),
            d.Penalties.Select(p => new AracCezaOzeti(p.Id, p.No, p.CezaTuru?.ToString(), p.TebligTarihi, p.Tutar, p.Durum.ToString())).ToList(),
            d.Damages.Select(h => new AracHasarOzeti(h.Id, h.No, h.AcilisTarihi, h.TahminiTutar, h.Durum.ToString())).ToList(),
            km.Select(k => new AracKmKaydi(k.Tarih, k.Km, k.Kaynak.ToString())).ToList()));
    }

    // ================================================================== yazmalar

    private static async Task<VehicleInput> InputAsync(
        AracIstegi i, IBranchRepository branches, ICurrentUser user, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        VehicleFormInput.Limit(i);
        var input = VehicleFormInput.Input(i); // enum adları → 400 errors[alan]
        await VehicleFormInput.TargetBranchScopeAsync(branches, user, i.Sube, ct);
        await VehicleFormInput.CustomerExistenceAsync(dbf, i.KiraMusteriId, ct);
        return input;
    }

    /// <summary>Plaka çakışması alt tip (<see cref="DuplicatePlakaException"/>) — alanı plaka olarak işaretle.</summary>
    private static async Task<T> PlateFieldAsync<T>(Func<Task<T>> is_)
    {
        try { return await is_(); }
        catch (DuplicatePlakaException ex) { throw new ValidationException(ex.Message, "plaka"); }
    }

    private static async Task<Results<Created<AracKartDto>, ProblemHttpResult>> Create(
        AracIstegi istek, VehicleService araclar, IBranchRepository subeler, ICurrentUser kullanici,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var input = await InputAsync(istek, subeler, kullanici, dbf, ct);
        var id = await PlateFieldAsync(() => araclar.CreateAsync(input, ct));
        var card = await CardAsync(id, araclar, ct);
        return card is null ? NotFoundProblem() : TypedResults.Created($"{Root}/{id}", card);
    }

    /// <summary>Tam değiştirme. Sıra: varlık+kapsam (404/403) → surum zorunlu → girdi → kilit altında sürüm (409).</summary>
    private static async Task<Results<Ok<AracKartDto>, ProblemHttpResult>> Update(
        Guid id, AracGuncelleIstegi istek, VehicleService araclar, IBranchRepository subeler, ICurrentUser kullanici,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await araclar.GetAsync(id, ct) is null) return NotFoundProblem();
        if (string.IsNullOrWhiteSpace(istek.Surum))
            throw new ValidationException("Kayıt sürümü (surum) zorunludur; kaydı yeniden açın.", "surum");
        var input = await InputAsync(istek, subeler, kullanici, dbf, ct);
        if (!await PlateFieldAsync(() => araclar.UpdateAsync(id, input, istek.Surum, ct))) return NotFoundProblem();
        return await CardAsync(id, araclar, ct) is { } k ? TypedResults.Ok(k) : NotFoundProblem();
    }

    /// <summary>Kalıcı silme (OperationsDelete). Başka kayıtlarda kullanılan araç FK nedeniyle silinemez → 400.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> Delete(Guid id, VehicleService araclar, CancellationToken ct)
    {
        if (await araclar.GetAsync(id, ct) is null) return NotFoundProblem();
        try
        {
            return await araclar.DeleteAsync(id, ct) ? TypedResults.NoContent() : NotFoundProblem();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            throw new ValidationException(
                "Araç başka kayıtlarda (kira, rezervasyon, servis…) kullanıldığı için silinemez; filo çıkışı/pasif sebebiyle kapatın.");
        }
    }

    /// <summary>Manuel odometre girişi (km serisi + araç km aynı işlemde; geriye gidemez, tarih gelecekte olamaz).</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> EnterKm(
        Guid id, AracKmIstegi istek, VehicleService araclar, CancellationToken ct)
    {
        if (await araclar.GetAsync(id, ct) is null) return NotFoundProblem();
        if (istek.Km is not { } km) throw new ValidationException("KM zorunludur.", "km");
        if (km > 10_000_000) throw new ValidationException("KM 10.000.000'dan büyük olamaz.", "km");
        await araclar.EnterManualKmAsync(id, km, istek.Tarih?.ToUniversalTime(), ct);
        return TypedResults.NoContent();
    }
}
