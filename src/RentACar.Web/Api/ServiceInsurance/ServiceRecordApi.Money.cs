using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Application.ServiceRecords;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.AracFinans;
using RentACar.Web.Common;
using S = RentACar.Web.Api.ServiceInsurance.ServiceInsuranceShared;

namespace RentACar.Web.Api.ServiceInsurance;

internal static partial class ServiceRecordApi
{
    // ------------------------------------------------------------------ kalem (toplamIscilik → rücu tabanı)

    /// <summary>Line limits: money 2 decimals (kuruş), quantity 4, VAT rate 0..1 (scale 4); unit × quantity below 10^15.</summary>
    private static void LineLimits(ServiceLineRequest l)
    {
        S.Text(l.Aciklama, 512, "aciklama");
        S.RecordAmount(l.Tutar, "tutar");
        S.RecordAmount(l.BirimFiyat, "birimFiyat");
        S.RecordAmount(l.Indirim, "indirim");
        if (l.Miktar is { } q)
        {
            if (q < 0m || q >= 1_000_000m) throw new ValidationException("Miktar 0 ile 1.000.000 arasında olmalıdır.", "miktar");
            AracFinansOrtak.EnsureMaxScale(q, 4, "miktar");
        }
        if (l.KdvOran is { } k) AracFinansOrtak.EnsureMaxScale(k, 4, "kdvOran");
        if ((l.BirimFiyat ?? 0m) * (l.Miktar ?? 1m) >= AracFinansOrtak.TutarUstSiniri)
            throw new ValidationException("Birim fiyat × miktar çok büyük.", "miktar");
    }

    private static ServiceLineInput LineInput(ServiceLineRequest l, Guid? id) => new()
    {
        Id = id, Aciklama = l.Aciklama ?? "", Tutar = l.Tutar, BirimFiyat = l.BirimFiyat, Miktar = l.Miktar, Indirim = l.Indirim,
        KdvOran = l.KdvOran,
    };

    private static async Task<Results<Ok<ServiceRecordDetail>, ProblemHttpResult>> AddLine(
        Guid id, ServiceLineRequest r, HttpContext http, ServiceRecordService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser user, CancellationToken ct)
    {
        var key = IdempotencyBasligi.ZorunluAnahtar(http);
        if (await ScopedAsync(id, svc, dbf, user, ct) is null) return NotFound(); // kapsam durumdan ÖNCE
        await ExistingLineAsync(dbf, key, id, r, ct);                            // (1) ÖNCE mevcut kalem
        LineLimits(r);
        bool ok;
        try
        {
            ok = await svc.AddItemAsync(id, LineInput(r, key), ct);
        }
        catch (DuplicateOperationException ex) when (ex.Existing is null)
        {
            await ExistingLineAsync(dbf, key, id, r, ct); // race: the lock let the first one in — report it
            throw;
        }
        if (!ok) return NotFound();
        return TypedResults.Ok((await DetailAsync(id, http, svc, dbf, user, ct))!);
    }

    /// <summary>Line already written with this key → 409 <c>mukerrer</c>; <c>mevcut</c> only when it belongs to this record.</summary>
    private static async Task ExistingLineAsync(IDbContextFactory<AppDbContext> dbf, Guid key, Guid recordId, ServiceLineRequest r,
        CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var l = await db.Set<ServiceLine>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == key, ct);
        if (l is null) return;
        if (l.ServiceRecordId != recordId) throw new DuplicateOperationException(OtherOperation);
        var same = l.Aciklama == (r.Aciklama ?? "").Trim() && l.BirimFiyat == r.BirimFiyat && l.Indirim == r.Indirim
                   && l.KdvOran == r.KdvOran && (r.Tutar is not { } t || l.Tutar == t)
                   && (r.Miktar is not { } q || l.Miktar == q);
        throw new DuplicateOperationException(
            same ? "Bu kalem zaten eklendi; yeni kalem yazılmadı."
                 : "Bu işlem anahtarıyla başka içerikte bir kalem eklenmiş; girdiğiniz kalem YAZILMADI. Kaydı kontrol edin.",
            new MevcutIslem(l.Id, l.Aciklama, l.Tutar, "TRY", same));
    }

    // ------------------------------------------------------------------ rücu yansıtma (PARA: Borç Cari / Alacak Gelir)

    private static async Task<Results<Ok<ServiceRecordDetail>, ProblemHttpResult>> Reflect(
        Guid id, ServiceReflectRequest r, HttpContext http, ServiceRecordService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser user, CancellationToken ct)
    {
        if (await ScopedAsync(id, svc, dbf, user, ct) is not { } rec) return NotFound(); // kapsam durumdan ÖNCE
        if (rec.Yansitildi) throw Reflected(rec, r);                                      // (1) ÖNCE mevcut yansıtma
        await AracFinansOrtak.CariVarAsync(dbf, r.CariId, "cariId", zorunlu: true, ct);
        try
        {
            await svc.ReflectAsync(id, r.CariId!.Value, ct: ct);
        }
        catch (ValidationException ex) when (ex.GetType() == typeof(ValidationException) && ex.Message == "Servis maliyeti zaten yansıtıldı.")
        {
            if (await svc.GetAsync(id, ct) is { Yansitildi: true } won) throw Reflected(won, r);
            throw;
        }
        return TypedResults.Ok((await DetailAsync(id, http, svc, dbf, user, ct))!);
    }

    private static DuplicateOperationException Reflected(ServiceRecord rec, ServiceReflectRequest r)
    {
        var same = rec.YansitilanCariId == r.CariId;
        return new DuplicateOperationException(
            same ? $"Servis maliyeti zaten yansıtıldı (No {rec.No}, {S.Money(rec.YansitilanTutar)} TRY); yeni kayıt yazılmadı."
                 : $"Servis maliyeti başka bir cariye yansıtılmış (No {rec.No}); girdiğiniz yansıtma YAZILMADI.",
            new MevcutIslem(rec.Id, rec.No, rec.YansitilanTutar, "TRY", same));
    }
}
