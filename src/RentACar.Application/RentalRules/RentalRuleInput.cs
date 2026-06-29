namespace RentACar.Application.RentalRules;

/// <summary>Kiralama kuralı oluştur/güncelle giriş modeli. Kapsam/indirim alanları opsiyonel.</summary>
public sealed class RentalRuleInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }

    public string? Kanal { get; set; }
    public string? Sube { get; set; }
    public string? AracGrupKod { get; set; }

    public int? MinGun { get; set; }
    public int? MaxGun { get; set; }

    public decimal? Iskonto { get; set; }
    public decimal? HaftaSonuFarkOran { get; set; } // roadmap G3
    public decimal? SonraOdeOran { get; set; }
    public int? HediyeGun { get; set; }
    public bool KampanyaMi { get; set; }
    public string? KampanyaKodu { get; set; }

    public DateTimeOffset? GecerlilikBas { get; set; }
    public DateTimeOffset? GecerlilikBit { get; set; }

    public string? SartMetni { get; set; }

    public bool Aktif { get; set; } = true;
}
