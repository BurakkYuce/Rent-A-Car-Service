using System.Security.Cryptography;
using System.Text;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Application.Integrations;
using RentACar.Application.Periods;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Hgs;

public sealed record HgsReflectionResult(int GecisSayisi, decimal ToplamGecis, decimal YansitilanTutar);

/// <summary>
/// HGS geçişlerini müşteriye yansıtma: IHgsService'ten (v1 stub) geçişleri çeker, hizmet
/// oranıyla (örn. 1.03 = +%3) çarpıp cari'ye yansıtır (Borç Cari / Alacak Gelir, dengeli).
/// Gerçek HGS API'si Faz 3'te stub'ın arkasına gelir; stub boş döner → no-op.
/// Para yazar → FinanceWrite guard (denetim O7-HGS: guard'sızdı, Operator cari borçlandırabiliyordu).
/// </summary>
public sealed class HgsReflectionService(
    IHgsService hgs, ILedgerPoster ledger, IPeriodLockGuard periodLock, ICurrentUser currentUser)
{
    public async Task<HgsReflectionResult> ReflectAsync(
        Guid customerId, string plate, DateTimeOffset from, DateTimeOffset to,
        decimal serviceRate = 1.03m, CancellationToken ct = default)
    {
        RentACar.Application.Authorization.PermissionGuard.Require(
            currentUser, RentACar.Application.Authorization.Permission.FinanceWrite);
        if (customerId == Guid.Empty) throw new ValidationException("Cari seçilmelidir.");
        if (serviceRate <= 0) throw new ValidationException("Hizmet oranı pozitif olmalıdır.");

        var crossings = await hgs.GetCrossingsAsync(plate, from, to, ct);
        var total = crossings.Sum(c => c.Tutar);
        var reflected = Math.Round(total * serviceRate, 2, MidpointRounding.AwayFromZero);

        if (reflected <= 0)
            return new HgsReflectionResult(crossings.Count, total, 0m);

        await periodLock.EnsureOpenAsync(DateTimeOffset.UtcNow, ct); // dönem kilidi: yansıtma bugün tarihli

        // İDEMPOTENT: SourceId aynı (cari, plaka, dönem) için DETERMİNİSTİK türetilir →
        // aynı yansıtmanın tekrarı (retry/çift-tık/batch yeniden-çalışma) defterde kısmi
        // unique index'e takılır ve LedgerPoster tarafından no-op olarak yutulur (çift
        // borçlanma olmaz). Farklı dönem/plaka → farklı SourceId → meşru ikinci yansıtma serbest.
        var sourceId = DeterministicSourceId(customerId, plate, from, to);
        await ledger.PostAsync(
        [
            new AccountLedgerEntry
            {
                EntryDateUtc = DateTimeOffset.UtcNow, AccountType = LedgerAccountType.Cari, AccountRef = customerId,
                Direction = LedgerDirection.Debit, Amount = new Money(reflected, "TRY", 1m),
                SourceType = "Hgs", SourceId = sourceId, Description = $"HGS yansıtma {plate}"
            },
            new AccountLedgerEntry
            {
                EntryDateUtc = DateTimeOffset.UtcNow, AccountType = LedgerAccountType.Gelir, AccountRef = null,
                Direction = LedgerDirection.Credit, Amount = new Money(reflected, "TRY", 1m),
                SourceType = "Hgs", SourceId = sourceId, Description = $"HGS yansıtma {plate}"
            }
        ], ct);

        return new HgsReflectionResult(crossings.Count, total, reflected);
    }

    /// <summary>
    /// (cari, plaka, dönem)'den deterministik GUID — idempotency anahtarı (kriptografik
    /// güvenlik amacı yok, yalnız kararlı kimlik). Tenant zaten unique index'in parçası.
    /// </summary>
    private static Guid DeterministicSourceId(Guid customerId, string plate, DateTimeOffset from, DateTimeOffset to)
    {
        var key = $"{customerId:N}|{(plate ?? string.Empty).Trim().ToUpperInvariant()}|{from.UtcTicks}|{to.UtcTicks}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash);
    }
}
