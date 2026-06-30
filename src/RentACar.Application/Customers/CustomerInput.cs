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

    // CRM parite zenginleştirme (additive, opsiyonel)
    public string? Sinif { get; set; }
    public bool? MailIzin { get; set; }
    public bool? SmsIzin { get; set; }
    public bool? TelefonIzin { get; set; }
    public DateTimeOffset? DogumTarihi { get; set; }
    public string? BabaAdi { get; set; }
    public string? AnaAdi { get; set; }
    public string? PasaportNo { get; set; }
    public string? FaturaDonemi { get; set; }
    public decimal? TevkifatOrani { get; set; }
    public string? Yetkili1Ad { get; set; }
    public string? Yetkili1Tel { get; set; }
    public string? Yetkili1Mail { get; set; }
    public string? Yetkili2Ad { get; set; }
    public string? Yetkili2Tel { get; set; }
    public string? Yetkili2Mail { get; set; }
    public string? Yetkili3Ad { get; set; }
    public string? Yetkili3Tel { get; set; }
    public string? Yetkili3Mail { get; set; }

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

    // KVKK + ek adres/banka/fatura adresi (roadmap K4)
    public bool? KvkkOnay { get; set; }
    public DateTimeOffset? KvkkOnayTarih { get; set; }
    public string? EkAdres { get; set; }
    public string? BankaIban { get; set; }
    public string? BankaAdi { get; set; }
    public string? FaturaAdresi { get; set; }
    public string? FaturaUnvan { get; set; }
}
