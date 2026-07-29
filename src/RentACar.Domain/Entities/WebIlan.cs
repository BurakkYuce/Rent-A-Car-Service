using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>PR-13 ilan yaşam döngüsü. Sihirbaz adım-1'de <see cref="Taslak"/> yaratır; adım-3
/// tamamlanınca <see cref="Yayinda"/> olur. <see cref="Pasif"/> personelin bilinçli gizlemesidir
/// (silmek değil — araç üyelikleri ve fiyat korunur).</summary>
public enum WebIlanDurum
{
    Taslak = 0,
    Yayinda = 1,
    Pasif = 2
}

/// <summary>
/// PR-13 — halka açık sitede görünen İLAN. Bir ilan bir aracı ya da birbirinin aynısı BİRDEN ÇOK
/// aracı temsil eder (üyelik <see cref="Vehicle.WebIlanId"/> ile).
///
/// <para><b>Fiyat motordan BAĞIMSIZDIR.</b> Kullanıcı kararı: "tarife matrisini yönetmek ilk
/// müşteriler için çok karışık". <c>RateMatrix</c>/<c>RentalQuoteEngine</c> halka açık yoldan
/// tamamen çıkar; ERP içi kira/sözleşme için olduğu gibi kalır.</para>
///
/// <para><b>TUZAK — alan anlamları:</b> <see cref="HaftalikToplam"/> ve <see cref="AylikToplam"/>
/// TOPLAM tutardır (7 günün / 30 günün parası). <c>RateMatrix.GunHaftalik</c>/<c>GunAylik</c> ise
/// o kademenin GÜNLÜK ücretidir — aynı ada/anlama sahip DEĞİLLER. Bu yüzden burada bilinçli olarak
/// farklı adlandırıldılar; karıştırmak fiyatı ~7 kat yanlış basar.</para>
/// </summary>
public class WebIlan : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kartta görünen ad (ör. "Fiat Egea Manuel Dizel"). Sihirbaz araçlardan türetir, personel değiştirebilir.</summary>
    public string Baslik { get; set; } = string.Empty;

    /// <summary>
    /// "Aynı araç" imzası: <c>Normalize(Marka)|Normalize(Tip)|Vites|Yakit</c>. Sonradan filoya
    /// eklenen araçların hangi ilana ait olabileceğini bulmak ve operatörün İKİZ ilan yaratmasını
    /// engellemek için saklanır.
    ///
    /// <para>UNIQUE DEĞİL ve olamaz: "ayrı göster" modu aynı anahtardan bilinçli olarak N ilan
    /// üretir (12 aracı 12 ayrı kart yapmak). Tekillik ihtiyacı "beraber" modda servis
    /// katmanında zorlanır.</para>
    /// </summary>
    public string? EslesmeAnahtari { get; set; }

    /// <summary>Vitrin sırası (küçük önce). Eşitlikte <c>Baslik</c> ile tie-break edilir —
    /// Postgres eşit değerlerde sıra GARANTİ ETMEZ (SEO + test kararlılığı).</summary>
    public int? Sira { get; set; }

    public WebIlanDurum Durum { get; set; } = WebIlanDurum.Taslak;

    /// <summary>1–7 gün için günlük ücret. Yayın kapısının fiyat şartı: &gt; 0.</summary>
    public decimal GunlukFiyat { get; set; }

    /// <summary>7 günlük TOPLAM (günlük eşdeğeri = /7). 8–29 gün aralığında kullanılır. Boşsa günlüğe düşülür.</summary>
    public decimal? HaftalikToplam { get; set; }

    /// <summary>30 günlük TOPLAM (günlük eşdeğeri = /30). 30+ gün için kullanılır. Boşsa haftalığa, o da yoksa günlüğe düşülür.</summary>
    public decimal? AylikToplam { get; set; }

    /// <summary>Girilen fiyatlara KDV dahil mi. Kart etiketi ("KDV dahil" / "+ KDV") buna göre basılır;
    /// talep rezervasyona dönüşürken ERP'nin NET beklentisine çevirmek için de gerekir.</summary>
    public bool KdvDahil { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}

/// <summary>
/// PR-13 — ilanın sitede gösterilecek teknik özellik satırı. Sihirbaz araç/grup alanlarından
/// SNAPSHOT üretir, personel hangilerinin görüneceğini işaretler ve "+" ile özel satır ekler.
///
/// Snapshot bilinçli: sihirbaz, sitede NE yazacağının kürasyon yeridir. ERP'de araç düzeltilirse
/// ilan eski değeri göstermeye devam eder — bu sessiz kalmasın diye tanılama uyarısı vardır.
/// </summary>
public class WebIlanOzellik : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid IlanId { get; set; }

    public string Etiket { get; set; } = string.Empty;
    public string Deger { get; set; } = string.Empty;
    public int Sira { get; set; }

    /// <summary>Sitede görünsün mü. Snapshot tüm alanları üretir, personel gereksizleri kapatır.</summary>
    public bool Gorunur { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
