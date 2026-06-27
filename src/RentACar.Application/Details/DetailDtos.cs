using RentACar.Domain.Entities;

namespace RentACar.Application.Details;

/// <summary>Araç 360° görünümü: araç + kira geçmişi + servis + ceza + hasar.</summary>
public sealed record VehicleDetailDto(
    Vehicle Vehicle,
    IReadOnlyList<RentalContract> Rentals,
    IReadOnlyList<ServiceRecord> Services,
    IReadOnlyList<Penalty> Penalties,
    IReadOnlyList<DamageFile> Damages);

/// <summary>Cari 360° görünümü: cari + bakiye + kiralar + son ekstre satırları.</summary>
public sealed record CustomerDetailDto(
    Customer Customer,
    decimal Bakiye,
    IReadOnlyList<RentalContract> Rentals,
    IReadOnlyList<AccountLedgerEntry> RecentLedger);
