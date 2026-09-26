using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Talebin (lead) yaşam döngüsü.
///
/// <para><b>YENİ DEĞERLER YALNIZ SONA EKLENİR.</b> Kolon <c>int</c> saklanıyor
/// (<c>HasConversion&lt;int&gt;()</c>); araya bir değer sokmak mevcut satırların ANLAMINI kaydırır
/// (ör. <see cref="Iletisimde"/> 1'e konsa bugünün <see cref="Donustu"/> satırları bir gecede
/// "İletişimde" olurdu). UI sıralaması bu numaralardan BAĞIMSIZ yazılır.</para>
///
/// <para><b>TERMİNAL durumlar:</b> <see cref="Donustu"/>, <see cref="Reddedildi"/>, <see cref="Kayip"/>
/// — bunlardan çıkış yok (bkz. <see cref="TalepDurumu.Terminal"/>). Diğerleri "üzerinde çalışılıyor".</para>
/// </summary>
public enum PublicBookingRequestDurum
{
    Yeni = 0,
    Donustu = 1,
    Reddedildi = 2,
    /// <summary>PR-17: personel müşteriyle temas kurdu, sonuç bekleniyor.</summary>
    Iletisimde = 3,
    /// <summary>PR-17: fiyat/araç teklifi iletildi, müşterinin cevabı bekleniyor.</summary>
    TeklifVerildi = 4,
    /// <summary>PR-17: müşteri vazgeçti ya da ulaşılamadı — "Reddedildi"den farkı, RED bizden değil.</summary>
    Kayip = 5,
}

/// <summary>
/// Durum kuralları TEK yerde. Hem servis guard'ı hem repository'nin atomik claim yüklemi buradan
/// okur — kopyalanırsa biri "İletişimde" talebi dönüştürülemez hale getirir (bu PR'da tam olarak
/// bu regresyon yakalandı: eski claim yüklemi <c>Durum == Yeni</c> idi).
/// </summary>
public static class TalepDurumu
{
    /// <summary>Çıkışı olmayan durumlar — üzerinde başka işlem yapılamaz.</summary>
    public static bool Terminal(PublicBookingRequestDurum d)
        => d is PublicBookingRequestDurum.Donustu
             or PublicBookingRequestDurum.Reddedildi
             or PublicBookingRequestDurum.Kayip;

    /// <summary>Hâlâ üzerinde çalışılan durumlar (claim edilebilir, durumu değiştirilebilir).</summary>
    public static bool IsActive(PublicBookingRequestDurum d) => !Terminal(d);

    /// <summary>Ekranda gösterim sırası — enum numaralarından BAĞIMSIZ (bkz. append-only kuralı).</summary>
    public static readonly PublicBookingRequestDurum[] GosterimSirasi =
    [
        PublicBookingRequestDurum.Yeni,
        PublicBookingRequestDurum.Iletisimde,
        PublicBookingRequestDurum.TeklifVerildi,
        PublicBookingRequestDurum.Donustu,
        PublicBookingRequestDurum.Reddedildi,
        PublicBookingRequestDurum.Kayip,
    ];

    public static string Label(PublicBookingRequestDurum d) => d switch
    {
        PublicBookingRequestDurum.Yeni => "Yeni",
        PublicBookingRequestDurum.Iletisimde => "İletişimde",
        PublicBookingRequestDurum.TeklifVerildi => "Teklif verildi",
        PublicBookingRequestDurum.Donustu => "Dönüştü",
        PublicBookingRequestDurum.Reddedildi => "Reddedildi",
        PublicBookingRequestDurum.Kayip => "Kayıp",
        _ => d.ToString(),
    };
}

