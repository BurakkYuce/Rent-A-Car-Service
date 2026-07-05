namespace RentACar.Application.Kur;

/// <summary>Kur sabitleme giriş modeli.</summary>
public sealed class SabitKurInput
{
    public string Kod { get; set; } = string.Empty;
    public decimal Kur { get; set; }
    public DateTimeOffset? BasTar { get; set; }
    public DateTimeOffset? BitTar { get; set; }
    public bool Aktif { get; set; } = true;
}
