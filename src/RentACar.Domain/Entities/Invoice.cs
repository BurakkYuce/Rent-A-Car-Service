using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Fatura (dahili belge — PR #7 stub; e-Fatura entegrasyonu Faz 2'de gerçek). Tenant-owned
/// + auditable + DB-seviyesinde DEĞİŞMEZ (kesilmiş fatura UPDATE/DELETE'e kapalı). Düzeltme
/// = ters/iptal kaydı. Kesilince cari'yi BORÇLANDIRIR (Borç Cari / Alacak Gelir+KDV).
/// </summary>
public class Invoice : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz fatura no (FT-000001).</summary>
    public string No { get; set; } = string.Empty;

    public InvoiceStatus Durum { get; set; } = InvoiceStatus.Kesildi;

    public Guid CariId { get; set; }
    public Guid? RentalId { get; set; }

    public DateTimeOffset Tarih { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? VadeTarihi { get; set; } // roadmap G2

    public decimal NetTutar { get; set; }
    public decimal KdvTutar { get; set; }
    public decimal GenelToplam { get; set; }
    public string Currency { get; set; } = "TRY";
    public decimal Kur { get; set; } = 1m;

    // e-Fatura (stub): ETTN ve gönderim durumu.
    public string? EFaturaEttn { get; set; }
    public bool EFaturaGonderildi { get; set; }

    // ---- Vergi/belge alanları (parite #8; additive, BİLGİ AMAÇLI) ----
    // Defter postlamasını DEĞİŞTİRMEZ: kayıt halen Borç Cari (GenelToplam) / Alacak Gelir (NetTutar) /
    // Alacak KDV (KdvTutar) — dengeli. Bu alanlar faturada gösterilir; v1'de ledger'a yansımaz
    // (docs/parite/05; tevkifat/ÖTV/damga ledger entegrasyonu ileride dengeli olarak eklenebilir).
    /// <summary>ÖTV tutarı (bilgi amaçlı).</summary>
    public decimal? Otv { get; set; }
    /// <summary>Tevkifat (stopaj) oranı (%).</summary>
    public decimal? TevkifatOran { get; set; }
    /// <summary>Tevkifat tutarı.</summary>
    public decimal? TevkifatTutar { get; set; }
    /// <summary>Damga vergisi.</summary>
    public decimal? DamgaVergisi { get; set; }
    /// <summary>İade/return faturası mı.</summary>
    public bool IadeMi { get; set; }
    /// <summary>İade faturasıysa kaynak (iptal edilen) faturanın Id'si. Kaynak başına TEK iade
    /// (kısmi-unique index). Normal faturada null.</summary>
    public Guid? KaynakFaturaId { get; set; }

    /// <summary>Fark faturası ise hangi kiranın ek-bedel farkı (dönüş/uzatma sonrası). Fark faturaları RentalId
    /// = null (kira-fatura unique index'ine çarpmasın) → kira bağı bu alandan (PR-F1). Kira-brütü hesabı
    /// (InvoicedGrossForRentalAsync) base fatura (RentalId) + fark faturalarını (KaynakKiraId) toplar.</summary>
    public Guid? KaynakKiraId { get; set; }

    /// <summary>Fark faturasının kira içindeki SIRA no'su (1-based = o ana dek kesilmiş fark sayısı + 1).
    /// İdempotency doğal anahtarı: (TenantId, KaynakKiraId, KaynakKiraFarkSira) kısmi-unique. Eşzamanlı/çift istek
    /// aynı sırayı hesaplar → çarpışır → tek fark (adversarial Kritik-1). Yeni ek bedel VEYA fark-iadesi sonrası
    /// yeniden kesim yeni sıra alır (iade'li fark sayaçta kalır) → mutlak-hedef kilidi kalkar (adversarial V6).</summary>
    public int? KaynakKiraFarkSira { get; set; }
    /// <summary>Kiradan bağımsız manuel fatura mı.</summary>
    public bool ManuelMi { get; set; }

    // ---- Bilgi/belge alanları (FAZ-51; additive, BİLGİ AMAÇLI — postlamaya/deftere YANSIMAZ) ----
    /// <summary>Faturanın kesildiği işlem şubesi (serbest metin — Şube master'a FK değil, canlı paritesi
    /// serbest metin alanıydı).</summary>
    public string? IslemSube { get; set; }
    /// <summary>Evrak/belge no (serbest metin, fatura No'sundan ayrı — harici evrak çapraz referansı).</summary>
    public string? EvrakNo { get; set; }
    /// <summary>Fatura özel kodu (serbest metin — canlıda kod-listesi, burada serbest).</summary>
    public string? FaturaOzelKod { get; set; }
    /// <summary>Ödeme türü (ör. Kart/Havale/Nakit — seç-veya-yaz).</summary>
    public string? OdemeTuru { get; set; }
    /// <summary>Gönderim şekli (ör. Mail/Kargo/Posta — seç-veya-yaz).</summary>
    public string? GonderimSekli { get; set; }
    /// <summary>KDV sıfır/istisna sebebi (canlıda 4 sabit seçenek).</summary>
    public string? KdvSifirSebep { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<InvoiceLine> Lines { get; set; } = [];
}

/// <summary>Fatura satırı. Tenant-owned (RLS) + faturayla birlikte değişmez.</summary>
public class InvoiceLine : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid InvoiceId { get; set; }

    public string Aciklama { get; set; } = string.Empty;
    public decimal Miktar { get; set; } = 1m;
    public decimal BirimNetFiyat { get; set; }
    public decimal KdvOrani { get; set; }   // ör. 0.20
    public decimal SatirNet { get; set; }
    public decimal SatirKdv { get; set; }
    public decimal SatirToplam { get; set; }
}
