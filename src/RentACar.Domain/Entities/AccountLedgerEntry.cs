using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

public enum LedgerDirection
{
    Debit = 0,   // Borç
    Credit = 1   // Alacak
}

/// <summary>
/// Cari hesap hareketi (çift-taraflı muhasebe defteri kaydı). PR #1'de yalnız ŞEMA
/// İSKELETİ — para mantığı (tahsilat/fatura/bakiye) henüz YOK. Değişmezlik (kesilmiş
/// kayıt UPDATE/DELETE'e kapalı) ve bakiye güncelleme Faz 2'de eklenecek.
/// </summary>
public class AccountLedgerEntry : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>Hareketin bağlı olduğu cari (müşteri/tedarikçi). İskelet: yalnız referans.</summary>
    public Guid CariId { get; set; }

    public DateTimeOffset EntryDateUtc { get; set; } = DateTimeOffset.UtcNow;

    public LedgerDirection Direction { get; set; }

    /// <summary>Çok-dövizli tutar (tutar + döviz + kur).</summary>
    public Money Amount { get; set; } = Money.Zero("TRY");

    public string? Description { get; set; }
}
