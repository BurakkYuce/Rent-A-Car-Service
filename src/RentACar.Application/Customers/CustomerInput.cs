using RentACar.Domain.Enums;

namespace RentACar.Application.Customers;

/// <summary>Cari oluştur/düzenle giriş modeli (Blazor formu da buna bağlanır).</summary>
public sealed class CustomerInput
{
    public CariType Tip { get; set; } = CariType.Bireysel;

    public string? Ad { get; set; }
    public string? Soyad { get; set; }
    public string? TcKimlik { get; set; }

    public string? Unvan { get; set; }
    public string? VergiDairesi { get; set; }
    public string? VergiNo { get; set; }

    public string? CepTel { get; set; }
    public string? Gsm2 { get; set; }
    public string? Email { get; set; }
    public string? Il { get; set; }
    public string? Ilce { get; set; }
    public string? Adres { get; set; }

    public string? Kaynak { get; set; }
    public string? MusteriTemsilcisi { get; set; }
    public bool IysIzinli { get; set; }
    public bool Uyari { get; set; }
    public string? UyariNedeni { get; set; }

    public string? EhliyetNo { get; set; }
    public string? EhliyetSinifi { get; set; }
    public DateTimeOffset? EhliyetTarihi { get; set; }
    public string? EhliyetYeri { get; set; }

    public string? Tarife { get; set; }
    public int VadeGun { get; set; }
    public decimal RiskLimiti { get; set; }
    public string? RiskMesaji { get; set; }
    public DateTimeOffset? RiskTarihi { get; set; }
    public string? HgsYansitmaTuru { get; set; }
    public bool KaraListe { get; set; }
    public bool Pasif { get; set; }
}
