using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Bookings;

/// <summary>
/// Kira sözleşmesi iş mantığı. Doğrudan kira oluşturma + iptal. Double-booking,
/// DB exclusion constraint ile garanti edilir (eşzamanlı istekte tek kazanan).
/// Teslim/dönüş PR #4'te.
/// </summary>
public sealed class RentalService(IBookingRepository repository)
{
    private readonly IBookingRepository _repository = repository;

    public Task<IReadOnlyList<RentalContract>> ListAsync(CancellationToken ct = default)
        => _repository.ListRentalsAsync(ct);

    public Task<RentalContract?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindRentalAsync(id, ct);

    public async Task<Guid> CreateDirectAsync(BookingInput input, CancellationToken ct = default)
    {
        BookingMath.Validate(input);
        var (gun, tutar) = BookingMath.Compute(input);

        // Yumuşak ön-kontrol (kullanıcı dostu hata); kesin garanti exclusion constraint.
        if (await _repository.HasOverlappingActiveRentalAsync(input.VehicleId, input.BasTar, input.BitTar, null, ct))
            throw new AvailabilityConflictException();

        var contract = new RentalContract
        {
            Durum = RentalStatus.Kirada,
            MusteriId = input.MusteriId,
            VehicleId = input.VehicleId,
            BasTar = input.BasTar,
            BitTar = input.BitTar,
            CikisOfisi = input.CikisOfisi,
            DonusOfisi = input.DonusOfisi,
            Gun = gun,
            GunlukUcret = input.GunlukUcret,
            Tutar = tutar,
            Tahsilat = 0m,
            Bakiye = tutar,
            Aciklama = input.Aciklama
        };
        await _repository.CreateRentalAsync(contract, ct);
        return contract.Id;
    }

    public async Task<bool> CancelAsync(Guid id, CancellationToken ct = default)
    {
        return await _repository.UpdateRentalAsync(id, c =>
        {
            if (c.Durum != RentalStatus.Kirada)
                throw new ValidationException($"Kira '{c.Durum}' durumundayken iptal edilemez.");
            c.Durum = RentalStatus.Iptal;
            c.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }
}
