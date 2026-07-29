using RentACar.Domain.Entities;

namespace RentACar.Application.PublicSite;

/// <summary>PR-8: halka açık site talebi (lead) kalıcılığı.</summary>
public interface IPublicBookingRequestRepository
{
    Task AddAsync(PublicBookingRequest talep, CancellationToken ct = default);

    Task<IReadOnlyList<PublicBookingRequest>> ListAsync(CancellationToken ct = default);

    Task<PublicBookingRequest?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>ATOMİK claim: `Durum` yalnız `Yeni` iken hedef duruma çeker. TEK SQL UPDATE — iki personel
    /// aynı anda "Dönüştür"e basarsa yalnız BİRİ true alır (yarış güvenli). Etkilenen satır 0 ise talep
    /// zaten işlenmiştir.</summary>
    Task<bool> TryClaimAsync(Guid id, PublicBookingRequestDurum hedef, CancellationToken ct = default);

    /// <summary>Claim SONRASI rezervasyon id'sini yazar (dönüştürme başarıyla tamamlandığında).</summary>
    Task SetDonusenReservationAsync(Guid id, Guid reservationId, CancellationToken ct = default);

    /// <summary>Claim'i GERİ ALIR (`Durum=Yeni`, `DonusenReservationId=null`) — dönüştürmenin Cari/Rezervasyon
    /// aşaması patlarsa satır "Donustu ama rezervasyonsuz" YARIM kalmasın, personel tekrar deneyebilsin.</summary>
    Task ReleaseClaimAsync(Guid id, CancellationToken ct = default);

    /// <summary>Telefonla mevcut Cari arama (dönüştürmede yeniden müşteri yaratmamak için). Normalize
    /// edilmiş (yalnız rakam) karşılaştırma — "0555 111 22 33" ile "05551112233" AYNI sayılır.</summary>
    Task<Guid?> FindCustomerIdByPhoneAsync(string telefon, CancellationToken ct = default);
}
