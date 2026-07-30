namespace RentACar.Domain.Entities;

/// <summary>PR-B belge yaşam döngüsü. <see cref="Taslak"/> yüklendi ama tenant GÖRMÜYOR —
/// "yükle → kontrol et → yayınla" akışı için şart; aksi halde her yükleme anında canlıya çıkar.
/// <see cref="Arsiv"/> listeden düşer ama kayıt ve dosya DURUR ("hangi sürümü indirdiler" cevaplanabilir kalsın).</summary>
public enum PlatformBelgeDurum
{
    Taslak = 0,
    Yayinda = 1,
    Arsiv = 2
}

/// <summary>
/// PR-B — platformdan tenant'lara dağıtılan hazır PDF (kılavuz, KVKK metni, boş sözleşme nüshası,
/// fiyat listesi, duyuru). Bizim düzenlememizi gerektirmez; tenant yalnız listeler ve indirir.
///
/// <para><b>PLATFORM tablosu — RLS YOK.</b> <see cref="Common.ITenantOwned"/> DEĞİL, dolayısıyla
/// <c>AppDbContext.OnModelCreating</c>'deki merkezi tenant-filtre döngüsü buna DOKUNMAZ. Sonuç:
/// izolasyon RLS'e yıkılamaz, <b>uygulama katmanında elle</b> kurulur (<c>UserRepository</c> deseni).
/// Yapılmazsa belge ID'si deneyen bir tenant başkasının hedefli belgesini indirir.</para>
///
/// <para><b>Sürümleme:</b> güncel hal için YENİ KAYIT AÇILMAZ — mevcut kaydın dosyası değişir,
/// <see cref="Surum"/> artar, <see cref="GuncellemeUtc"/> yenilenir. Böylece tenant'ın elindeki
/// link kırılmaz ve "v2 · 3 gün önce güncellendi" gösterilebilir.</para>
///
/// <para><c>BelgeSablon</c> ile KARIŞTIRILMAMALI: o marka-özel <b>metin şablonu</b> (sözleşme
/// başlığı/hukuki metin); bu <b>dosya dağıtımı</b>. Menüde de ayrı adlandırıldı ("Firma Belgeleri").</para>
/// </summary>
public class PlatformBelge
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Baslik { get; set; } = string.Empty;
    public string? Aciklama { get; set; }

    /// <summary>İndirmede kullanılacak dosya adı. ASCII tutulur — Türkçe karakter
    /// `Content-Disposition` başlığında RFC 5987 encode gerektirir, gereksiz karmaşa.</summary>
    public string DosyaAdi { get; set; } = string.Empty;

    /// <summary>PDF içeriği. <b>Liste sorgularına ASLA girmez</b> (20 belgeyi listelerken 20 PDF'i
    /// belleğe almak — blog kapağı dersi); yalnız indirme ucu okur.</summary>
    public byte[] Bytes { get; set; } = [];
    public long Boyut { get; set; }

    /// <summary>Her dosya güncellemesinde artar. ETag'in parçası → sürüm artınca tarayıcı cache'i tazelenir.</summary>
    public int Surum { get; set; } = 1;

    public PlatformBelgeDurum Durum { get; set; } = PlatformBelgeDurum.Taslak;

    /// <summary>true → belgeyi yalnız Admin/Yonetici görür (hassas belge). Varsayılan KAPALI:
    /// KVKK metnini muhasebeci de görmeli.</summary>
    public bool YalnizYoneticiler { get; set; }

    public string? YukleyenOperator { get; set; }
    public DateTimeOffset GuncellemeUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// PR-B — belgenin hedef kitlesi. <b>Bir belge için HİÇ satır yoksa belge GLOBAL'dir</b> (tüm
/// tenant'lar görür); satır varsa yalnız o tenant'lar görür (firmaya özel sözleşme eki, özel
/// fiyat anlaşması).
///
/// Platform tablosu — RLS yok; erişim kontrolü <c>PlatformBelgeService</c>'te.
/// </summary>
public class PlatformBelgeHedef
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BelgeId { get; set; }
    public Guid TenantId { get; set; }
}
