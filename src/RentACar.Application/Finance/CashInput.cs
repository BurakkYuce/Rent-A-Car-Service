namespace RentACar.Application.Finance;

/// <summary>Nakit tahsilat giriş modeli (çok-dövizli).</summary>
public sealed class CashInput
{
    public Guid CariId { get; set; }
    public Guid? RentalId { get; set; }
    public decimal Tutar { get; set; }
    public string Doviz { get; set; } = "TRY";
    public decimal Kur { get; set; } = 1m;
    public DateTimeOffset? Tarih { get; set; }
    public string? Aciklama { get; set; }
}
