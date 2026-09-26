using RentACar.Domain.Enums;

namespace RentACar.Application.Fleet;

/// <summary>
/// Araç Güncel Durum gridi satırı: araç kimlik/filo bilgisi + (varsa) aktif kira özeti.
/// Salt-okunur projeksiyon — araç + aktif RentalContract + müşteri birleştirilir.
///
/// <para>FAZ-11: ekran salt-okunur panodan <b>aksiyon konsoluna</b> döndü; satırın "şu an ne
/// oluyor" özeti kira ile sınırlı değil — açık servis, açık BAF tahsisi, sıradaki rezervasyon ve
/// (varsa) uzun-dönem filo kiralama dosyası da aynı satırda görünür. Hepsi <b>canlı</b> çözülür;
/// araç kartındaki <c>Kiralayan</c>/<c>KiraBitTar</c> gibi elle girilen "not" alanları kaynak
/// DEĞİLDİR (VehicleDetayRow'daki aynı ayrım).</para>
/// </summary>
public sealed class FleetStatusRow
{
    public Guid VehicleId { get; init; }
    public string Plaka { get; init; } = string.Empty;
    public string? Marka { get; init; }
    public string? Tip { get; init; }
    public string? Grup { get; init; }
    public string? Segment { get; init; }
    public string? Sipp { get; init; }
    public Transmission? Vites { get; init; }
    /// <summary>PR-21: null = "girilmedi" (araç kaydında yakıt seçilmemiş).</summary>
    public FuelType? Yakit { get; init; }
    public int Km { get; init; }
    public string? Sube { get; init; }

    /// <summary>Operasyonel durum (Boş/Kirada/Serviste…).</summary>
    public VehicleStatus Durum { get; init; }
    /// <summary>Filo yaşam döngüsü statüsü (stok/havuz/tahsis…).</summary>
    public FleetLifecycleStatus? FiloDurum { get; init; }

    // ---- FAZ-11: araç künyesinden operasyon kolonları (filtrelerle aynı alanlar) ----
    /// <summary>Araç pasife alındıysa gerekçesi (FAZ-10 alanı).</summary>
    public string? PasifSebep { get; init; }
    /// <summary>Aracın fiziksel konumu (FAZ-10; şube/ofis FK'si DEĞİL).</summary>
    public string? Konum { get; init; }
    /// <summary>GPS takip cihazı numarası (FAZ-10) — HGS/OGS'den ayrı cihaz.</summary>
    public string? TakipNo { get; init; }
    public string? HgsNo { get; init; }
    public bool KarLastigi { get; init; }
    public bool WebRezKapat { get; init; }
    public bool OfisRezKapat { get; init; }

    // Aktif kira (yoksa null)
    public Guid? AktifKiraId { get; init; }
    public string? KiraSozlesmeNo { get; init; }
    /// <summary>F6.1a — aktif kira müşterisinin kimliği (JSON ucu görünen adı KVKK tek kuralıyla yeniden çözer).</summary>
    public Guid? MusteriId { get; init; }
    /// <summary>F6.1a — sıradaki rezervasyon müşterisinin kimliği (aynı amaç).</summary>
    public Guid? RezMusteriId { get; init; }
    public string? MusteriAd { get; init; }
    /// <summary>Aktif kira müşterisinin cep telefonu — operatör satırdan arayabilsin diye
    /// (canlı arac_guncel_durum'un "Cep Tel" kolonu). Şifreli PII değildir (TC/ehliyet/pasaport
    /// şifreli, telefon değil) — ek bir çözme adımı gerekmez.</summary>
    public string? MusteriTel { get; init; }
    public DateTimeOffset? KiraBitTar { get; init; }
    public decimal? KiraBakiye { get; init; }
    /// <summary>Kira bitişine kalan GÜN (negatif = gecikmiş). <see cref="RemainingDays"/> ile hesaplanır.</summary>
    public int? KiraKalanGun { get; init; }

    // ---- Sıradaki rezervasyon (araç boşta olsa bile "yarın kime gidiyor" bilgisi) ----
    public string? RezMusteriAd { get; init; }
    public DateTimeOffset? RezBasTar { get; init; }

    // ---- Açık servis kaydı (Acik veya Serviste) ----
    public string? AcikServisNo { get; init; }
    public string? ServisAtolye { get; init; }

    // ---- Açık BAF tahsisi (araç personelde) ----
    public string? AktifBafNo { get; init; }
    public string? BafPersonelAd { get; init; }

    /// <summary>Aktif uzun-dönem filo kiralama sözleşmesinin DOSYA numarası (varsa).</summary>
    public string? DosyaNo { get; init; }

    public bool Kirada => AktifKiraId is not null;
    /// <summary>Açık servis kaydı var mı (satır-içi "Servise Al" aksiyonu buna göre gizlenir).</summary>
    public bool Serviste => AcikServisNo is not null;
    /// <summary>Açık BAF tahsisi var mı.</summary>
    public bool Bafta => AktifBafNo is not null;

    /// <summary>
    /// Kira bitişine kalan gün — SAF fonksiyon (bağımsız test edilebilir; saat/dakika değil
    /// TAKVİM GÜNÜ farkı). Aynı gün biten kira 0, yarın biten 1, dün bitmiş olan −1 döner.
    ///
    /// <para>Tuzak: <c>(bit - now).TotalDays</c> üzerinden yuvarlamak, saat farkına göre aynı
    /// senaryoda 2 ile 3 arasında sallanan bir sayı üretiyordu (ve testi CI'da kırılgan yapıyordu).
    /// UTC tarih tabanına indirgemek hem kullanıcının "kaç gün kaldı" sezgisiyle hem de
    /// deterministik testle uyumlu.</para>
    /// </summary>
    public static int RemainingDays(DateTimeOffset bitTar, DateTimeOffset now)
        => (bitTar.UtcDateTime.Date - now.UtcDateTime.Date).Days;
}
