using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.AracFinans;
using RentACar.Web.Api.Rezervasyon;

namespace RentACar.Web.Api.ServiceInsurance;

/// <summary>
/// F9.1 — endpoint-layer rules shared by the servis / sigorta / vade / fiyat-tarife endpoints. NO business logic:
/// input limits (<c>numeric(19,4)</c>, varchar lengths, money scale), vehicle-branch scope (child records follow the
/// VEHICLE's branch — same rule as F6.1b), primary-key-as-idempotency-key detection.
/// </summary>
internal static class ServiceInsuranceShared
{
    public static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Money written as a payment: 2 decimals max (kuruş), positive, below <c>numeric(19,4)</c>'s 15 integer digits.</summary>
    public const int PaymentScale = 2;

    public static ProblemHttpResult NotFound(string detail) => F5Shared.NotFound(detail);

    /// <summary>
    /// Payment amount (F8.1b M2 lesson): more than 2 decimals is REJECTED (not rounded — the DB would silently round
    /// and an exact retry would read as "different content"); 0 and negatives rejected; below 10^15.
    /// </summary>
    public static void PaymentAmount(decimal? value, string field)
    {
        if (value is not { } v) return;
        if (v <= 0m) throw new ValidationException("Tutar pozitif olmalıdır.", field);
        if (v >= VehicleFinanceShared.AmountUpperLimit) throw new ValidationException("Tutar çok büyük.", field);
        VehicleFinanceShared.EnsureMaxScale(v, PaymentScale, field);
    }

    /// <summary>Record amount (debt/premium/fee on a NEW record): not negative, 2 decimals max, below 10^15.</summary>
    public static void RecordAmount(decimal? value, string field, bool allowNegative = false)
    {
        if (value is not { } v) return;
        if (!allowNegative && v < 0m) throw new ValidationException("Tutar negatif olamaz.", field);
        if (Math.Abs(v) >= VehicleFinanceShared.AmountUpperLimit) throw new ValidationException("Tutar çok büyük.", field);
        VehicleFinanceShared.EnsureMaxScale(v, PaymentScale, field);
    }

    /// <summary>Catalog price/amount: column scale (4) and size; checked ONLY when the value changed (legacy rows
    /// stay editable — #273/#285/#283 lesson).</summary>
    public static void CatalogAmount(decimal? value, decimal? previous, string field, int scale = 4)
    {
        if (value is not { } v || value == previous) return;
        if (Math.Abs(v) >= VehicleFinanceShared.AmountUpperLimit) throw new ValidationException("Tutar çok büyük.", field);
        VehicleFinanceShared.EnsureMaxScale(v, scale, field);
    }

    /// <summary>Ratio column <c>numeric(9,4)</c> (percent or fraction): scale 4, |v| &lt; 10^5; only when changed.</summary>
    public static void Ratio(decimal? value, decimal? previous, string field)
    {
        if (value is not { } v || value == previous) return;
        if (Math.Abs(v) >= 100_000m) throw new ValidationException("Oran çok büyük.", field);
        VehicleFinanceShared.EnsureMaxScale(v, 4, field);
    }

    /// <summary>Text column length (trimmed, as the services store it).</summary>
    public static void Text(string? value, int max, string field) => VehicleFinanceShared.Text(value, max, field);

    /// <summary>Integer sanity window (days, km, …) — values are stored as <c>int</c>.</summary>
    public static void IntRange(int? value, int min, int max, string field)
    {
        if (value is { } v && (v < min || v > max))
            throw new ValidationException($"{min} ile {max} arasında olmalıdır.", field);
    }

    /// <summary>Date sanity (optional): 1900–2100, as the query helpers; DB receives UTC.</summary>
    public static DateTimeOffset? Date(DateTimeOffset? value, string field)
    {
        if (value is not { } v) return null;
        var u = v.ToUniversalTime();
        if (u.Year is < F5Shared.MinQueryYear or > F5Shared.MaxQueryYear)
            throw new ValidationException($"Tarih {F5Shared.MinQueryYear} ile {F5Shared.MaxQueryYear} yılları arasında olmalıdır.", field);
        return u;
    }

    public static DateTimeOffset RequiredDate(DateTimeOffset? value, string field)
        => Date(value, field) ?? throw new ValidationException("Tarih zorunludur.", field);

