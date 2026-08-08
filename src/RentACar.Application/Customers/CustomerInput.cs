using RentACar.Domain.Enums;

namespace RentACar.Application.Customers;

/// <summary>Kurumsal cari yetkili kişisi girişi (PR-E). AdSoyad zorunlu; boş satır (AdSoyad'sız) atlanır.</summary>
public sealed class CustomerContactInput
{
    public string? AdSoyad { get; set; }
    public string? Telefon { get; set; }
    public string? Mail { get; set; }
    public string? Gorev { get; set; }
}

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

    /// <summary>PR-E: kurumsal yetkili kişiler (DEĞİŞKEN sayı; Yetkili1-3 flat kolonların yerine).</summary>
    public List<CustomerContactInput> Kisiler { get; set; } = [];

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

    // referans sistem parite (additive)
    public string? OzelCariTip { get; set; }
    public string? MusteriTipi { get; set; }
    public string? EhliyetUlke { get; set; }
    public string? Dil { get; set; }
    public string? Doviz { get; set; }
    public string? TevkifatDurum { get; set; }

    // ---- FAZ-40 derinlik ----
    public string? Ulke { get; set; }
    public string? Tel2 { get; set; }
    public string? OzelKod { get; set; }
    public string? EntegrasyonKodu { get; set; }
    public string? Aciklama { get; set; }
    public string? RiskIzin { get; set; }
    public string? DogumYeri { get; set; }
    public string? PasaportYeri { get; set; }
    public string? KurumsalNo { get; set; }
    public string? UyariSerbest { get; set; }
    public string? TevkifatKodu { get; set; }
    public string? FaturaKiralayanIsim { get; set; }
    public string? IsAdresi { get; set; }
    public string? IsTelefonu { get; set; }
    public string? KayitliIl { get; set; }
    public string? KayitliIlce { get; set; }
    public string? MahalleKoy { get; set; }
    public string? SeriNo { get; set; }
    public string? CiltNo { get; set; }
    public string? AileSira { get; set; }
    public string? SiraNo { get; set; }
    public bool TcDogrulama { get; set; }
    public bool FaturaAdresFarkli { get; set; }
    public bool FaturaTekSatir { get; set; }
    public bool DogumGunuTakip { get; set; }
    public bool AnonimAd { get; set; }
    public bool AnonimTc { get; set; }
    public bool AnonimTelefon { get; set; }
    public bool AnonimMail { get; set; }
    public bool AnonimAdres { get; set; }
    public bool AnonimBelge { get; set; }
    public bool BakiyeGor { get; set; }
    public bool AracVerilmez { get; set; }
    public bool YasEhliyetSerbest { get; set; }
    public bool MerkezKurumsal { get; set; }
    public bool Broker { get; set; }
    public bool FindexZorunlu { get; set; }
    public decimal? BayiKomisyon { get; set; }
    public DateTimeOffset? PasaportTarihi { get; set; }
    public decimal? WebIndirim { get; set; }
    public DateTimeOffset? KaraZamani { get; set; }
    public Guid? IslemSubeId { get; set; }
    public Guid? FirmaId { get; set; }

    /// <summary>Portal şifresi DÜZ METİN — servis hash'ler, kolona ASLA düz yazılmaz.
    /// Boş bırakılırsa mevcut özet KORUNUR ("değiştirme").</summary>
    public string? Sifre { get; set; }
}
