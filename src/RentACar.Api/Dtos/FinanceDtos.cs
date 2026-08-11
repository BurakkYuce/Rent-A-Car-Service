using RentACar.Application.Finance;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Api.Dtos;

/// <summary>Para zarfı (çok-dövizli): tutar + döviz + kur + yerel karşılık (base).</summary>
public sealed record MoneyDto(decimal Amount, string Currency, decimal Rate, decimal Base)
{
    public static MoneyDto From(Money m) => new(m.Amount, m.Currency, m.Rate, m.AmountInBase);
}

/// <summary>Tahsilat/Ödeme isteği. Hesap: Kasa veya Banka.</summary>
public sealed class CashRequest
{
    public Guid CariId { get; set; }
    public Guid? RentalId { get; set; }
    public decimal Tutar { get; set; }
    public string Doviz { get; set; } = "TRY";
    /// <summary>Boş/gönderilmez → otomatik çözüm (TRY=1; döviz sabit-kur/TCMB, yoksa red — 1.1b).</summary>
    public decimal? Kur { get; set; }
    public DateTimeOffset? Tarih { get; set; }
    public string? Aciklama { get; set; }
    public LedgerAccountType Hesap { get; set; } = LedgerAccountType.Kasa;
    /// <summary>FAZ-50 — hangi SPESİFİK kasa/banka hesabı (FinancialAccount.Id). Boş → hesap
    /// belirtilmemiş (defterde AccountRef null).</summary>
    public Guid? HesapId { get; set; }

    public CashInput ToInput() => new()
    {
        CariId = CariId, RentalId = RentalId, Tutar = Tutar, Doviz = Doviz, Kur = Kur,
        Tarih = Tarih, Aciklama = Aciklama, Hesap = Hesap, HesapId = HesapId
    };
}

/// <summary>Kasa↔Banka virman isteği.</summary>
public sealed class TransferRequest
{
    public LedgerAccountType Kaynak { get; set; }
    public LedgerAccountType Hedef { get; set; }
    public decimal Tutar { get; set; }
    public string? Doviz { get; set; } = "TRY";
    /// <summary>Boş/gönderilmez → otomatik çözüm (TRY=1; döviz sabit-kur/TCMB, yoksa red — 1.1b).</summary>
    public decimal? Kur { get; set; }
    public string? Aciklama { get; set; }
    // FAZ-50 — spesifik hesaplar. İkisi de verilirse aynı türde (Banka→Banka) virman da mümkün.
    public Guid? KaynakHesapId { get; set; }
    public Guid? HedefHesapId { get; set; }
    public string? MakbuzNo { get; set; }
    public string? Sube { get; set; }
}

public sealed record CashTransactionResponse(
    Guid Id, string No, CashTransactionType Tip, Guid CariId, Guid? RentalId, DateTimeOffset Tarih,
    MoneyDto Tutar, LedgerAccountType KarsiHesap, Guid? HesapId, string? Aciklama, bool TersKayitMi, Guid? TersAlinanId)
{
    public static CashTransactionResponse From(CashTransaction t) => new(
        t.Id, t.No, t.Tip, t.CariId, t.RentalId, t.Tarih, MoneyDto.From(t.Amount),
        t.KarsiHesap, t.HesapId, t.Aciklama, t.TersKayitMi, t.TersAlinanId);
}

public sealed record LedgerEntryResponse(
    DateTimeOffset EntryDateUtc, LedgerAccountType AccountType, Guid? AccountRef, LedgerDirection Direction,
    MoneyDto Tutar, decimal SignedBase, string SourceType, Guid? SourceId, string? Description)
{
    public static LedgerEntryResponse From(AccountLedgerEntry e) => new(
        e.EntryDateUtc, e.AccountType, e.AccountRef, e.Direction, MoneyDto.From(e.Amount), e.SignedBase,
        e.SourceType, e.SourceId, e.Description);
}

public sealed record InvoiceResponse(
    Guid Id, string No, InvoiceStatus Durum, Guid CariId, Guid? RentalId, DateTimeOffset Tarih,
    decimal NetTutar, decimal KdvTutar, decimal GenelToplam, string Currency, decimal Kur,
    string? EFaturaEttn, DateTimeOffset CreatedAtUtc)
{
    public static InvoiceResponse From(Invoice i) => new(
        i.Id, i.No, i.Durum, i.CariId, i.RentalId, i.Tarih, i.NetTutar, i.KdvTutar, i.GenelToplam,
        i.Currency, i.Kur, i.EFaturaEttn, i.CreatedAtUtc);
}
