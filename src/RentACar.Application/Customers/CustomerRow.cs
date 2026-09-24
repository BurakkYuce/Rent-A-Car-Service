using RentACar.Domain.Enums;

namespace RentACar.Application.Customers;

/// <summary>
/// Cari liste satırı: cari kimlik + CRM bayrakları + kira agregaları (adet/ciro/son kira).
/// Salt-okunur projeksiyon — Customer + Rentals(GROUP BY MusteriId) birleşimi (İptal hariç).
/// </summary>
public sealed class CustomerRow
{
    public Guid Id { get; init; }
    public CariType Tip { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? TcKimlik { get; init; }
    public string? VergiNo { get; init; }
    public string? CepTel { get; init; }
    public string? Email { get; init; }
    public string? Il { get; init; }
    public string? Kaynak { get; init; }

    public bool KaraListe { get; init; }
    public bool Pasif { get; init; }
    public bool Uyari { get; init; }
    public bool IysIzinli { get; init; }

    /// <summary>İptal olmayan kira adedi.</summary>
    public int KiraAdet { get; init; }
    /// <summary>İptal olmayan kiraların GenelToplam toplamı (ciro).</summary>
    public decimal Ciro { get; init; }
    public DateTimeOffset? SonKira { get; init; }

    // ---- FAZ-40: entity'de ZATEN VAR ama projeksiyona girmiyordu (D3 — en ucuz sınıf) ----
    public string? MusteriTemsilcisi { get; init; }
    public DateTimeOffset? DogumTarihi { get; init; }
    public int? VadeGun { get; init; }
    public string? UyariNedeni { get; init; }
    public string? Gsm2 { get; init; }
    public string? Adres { get; init; }
    public string? Ilce { get; init; }
    /// <summary>FAZ-40 yeni alanları — listede arama/ayırt etme için.</summary>
    public string? EntegrasyonKodu { get; init; }
    public string? OzelKod { get; init; }
    public string? Ulke { get; init; }
    public string? Sinif { get; init; }
    /// <summary>Operasyonel uyarı: bu cariye araç verilmez.</summary>
    public bool AracVerilmez { get; init; }

    /// <summary>F7.1 — KVKK anonimleştirme bayrakları (yeni yüzey görünen alanları bunlarla maskeler).</summary>
    public bool AnonimAd { get; init; }
    public bool AnonimTelefon { get; init; }
    public bool AnonimMail { get; init; }
    public bool AnonimAdres { get; init; }
}