    /// <summary>"Kasa" / "Banka" (case-insensitive); anything else 400 on <paramref name="field"/>.</summary>
    public static LedgerAccountType CashAccount(string? value, string field = "hesap")
    {
        if (string.Equals(value?.Trim(), "Kasa", StringComparison.OrdinalIgnoreCase)) return LedgerAccountType.Kasa;
        if (string.Equals(value?.Trim(), "Banka", StringComparison.OrdinalIgnoreCase)) return LedgerAccountType.Banka;
        throw new ValidationException("Ödeme hesabı Kasa veya Banka olmalıdır.", field);
    }

    /// <summary>Primary-key collision (the id was derived from the <c>Idempotency-Key</c>): a concurrent duplicate.</summary>
    public static bool IsPrimaryKeyViolation(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } p
                && (p.ConstraintName?.StartsWith("PK_", StringComparison.Ordinal) ?? false))
                return true;
        return false;
    }

    /// <summary>Vehicle id → plate (RLS-scoped single query).</summary>
    public static Task<Dictionary<Guid, string>> PlatesAsync(IDbContextFactory<AppDbContext> dbf, IEnumerable<Guid> ids,
        CancellationToken ct) => F5Shared.PlatesAsync(dbf, ids, ct);

    /// <summary>Rows visible to the caller through the VEHICLE's branch (list filter).</summary>
    public static async Task<List<T>> VisibleAsync<T>(IDbContextFactory<AppDbContext> dbf, ICurrentUser user,
        IEnumerable<T> rows, Func<T, Guid?> vehicleOf, CancellationToken ct)
    {
        var list = rows.ToList();
        var f = Application.Authorization.BranchScope.EffectiveFilter(user);
        if (f.Unrestricted) return list;
        var branches = await VehicleFinanceShared.VehicleBranchesAsync(dbf,
            list.Select(vehicleOf).Where(v => v is not null).Select(v => v!.Value), ct);
        return list.Where(r => VehicleFinanceShared.IsVisible(f, vehicleOf(r), branches)).ToList();
    }

    /// <summary>Single-record gate through the vehicle's branch: out of scope → 403 (called BEFORE state checks).</summary>
    public static Task RecordScopeAsync(IDbContextFactory<AppDbContext> dbf, ICurrentUser user, Guid? vehicleId,
        CancellationToken ct) => VehicleFinanceShared.RecordScopeAsync(dbf, user, vehicleId, ct);

    /// <summary>Write-time vehicle: must exist in the tenant (400) and be in the caller's branch scope (403).</summary>
    public static Task VehicleForWriteAsync(IDbContextFactory<AppDbContext> dbf, ICurrentUser user, Guid? vehicleId,
        string field, CancellationToken ct) => VehicleFinanceShared.VehicleWriteAsync(dbf, user, vehicleId, field, required: true, ct);

    public static string Money(decimal value) => value.ToString("N2", Tr);

    public static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>ServiceRecord / policy etc. have no plate column; resolves the plate for a single vehicle.</summary>
    public static async Task<string> PlateAsync(IDbContextFactory<AppDbContext> dbf, Guid vehicleId, CancellationToken ct)
        => F5Shared.Plate(await F5Shared.PlatesAsync(dbf, [vehicleId], ct), vehicleId);

    /// <summary>Customer display name under the KVKK rule (<see cref="Kira.CustomerView"/>); null id → null.</summary>
    public static async Task<Dictionary<Guid, F5Shared.CariGorunum>> CustomersAsync(IDbContextFactory<AppDbContext> dbf,
        IEnumerable<Guid?> ids, CancellationToken ct)
        => await F5Shared.CustomersAsync(dbf, ids.Where(i => i is not null).Select(i => i!.Value), ct);

    public static string? CustomerName(Dictionary<Guid, F5Shared.CariGorunum> map, Guid? id)
        => id is { } c ? F5Shared.CustomerName(map, c) : null;

    /// <summary>Tenant-scoped existence of a vehicle (unused ids / other tenant → false).</summary>
    public static async Task<bool> VehicleExistsAsync(IDbContextFactory<AppDbContext> dbf, Guid id, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        return await db.Set<Vehicle>().AsNoTracking().AnyAsync(v => v.Id == id, ct);
    }
}
