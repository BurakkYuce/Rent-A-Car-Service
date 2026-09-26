using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Servis/bakım kaydı. Tenant-owned + auditable. Operasyonel kayıt (mali belge DEĞİL —
/// maliyet bilgilendirme; gerçek gider Gider dilimine bağlanır, follow-up). Servis süresince
/// araç Serviste durumuna geçer, tamamlanınca Musait'e döner. İşçilik kalemleri alt-tabloda.
/// </summary>
public class ServiceRecord : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz no (SRV-000001).</summary>
    public string No { get; set; } = string.Empty;

    public Guid VehicleId { get; set; }
    public ServiceType Tip { get; set; } = ServiceType.Periyodik;
    public ServiceStatus Durum { get; set; } = ServiceStatus.Acik;

    public string? AtolyeAdi { get; set; }
    public DateTimeOffset GirisTarihi { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CikisTarihi { get; set; }
    public int GirisKm { get; set; }
    public int? CikisKm { get; set; }

    /// <summary>Hasarlı serviste sorumluluk; KusurOrani 0..1 (kusur yüzdesi).</summary>
    public DamageResponsible HasarSorumlu { get; set; } = DamageResponsible.Yok;
    public decimal? KusurOrani { get; set; }

    /// <summary>Periyodik bakım için sonraki bakım KM hedefi.</summary>
    public int? SonrakiBakimKm { get; set; }

    public string? Aciklama { get; set; }

    /// <summary>İşçilik kalemleri toplamı (Lines'tan türetilir, kalıcılaştırılır). KDV HARİÇ net.</summary>
    public decimal ToplamIscilik { get; set; }

    /// <summary>Rücu/yansıtma yapıldı mı (roadmap J4): true → defter kaydı yazılmış, tekrar yansıtılamaz.</summary>
    public bool Yansitildi { get; set; }
    /// <summary>Yansıtılan tutar (roadmap J4): ToplamIscilik × KusurOrani.</summary>
    public decimal YansitilanTutar { get; set; }
    /// <summary>Yansıtmanın borçlandırıldığı cari (roadmap J4).</summary>
    public Guid? YansitilanCariId { get; set; }

    // ==================== FAZ-16 — KAZA / HASAR BLOĞU (BİLGİ) ====================
    // Canlı arac_servis_islemleri.aspx kaza bölümünün alanları. Rücu/yansıtma akışını (yukarıdaki
    // Yansitildi/YansitilanTutar/YansitilanCariId) BESLER ama KENDİSİ hiçbir hesaba girmez —
    // burada girilen hiçbir değer defter/rapor toplamı üretmez.

    /// <summary>Beyan türü (kaza tespit tutanağı / anlaşmalı beyan / tek taraflı …). Serbest metin —
    /// canlıda da sabit listesi yok, tenant'a göre değişiyor.</summary>
    public string? BeyanTuru { get; set; }
    /// <summary>Karşı aracın plakası (çift taraflı kazada).</summary>
    public string? KarsiPlaka { get; set; }
    /// <summary>Karşı tarafın trafik sigortası (şirket/poliçe bilgisi, serbest metin).</summary>
    public string? KarsiTrafikSigortasi { get; set; }
    /// <summary>Kazanın olduğu tarih (servise giriş tarihinden AYRI — kaza önce olur, araç sonra girer).</summary>
    public DateTimeOffset? KazaTarihi { get; set; }
    /// <summary>Kaza sorumlusu (sürücü/karşı taraf adı vb.). <see cref="HasarSorumlu"/> enum'undan AYRI:
    /// o rücu kuralını sürer, bu serbest bir isim/nottur.</summary>
    public string? KazaSorumlusu { get; set; }
    /// <summary>Sigorta/eksper hasar dosya numarası (dış referans).</summary>
    public string? HasarDosyaNo { get; set; }
    /// <summary>Araçta oluşan değer kaybı — BİLGİ. Deftere yazılmaz, amortismana/karneye girmez.</summary>
    public decimal? DegerKaybi { get; set; }

    // ==================== FAZ-16 — FATURA BLOĞU (BİLGİ, DEFTERE YAZMAZ) ====================
    // KİLİTLİ KARAR (docs/KARARLAR.md "FAZ-16 — Servis faturası/ödemesi defter bağı"):
    // BAĞLANMAZ. Servis kaydı bilgi olarak zenginleşir; GERÇEK maliyet Giderler ekranından girilir.
    // Gerekçe: aynı masrafı hem servisten hem giderden yazmak ÇİFT-SAYIM olurdu (Araç Karnesi /
    // Karlılık / Filo Analiz P&L'i YALNIZ AccountLedgerEntry'den okur). Bu alanlar bir
    // AccountLedgerEntry ÜRETMEZ; dönem kilidine ve KurCozucu'ya da uğramaz (mali işlem değil).

    public DateTimeOffset? FaturaTarihi { get; set; }
    public string? FaturaNo { get; set; }
    /// <summary>Servis faturasının KDV HARİÇ tutarı (matrah) — BİLGİ.</summary>
    public decimal? FaturaTutar { get; set; }
    /// <summary>Servis faturasının KDV TUTARI (oran değil) — BİLGİ.</summary>
    public decimal? FaturaKdv { get; set; }
    /// <summary>Fatura genel toplamı = FaturaTutar + FaturaKdv. TÜRETİLİR (servis yazar), elle
    /// girilmez: üçüncü bir serbest sayı, matrah+KDV ile çelişebilir bir "üçüncü gerçek" olurdu.</summary>
    public decimal? FaturaGenelToplam { get; set; }

    // ==================== FAZ-16 — ÖDEME BLOĞU (BİLGİ, DEFTERE YAZMAZ) ====================
    // Yukarıdaki kararla aynı: ödeme burada YALNIZCA kayıt altına alınır. Kasadan/bankadan gerçek
    // çıkış Kasa/Banka ekranından yapılır ve defteri O yazar.

    public DateTimeOffset? OdemeTarihi { get; set; }
    /// <summary>Ödenen tutar — BİLGİ (kasa/banka bakiyesini DEĞİŞTİRMEZ).</summary>
    public decimal? Odeme { get; set; }
    /// <summary>Ödemenin dövizi (ISO-3, ör. TRY/EUR) — BİLGİ.</summary>
    public string? OdemeDoviz { get; set; }
    /// <summary>Ödeme kuru — BİLGİ. KurCozucu'ya SORULMAZ: mali işlem değil, kullanıcının yazdığı
    /// belge bilgisidir.</summary>
    public decimal? OdemeKur { get; set; }
    /// <summary>Ödeme türü (Nakit/Banka/Açık hesap) — BİLGİ; karşı hesabı BELİRLEMEZ.</summary>
    public PaymentMethod? OdemeTuru { get; set; }
    /// <summary>Kasa kodu (canlı alan adı) — serbest metin; FinancialAccount FK DEĞİL, çünkü bu
    /// ödeme defterde bir kasa hareketi yaratmıyor.</summary>
    public string? KasaKodu { get; set; }
    /// <summary>Banka hesap no (canlı alan adı) — serbest metin, aynı gerekçe.</summary>
    public string? HesapNo { get; set; }

    // ==================== FAZ-16 — YAKIT + PLAN ====================

    /// <summary>Araç servise girerken ("çıkış" = filodan çıkış) yakıt seviyesi, 0-12 (kira sözleşmesiyle
    /// AYNI ölçek).</summary>
    public int? CikisYakit { get; set; }
    /// <summary>Araç servisten dönerken yakıt seviyesi, 0-12.</summary>
    public int? DonusYakit { get; set; }

    /// <summary>Planlanan randevu penceresi başlangıcı (<see cref="ServiceStatus.Rezerve"/>).
    /// GERÇEK <see cref="GirisTarihi"/>'nden AYRI: plan tutmayabilir, ikisinin farkı ölçülebilsin.</summary>
    public DateTimeOffset? PlanBasTarihi { get; set; }
    /// <summary>Planlanan randevu penceresi bitişi.</summary>
    public DateTimeOffset? PlanBitTarihi { get; set; }

    public List<ServiceLine> Lines { get; set; } = [];

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}

/// <summary>
/// Servis işçilik/parça kalemi. Tenant-owned (RLS); servis kaydına bağlı.
/// <para>FAZ-16: canlı fiyatlandırma ızgarasının kolonları eklendi (Birim Fiyat / Miktar /
/// İndirim / KDV). <see cref="Tutar"/> ANLAMI DEĞİŞMEDİ — hâlâ KDV HARİÇ net satır tutarıdır ve
/// <c>ServiceRecord.ToplamIscilik</c>'i (dolayısıyla rücu/yansıtmayı, yani DEFTERE giden tek
/// sayıyı) besler. KDV bu yüzden Tutar'a KARIŞMAZ; genel toplam yalnız gösterimde türetilir.</para>
/// </summary>
public class ServiceLine : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ServiceRecordId { get; set; }

    public string Aciklama { get; set; } = string.Empty;

    /// <summary>KDV HARİÇ, indirim SONRASI net satır tutarı. Kaynak: kullanıcı ya da
    /// <c>ServisKalemHesap.Net(BirimFiyat, Miktar, Indirim)</c>.</summary>
    public decimal Tutar { get; set; }

    /// <summary>Birim fiyat (KDV hariç) — opsiyonel; serbest tutar girişi KORUNUR.</summary>
    public decimal? BirimFiyat { get; set; }
    /// <summary>Miktar (adet/saat — kesirli olabilir: 1,5 saat işçilik).</summary>
    public decimal? Miktar { get; set; }
    /// <summary>İndirim TUTARI (oran değil) — BirimFiyat × Miktar brütünden düşülür.</summary>
    public decimal? Indirim { get; set; }
    /// <summary>KDV ORANI 0..1 (0,20 = %20). İsim bilerek <c>Kdv</c> değil <c>KdvOran</c>: "0,20"
    /// değerinin ileride 0,20 TL sanılması klasik bir para hatası kaynağıdır.</summary>
    public decimal? KdvOran { get; set; }
}
