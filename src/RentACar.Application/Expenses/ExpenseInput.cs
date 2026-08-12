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

    // ---- FAZ-64 bilgi alanları (kayıt anında yazılır; belge sonradan DEĞİŞTİRİLEMEZ) ----
    /// <summary>Belge tarihinden AYRI ödeme tarihi. BİLGİ — deftere girmez.</summary>
    public DateTimeOffset? OdemeTarihi { get; set; }
    /// <summary>Şablon/hazır açıklama. BİLGİ.</summary>
    public string? HazirAciklama { get; set; }
    /// <summary>Gideri bir kira sözleşmesine bağlar. BİLGİ — kira bakiyesine girmez.</summary>
    public Guid? RentalId { get; set; }
}

/// <summary>FAZ-64 — gider kısmi ödeme girişi.</summary>
public sealed class GiderOdemeInput
{
    public Guid ExpenseId { get; set; }
    /// <summary>Ödenen tutar. null → KALANIN TAMAMI (tek tıkla kapatma).</summary>
    public decimal? Tutar { get; set; }
    public DateTimeOffset? Tarih { get; set; }
    public string? MakbuzNo { get; set; }
    public string? Aciklama { get; set; }
    /// <summary>Form çift-submit koruması.</summary>
    public Guid? IslemAnahtari { get; set; }
}

/// <summary>
/// FAZ-64 — bir giderin ödeme durumu. <b>Nakit/Banka giderinde borç YOKTUR</b>: para zaten kayıt
/// anında kasadan/bankadan çıktı (defter öyle yazıldı), bu yüzden <c>Odenen = GenelToplam</c> ve
/// <c>Kalan = 0</c> kabul edilir; takip yalnız AÇIK HESAP giderinde anlamlıdır.
/// </summary>
public sealed record GiderOdemeDurumu(
    Guid ExpenseId, decimal GenelToplam, decimal Odenen, decimal Kalan, bool TakipEdilir)
{
    public bool TamamenOdendi => Kalan <= 0m;
}
