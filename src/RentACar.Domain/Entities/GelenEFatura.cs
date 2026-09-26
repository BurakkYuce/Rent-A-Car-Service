using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Gelen (satın-alma) e-Fatura kaydı — GİB'den çekilen veya elle girilen gelen faturaların triage
/// kutusu (canlı referans sistem gelen_e_fatura_listesi karşılığı). ETTN tenant içinde benzersiz.
/// Triage iş akışı: Beklemede → Onaylandı/Reddedildi; Onaylandı → İşlendi. Tenant-owned + auditable.
///
/// <para><b>DEFTER (FAZ-55):</b> Bu kayıt KENDİ BAŞINA deftere POSTLAMAZ — bütün alanları (toplamlar,
/// KDV oran kırılımı, araç/kategori bağı) BİLGİDİR. Para hareketi YALNIZCA "Giderleştir" aksiyonuyla
/// ve YALNIZCA mevcut <c>Expense</c> yolundan doğar (Borç Gider(net) + Borç KDV(indirilecek) /
/// Alacak Kasa·Banka·Cari(gross)) — yani defter şeması bu fazda DEĞİŞMEDİ, yeni bir defter şekli
/// icat edilmedi. Raporlar (Gelir-Gider, Karlılık, Karne) yalnız <c>AccountLedgerEntry</c> okur;
/// bu tablonun tutarları hiçbir rapor toplamına GİRMEZ → çift-sayım yapısal olarak imkânsız
/// (kırılgan regresyon testi: <c>GelenEFaturaKdvKirilimTests.Giderlestirilmemis_fatura_raporlara_SIZMAZ</c>).</para>
///
/// <para>GİB'den gerçek çekiş kimlik-bağımlı (IEInvoiceService stub) — entegratör kısmı BLOKE.</para>
/// </summary>
public class GelenEFatura : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>ETTN (e-fatura tekil no) — tenant içinde benzersiz.</summary>
    public string Ettn { get; set; } = string.Empty;
    public string GonderenVkn { get; set; } = string.Empty;
    public string GonderenUnvan { get; set; } = string.Empty;
    public DateTimeOffset Tarih { get; set; }

    public decimal NetTutar { get; set; }
    public decimal KdvTutar { get; set; }
    public decimal GenelToplam { get; set; }
    public string Currency { get; set; } = "TRY";

    public IncomingEInvoiceStatus Durum { get; set; } = IncomingEInvoiceStatus.Beklemede;
    public string? RedNedeni { get; set; }
    public string? Aciklama { get; set; }

    // ---- FAZ-55 (a) KDV ORAN KIRILIMI (canlı gelen_e_fatura_listesi kolonları) ----
    // Hepsi decimal? — GEÇMİŞ satırlarda kırılım BİLİNMİYOR ve "0" yazmak "matrahı 0" anlamı
    // yükler (migration defaultValue tuzağı). null = "girilmemiş", 0 = "gerçekten sıfır".
    // Doğrulama + giderleştirme kuralları: GelenEFaturaKdvKirilim.

    /// <summary>%20 kademesinin matrahı (KDV hariç tutar).</summary>
    public decimal? Kdv20Matrah { get; set; }
    /// <summary>%20 kademesinin KDV tutarı.</summary>
    public decimal? Kdv20 { get; set; }
    /// <summary>%10 kademesinin matrahı.</summary>
    public decimal? Kdv10Matrah { get; set; }
    /// <summary>%10 kademesinin KDV tutarı.</summary>
    public decimal? Kdv10 { get; set; }
    /// <summary>%1 kademesinin matrahı.</summary>
    public decimal? Kdv1Matrah { get; set; }
    /// <summary>%1 kademesinin KDV tutarı.</summary>
    public decimal? Kdv1 { get; set; }
    /// <summary>%0 (istisna/muaf) kademesinin matrahı. Bu kademenin KDV'si TANIMI GEREĞİ 0'dır —
    /// ayrı bir "Kdv0" kolonu AÇILMADI (daima 0 tutan kolon, ileride yanlış doldurulmaya açık tuzaktır).</summary>
    public decimal? Kdv0Matrah { get; set; }

    // ---- FAZ-55 (a) BAĞLAMA (araç / gider kategorisi / tedarikçi cari) ----

    /// <summary>Faturanın ilişkilendirildiği araç. Giderleştirmede <c>Expense.VehicleId</c>'ye taşınır
    /// (araç karnesi/karlılık atfı buradan gelir).</summary>
    public Guid? VehicleId { get; set; }
    /// <summary>Gider kategorisi (<see cref="ExpenseCategory"/>) — Periyodik Servis / Hasar-Kaza /
    /// Mekanik Arıza / Bakım vb. Giderleştirmede açıklamaya yazılır (Expense'te kategori kolonu YOK).</summary>
    public Guid? ExpenseCategoryId { get; set; }
    /// <summary>Tedarikçi cari — açık-hesap giderleştirmede Alacak tarafı.</summary>
    public Guid? CariId { get; set; }
    /// <summary>Oluşacak giderin türü. null → araç bağlıysa <see cref="ExpenseType.Arac"/>, değilse
    /// <see cref="ExpenseType.Genel"/>. Nullable: geçmiş satırlara "Genel" anlamı yüklememek için.</summary>
    public ExpenseType? GiderTipi { get; set; }

    // ---- FAZ-55 (b) GİDERLEŞTİRME İZİ (defter bağı) ----

    /// <summary>Giderleştirme anı (UTC). Dolu ⇒ bu fatura deftere yansımıştır: kırılım/bağlama alanları
    /// ARTIK DEĞİŞTİRİLEMEZ (defterle belge diverge etmesin) ve ikinci kez giderleştirilemez.</summary>
    public DateTimeOffset? GiderlestirilmeUtc { get; set; }
    /// <summary>Giderleştirmede kullanılan deterministik idempotency batch anahtarı (= <see cref="Id"/>).
    /// Oluşan her <c>Expense</c> satırı <c>CashService.RowKey(anahtar, i)</c> alır; Expenses üzerindeki
    /// kısmi unique index <c>(TenantId, IslemAnahtari)</c> ikinci yazımı DB seviyesinde imkânsız kılar.</summary>
    public Guid? GiderIslemAnahtari { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
