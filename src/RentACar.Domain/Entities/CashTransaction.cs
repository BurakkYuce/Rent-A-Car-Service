using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Nakit işlem (Tahsilat). Tenant-owned + auditable (kim/ne zaman). Defter kayıtlarını
/// (dengeli) ve —kira bağlıysa— sözleşme Tahsilat/Bakiye'sini üreten "belge". Düzeltme
/// ters kayıtla yapılır (yeni CashTransaction + ters defter kümesi).
/// </summary>
public class CashTransaction : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz no (TH-000001).</summary>
    public string No { get; set; } = string.Empty;

    public CashTransactionType Tip { get; set; } = CashTransactionType.Tahsilat;

    public Guid CariId { get; set; }
    public Guid? RentalId { get; set; }

    public DateTimeOffset Tarih { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Çok-dövizli tutar (tutar + döviz + kur). Bakiye base'e çevrilerek işlenir.</summary>
    public Money Amount { get; set; } = Money.Zero("TRY");

    /// <summary>Karşı hesap (PR #5: Kasa).</summary>
    public LedgerAccountType KarsiHesap { get; set; } = LedgerAccountType.Kasa;

    public string? Aciklama { get; set; }

    public bool TersKayitMi { get; set; }
    public Guid? TersAlinanId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
