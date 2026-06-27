using RentACar.Domain.Enums;

namespace RentACar.Application.Expenses;

/// <summary>Gider girişi (net + KDV oranı; tedarikçi faturası modeli).</summary>
public sealed class ExpenseInput
{
    public ExpenseType Tip { get; set; } = ExpenseType.Genel;
    public DateTimeOffset? Tarih { get; set; }
    public Guid? VehicleId { get; set; }
    public Guid? CariId { get; set; }
    public string? Sube { get; set; }
    public string? EvrakNo { get; set; }
    public decimal NetTutar { get; set; }
    public decimal KdvOrani { get; set; } = 0.20m;
    public string Doviz { get; set; } = "TRY";
    public decimal Kur { get; set; } = 1m;
    public OdemeYontemi OdemeYontemi { get; set; } = OdemeYontemi.Nakit;
    public LedgerAccountType KasaBankaHesap { get; set; } = LedgerAccountType.Kasa;
    public string? Aciklama { get; set; }
}
