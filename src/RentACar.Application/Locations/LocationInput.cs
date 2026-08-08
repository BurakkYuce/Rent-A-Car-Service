namespace RentACar.Application.Locations;

/// <summary>Ofis/Lokasyon oluştur/güncelle giriş modeli.</summary>
public sealed class LocationInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Adres { get; set; }
    public string? Telefon { get; set; }
    public string? Eposta { get; set; }
    public string? CalismaSaatleri { get; set; }
    public decimal? TeslimUcreti { get; set; }
    public string? Sube { get; set; }

    // ---- FAZ-22 derinlik ----
    public string? IngilizceAd { get; set; }
    public string? BulusmaNoktasi { get; set; }
    public string? Iata { get; set; }
    public bool WebdeGizle { get; set; }
    public string? LokasyonTuru { get; set; }
    public string? BinaNo { get; set; }
    public string? Tarif { get; set; }
    public string? Ulke { get; set; }
    public string? PostaKodu { get; set; }
    public string? MapsKonumu { get; set; }
    public string? EkAciklama { get; set; }
    public int? WebSira { get; set; }
    public string? DropKarsilamaTuru { get; set; }
    public string? DropCalismaSekli { get; set; }
    public string? OzelMail { get; set; }
    public string? OzelTelefon { get; set; }

    /// <summary>Haftalık saatler; boş/eksik gün gönderilirse servis 7 güne TAMAMLAR.</summary>
    public List<RentACar.Domain.Entities.GunSaat> HaftalikCalismaSaatleri { get; set; } = [];

    public bool Aktif { get; set; } = true;
}
