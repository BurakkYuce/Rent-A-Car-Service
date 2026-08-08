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
    /// <summary>Boş → otomatik çözüm (TRY=1; döviz KurService). Açık değer aynen kullanılır (1.1b).</summary>
    public decimal? Kur { get; set; }
    public OdemeYontemi OdemeYontemi { get; set; } = OdemeYontemi.Nakit;
    public LedgerAccountType KasaBankaHesap { get; set; } = LedgerAccountType.Kasa;
    public string? Aciklama { get; set; }

    // ---- FAZ-29 toplu gider derinliği ----
    /// <summary>Ödeme vadesi (bilgi) — deftere GİRMEZ, belge notudur.</summary>
    public DateTimeOffset? Vade { get; set; }
    /// <summary>Hangi kasa/banka hesabından ödendiği (FinancialAccount). BELGE BİLGİSİ —
    /// defter karşı hesabı hâlâ KasaBankaHesap (Kasa/Banka) üzerinden yazılır.</summary>
    public Guid? FinansalHesapId { get; set; }
}
