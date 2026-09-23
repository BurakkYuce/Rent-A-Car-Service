using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Bookings;

/// <summary>
/// Rezervasyon listesi filtreleri (FAZ-48; <see cref="RentalFilter"/> deseni). Kapsam, servis
/// tarafından rol bazlı şube kapsamına ayarlanır — çağıran GENİŞLETEMEZ.
/// </summary>
public sealed class ReservationFilter
{
    /// <summary>Rez no / müşteri adı / plaka içeren arama (case-insensitive).</summary>
    public string? Query { get; set; }
    public ReservationStatus? Durum { get; set; }
    /// <summary>Başlangıç tarihi alt sınırı (dahil).</summary>
    public DateTimeOffset? TarihMin { get; set; }
    /// <summary>Başlangıç tarihi üst sınırı (dahil — web ucu .AddDays(1).AddTicks(-1) ile gün-sonuna taşır).</summary>
    public DateTimeOffset? TarihMax { get; set; }
    /// <summary>Rezervasyon kaynağı (tam eşleşme, case-insensitive).</summary>
    public string? Kaynak { get; set; }
    /// <summary>Rol bazlı şube KAPSAMI (C4; servis ayarlar) — türetilmiş CikisSubeId + ofis metni.</summary>
    public Authorization.BranchScope.BranchFilter Kapsam { get; set; }
}

/// <summary>
/// Rezervasyon liste satırı: kaydın KENDİSİ + görüntüleme adları (müşteri/plaka/telefon) tek
/// sorguda çözülmüş. Entity taşınır çünkü liste ekranındaki düzenleme formu tüm alanları
/// (Ota*, kampanya kodu, fiyat türü…) prefill eder; adlar ayrı taşınır çünkü ekran artık
/// PII çözen <c>CustomerService.ListAsync</c>'e bağımlı değil. <c>MusteriAnonimAd</c>: KVKK bayrağı —
/// <c>MusteriAd</c> ham addır, /api/ui yüzeyleri <c>MusteriGorunumu.ListeAdi</c> ile maskeler.
/// </summary>
public sealed record ReservationRow(Reservation Rez, string MusteriAd, string? CepTel, string Plaka, bool MusteriAnonimAd = false);
