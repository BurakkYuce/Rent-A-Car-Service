using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Araç — PR #1 dikey diliminin çekirdek varlığı. Orijinaldeki 80+ alanın yalnız
/// çekirdeği modellenir (plaka, marka, grup, şube, durum, km, yakıt). Tenant-owned
/// + auditable: EF global query filter + Postgres RLS ile izole, değişiklikleri
/// AuditLog'a yazılır.
/// </summary>
public class Vehicle : ITenantOwned, IAuditable, IBranchScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>Plaka — zorunlu; tenant içinde benzersiz (doğal iş anahtarı).</summary>
    public string Plaka { get; set; } = string.Empty;

    public string? Marka { get; set; }

    /// <summary>Model/tip (ör. Egea, Clio).</summary>
    public string? Tip { get; set; }

    /// <summary>Araç grubu / fiyat sınıfı (rate-class).</summary>
    public string? Grup { get; set; }

    /// <summary>Segment (ör. Ekonomik, Orta, SUV).</summary>
    public string? Segment { get; set; }

    /// <summary>SIPP/ACRISS kodu (4 harf, ör. CDMD).</summary>
    public string? Sipp { get; set; }

    public string? Renk { get; set; }

    /// <summary>Model yılı (opsiyonel).</summary>
    public int? ModelYili { get; set; }

    public Vites? Vites { get; set; }

    public string? SasiNo { get; set; }

    public string? MotorNo { get; set; }

    /// <summary>İşlem şubesi (serbest metin — geriye-uyum; görüntü/yedek).</summary>
    public string? Sube { get; set; }
    /// <summary>Şube FK (Branch master, roadmap F1). Tenant içi çözülür; eşleşmeyen eski kayıtta null.</summary>
    public Guid? SubeId { get; set; }

    // Şube-FK marker (roadmap F1 tamamlama): Sube metnini SubeId'ye çözer (BranchFkInterceptor).
    string? IBranchScoped.SubeAdi => Sube;
    Guid? IBranchScoped.SubeFk { get => SubeId; set => SubeId = value; }

    /// <summary>Operasyonel durum (Boş/Kirada/Serviste…).</summary>
    public VehicleStatus Durum { get; set; } = VehicleStatus.Musait;

    /// <summary>Filo yaşam döngüsü statüsü (stok/havuz/tahsis…); operasyonel <see cref="Durum"/>'dan ayrı.</summary>
    public FiloStatus? FiloDurum { get; set; }

    public int Km { get; set; }

    /// <summary>
    /// Yakıt türü. <b>NULLABLE (PR-21)</b> — eskiden zorunluydu ve varsayılanı <c>Benzin</c>'di,
    /// yani hiç girilmemiş her araç sessizce "Benzin" oluyordu ve bu bilgi halka açık siteye,
    /// ilan başlığına ve sözleşmeye basılıyordu. Canlıda 26 aracın 26'sı "Benzin" görünüyordu.
    /// <c>null</c> = "girilmedi"; yanlış bilgi yayınlamaktansa hiç yayınlamamak doğrudur.
    /// </summary>
    public FuelType? Yakit { get; set; }

    // ---- Parite zenginleştirme (docs/parite/01; additive, NULLABLE) ----
    /// <summary>Motor gücü (HP).</summary>
    public int? MotorGucu { get; set; }
    /// <summary>Silindir hacmi (cc).</summary>
    public int? SilindirHacmi { get; set; }
    /// <summary>Ruhsat belge no.</summary>
    public string? RuhsatNo { get; set; }
    /// <summary>Tescil tarihi.</summary>
    public DateTimeOffset? TescilTarihi { get; set; }
    /// <summary>Araç sahibi (VehicleOwner master'ından serbest metin besleme; FK YOK).</summary>
    public string? AracSahibi { get; set; }

    // Alış / maliyet
    public decimal? AlimBedeli { get; set; }
    public DateTimeOffset? AlimTarihi { get; set; }
    /// <summary>Alış fatura vergisiz tutar.</summary>
    public decimal? AlisVergisiz { get; set; }
    /// <summary>Alış ÖTV.</summary>
    public decimal? AlisOtv { get; set; }
    /// <summary>Alış KDV.</summary>
    public decimal? AlisKdv { get; set; }
    /// <summary>Aylık maliyet.</summary>
    public decimal? AylikMaliyet { get; set; }
    /// <summary>Filo yönetim maliyeti.</summary>
    public decimal? FiloYonetimMaliyeti { get; set; }
    /// <summary>2.el / güncel değer.</summary>
    public decimal? IkinciElDeger { get; set; }

    // Filo yaşam döngüsü tarihleri
    public DateTimeOffset? FiloGirisTarih { get; set; }
    public DateTimeOffset? FiloCikisTarih { get; set; }

    // Kullanıcı tanımlı özel kodlar (5 adet)
    public string? OzelKod1 { get; set; }
    public string? OzelKod2 { get; set; }
    public string? OzelKod3 { get; set; }
    public string? OzelKod4 { get; set; }
    public string? OzelKod5 { get; set; }

    // roadmap G1: araç kartı ek alanlar (additive — HGS/OGS, kasa/detay tipi, alış fatura/firma, km limiti)
    public string? HgsNo { get; set; }
    public string? OgsNo { get; set; }
    public string? KasaTipi { get; set; }
    public string? DetayTipi { get; set; }
    public string? AlimFaturaNo { get; set; }
    public string? AlimYapilanFirma { get; set; }
    public int? KiraKmLimiti { get; set; }

    /// <summary>
    /// PR-11 — halka açık sitede bu kaydın kaç araç olarak GÖSTERİLECEĞİ (null = 1). Sınır 1..999.
    ///
    /// <para><b>YALNIZ GÖRÜNTÜLEME.</b> Rezervasyon, kira, müsaitlik, km, hasar, sigorta ve defter
    /// yollarının HİÇBİRİ bu alanı okumaz — iç sistem plaka başına tekil araçla çalışır. Sebebi
    /// yapısal: <c>rentals_no_overlap</c> GiST kısıtı (<c>EXCLUDE … "VehicleId" =, "Period" &amp;&amp;</c>)
    /// tek VehicleId üzerinde çakışan iki açık kirayı FİZİKSEL olarak yasaklar; 17 entity VehicleId
    /// FK'lıdır ve <c>(TenantId, Plaka)</c> benzersizdir. "Tek kayıt = 12 araç" iç sistemde
    /// yapılamaz — vitrinde yapılabilir.</para>
    ///
    /// <para><b>SINIR:</b> tek-kayıt-N modunda temsilci gerçek plakalı bir araçsa, ilk kirada
    /// müsaitlik onu düşürür → aramada grup N'den 0'a inmez, TAMAMEN kaybolur (diğer N−1 araç boşta
    /// olsa bile). Bu mod fiilen "o araçların kirasını sistemde tutmayan" tenant içindir; kirayı
    /// sistemde tutan filo için doğru model her araç için ayrı kayıttır (VitrinAdet = null).</para>
    /// </summary>
    public int? VitrinAdet { get; set; }

    /// <summary>PR-13: aracın bağlı olduğu halka açık site İLANI (yoksa araç sitede hiç yayınlanmamış).
    /// Bir araç en fazla BİR ilana aittir. FK composite'tir (<c>TenantId, WebIlanId</c>) — çapraz-tenant
    /// referansı yapısal olarak imkânsız (FAZ 5-C4 şube FK deseni). İlan silinirse SET NULL: araç
    /// yayından düşer ama araç kaydı bozulmaz.</summary>
    public Guid? WebIlanId { get; set; }

    // ---- Operasyon bayrakları (roadmap K2; varsayılan false) ----
    /// <summary>Web kanalına rezervasyona kapalı.</summary>
    public bool WebRezKapat { get; set; }
    /// <summary>Ofis/çağrı rezervasyonuna kapalı.</summary>
    public bool OfisRezKapat { get; set; }
    /// <summary>Z raporu/izin (özel kullanım izni).</summary>
    public bool ZIzni { get; set; }
    /// <summary>UTTS (ulusal taşıt tanıma) takılı.</summary>
    public bool Utts { get; set; }
    /// <summary>Kar/kış lastiği takılı.</summary>
    public bool KarLastigi { get; set; }
    /// <summary>Yedek anahtar mevcut.</summary>
    public bool YedekAnahtar { get; set; }
    /// <summary>Temizlik tamamlandı/temiz.</summary>
    public bool Temizlik { get; set; }
    /// <summary>Araç rehinli/ipotekli.</summary>
    public bool Rehin { get; set; }

    // ---- Bakım/lastik (roadmap K2) ----
    public DateTimeOffset? SonBakimTarih { get; set; }
    public int? SonBakimKm { get; set; }
    /// <summary>Lastik durumu (serbest metin: Yazlık/Kışlık/Yıpranmış…).</summary>
    public string? LastikDurumu { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