/// <summary>
/// Halka açık siteden gelen rezervasyon TALEBİ (lead) — PR-8. GERÇEK rezervasyon DEĞİLDİR:
/// <see cref="Reservation"/> `PermissionGuard.Require(OperationsWrite)` + var olan `MusteriId` ister,
/// anonim ziyaretçi bunları sağlayamaz. Personel talebi sonradan mevcut Cari+Rezervasyon akışına
/// DÖNÜŞTÜRÜR (bkz. PublicBookingRequestService.DonusturAsync).
///
/// PII sınırı (bilinçli): TcKimlik/EhliyetNo TOPLANMAZ — yalnız Ad Soyad/Telefon/E-posta
/// (`CustomerContact` ile aynı hassasiyet sınıfı) → F2 şifreleme/blind-index GEREKMEZ.
/// </summary>
public class PublicBookingRequest : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string AdSoyad { get; set; } = string.Empty;
    public string Telefon { get; set; } = string.Empty;
    public string? Email { get; set; }

    /// <summary>PR-7 aramasından "Teklif İste" ile gelindiyse dolu (grup KODU).
    /// PR-14: ARTIK YAZILMIYOR — vitrin ilan bazlı oldu, yerini <see cref="IlanId"/> aldı.
    /// Kolon eski talepler için duruyor (veri silinmez).</summary>
    public string? AracGrupKod { get; set; }

    /// <summary>PR-14: talebin geldiği ilan. Personel dönüştürürken müşterinin HANGİ aracı
    /// gördüğünü ve fiyatını bilsin.</summary>
    public Guid? IlanId { get; set; }

    /// <summary>İlan başlığının o ANKİ hâli. İlan sonradan yeniden adlandırılsa/silinse bile
    /// personel müşterinin ne gördüğünü görebilmeli (snapshot).</summary>
    public string? IlanBaslik { get; set; }

    public DateTimeOffset BasTar { get; set; }
    public DateTimeOffset BitTar { get; set; }
    public string? Sube { get; set; }
    public string? Not { get; set; }

    /// <summary>Ziyaretçinin arama sonucunda GÖRDÜĞÜ günlük fiyat. Doğrudan forma gelen taleplerde null.
    ///
    /// <para>PR-14 UYARI: kolon adı "KdvDahil" diyor ama ARTIK HER ZAMAN ÖYLE DEĞİL — ilan başına
    /// KDV işareti var, bu rakam <see cref="GosterilenKdvDahil"/> ile birlikte okunmalıdır. Ad
    /// tarihsel; kolon yeniden adlandırılmadı (mevcut veriyi ve migration zincirini kırmamak için).</para></summary>
    public decimal? GosterilenGunlukUcretKdvDahil { get; set; }

    /// <summary>PR-14: <see cref="GosterilenGunlukUcretKdvDahil"/> KDV dahil miydi. Personelin
    /// rezervasyona NET fiyat yazabilmesi için şart (ERP zincirinin tamamı NET çalışır).</summary>
    public bool? GosterilenKdvDahil { get; set; }

    public PublicBookingRequestDurum Durum { get; set; }

    /// <summary>Dönüştürme sonucu oluşan rezervasyon. `Durum=Donustu` ama bu NULL ise: dönüştürme
    /// yarıda kalmış demektir (bkz. servisin claim/release notu) — staff ekranı bunu uyarı olarak gösterir.</summary>
    public Guid? DonusenReservationId { get; set; }

    /// <summary>
    /// PR-17: talebi üstlenen personel. ATAMA YALNIZ KENDİNE yapılır ("Bana ata") — başka bir
    /// kullanıcıya atamak, <c>ManageUsers</c> kilidi ardındaki kullanıcı listesini bu ekrana taşımayı
    /// gerektirirdi ve Operatör rolünde patlardı (Personel dropdown'ında yaşanan tuzağın aynısı).
    /// Ad DENORMALİZE saklanıyor: liste ekranı için <c>Users</c>'a join etmeye gerek kalmasın.
    /// </summary>
    public Guid? AtananKullaniciId { get; set; }
    public string? AtananAd { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}

/// <summary>
/// PR-17 — talep takip notu ("aradım, akşam tekrar arayacağım"). Bir lead'in neden hâlâ açık
/// olduğunu ancak bu satırlar açıklar; tek bir "Not" alanı üzerine yazılırdı ve geçmiş kaybolurdu.
///
/// <para>SİLİNMEZ: takip geçmişi kanıttır (müşteri "kimse aramadı" derse cevap burada). Bu yüzden
/// silme metodu YOK — yalnız ekleme.</para>
/// </summary>
public class TalepNotu : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid TalepId { get; set; }

    public string Metin { get; set; } = string.Empty;

    /// <summary>Notu yazan personel (denormalize — liste için join gerekmesin).</summary>
    public string? Kullanici { get; set; }

    public DateTimeOffset ZamanUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
