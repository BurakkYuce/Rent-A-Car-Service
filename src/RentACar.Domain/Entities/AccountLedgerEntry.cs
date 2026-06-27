using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

public enum LedgerDirection
{
    Debit = 0,   // Borç
    Credit = 1   // Alacak
}

/// <summary>
/// Çift-taraflı muhasebe defteri kaydı. EKLE-SADECE (append-only) ve DB seviyesinde
/// DEĞİŞMEZ (trigger UPDATE/DELETE'i engeller) — mali denetim için. Düzeltme = ters
/// kayıt (yeni satır), asla güncelleme. Her finansal işlem DENGELİ bir küme yazar
/// (Σ Borç(base) = Σ Alacak(base)).
///
/// Cari bakiye (CariId) = Σ (Borç ? +AmountInBase : -AmountInBase). Pozitif = müşteri borçlu.
/// </summary>
public class AccountLedgerEntry : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public DateTimeOffset EntryDateUtc { get; set; } = DateTimeOffset.UtcNow;

    public LedgerAccountType AccountType { get; set; }
    /// <summary>Cari ise CariId; Kasa/Banka ise hesap id; Gelir/Kdv ise null.</summary>
    public Guid? AccountRef { get; set; }

    public LedgerDirection Direction { get; set; }

    /// <summary>Çok-dövizli tutar (tutar + döviz + kur).</summary>
    public Money Amount { get; set; } = Money.Zero("TRY");

    public string? Description { get; set; }

    /// <summary>Kaynak belge türü: "Tahsilat" | "TersKayit" | "Fatura".</summary>
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }

    /// <summary>Bakiye hesabı için işaretli yerel-para tutarı.</summary>
    public decimal SignedBase => Direction == LedgerDirection.Debit ? Amount.AmountInBase : -Amount.AmountInBase;
}
