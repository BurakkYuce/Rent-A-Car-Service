namespace RentACar.Application.Pricing;

/// <summary>Tarife (rate card) oluştur/güncelle giriş modeli.</summary>
public sealed class RateCardInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string Grup { get; set; } = string.Empty;
    public int MinGun { get; set; } = 1;
    public int MaxGun { get; set; } = 9999;
    public decimal GunlukUcret { get; set; }
    public string Doviz { get; set; } = "TRY";
    public DateTimeOffset? GecerliBas { get; set; }
    public DateTimeOffset? GecerliBit { get; set; }
    public bool Aktif { get; set; } = true;

    // ---- FAZ-72 teminat/görünürlük + tarife grubu ----
    public bool ScdwDahil { get; set; }
    public bool MiniHasarDahil { get; set; }
    public bool HirsizlikDahil { get; set; }
    public bool ScdwZorunlu { get; set; }
    /// <summary>true → satır gizlenir (varsayılan false = görünür).</summary>
    public bool Gosterme { get; set; }
    public Guid? TarifeGrubuId { get; set; }
}
