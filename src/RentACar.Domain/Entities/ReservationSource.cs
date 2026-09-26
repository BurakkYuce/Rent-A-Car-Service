using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Rezervasyon kaynağı tanımı (master sözlük): rezervasyon/müşteri kaynaklarının adlandırılmış
/// listesi (ör. "Web", "Telefon", "Bayi", "Tavsiye"). Tenant-owned + auditable. Rezervasyon ve
/// cari formlarındaki Kaynak açılır listesini besler (additive).
/// </summary>
public class ReservationSource : ITenantOwned, IAuditable, IMasterDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kısa kod (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public bool Aktif { get; set; } = true;

    /// <summary>Kaynağın arkasındaki tedarikçi/acente adı (ör. "Rentalcars", "Booking").</summary>
    public string? Tedarikci { get; set; }

    // FAZ-24 — ORAN ALANLARI YÜZDEDİR (12,5 = %12,5), oransal katsayı DEĞİL. Bu faz alanları
    // yalnız KAYDEDER: hiçbir fiyat/komisyon/karlılık hesabı bunları OKUMAZ. "Hangi hesaba,
    // ne zaman girecek" ayrı bir para incelemesidir (bkz. docs/KARARLAR.md "Açık işler" → FAZ-24). Bir tüketici
    // eklenmeden önce yüzde/katsayı birimi ve geçmişe etki sorusu cevaplanmalıdır.

    /// <summary>Kira bedeli üzerinden tedarikçi oranı — YÜZDE (12,5 = %12,5). Hesaba girmez.</summary>
    public decimal? KiraOrani { get; set; }

    /// <summary>Ek hizmet bedeli üzerinden tedarikçi oranı — YÜZDE. Hesaba girmez.</summary>
    public decimal? HizmetOrani { get; set; }

    /// <summary>Drop (tek yön) bedeli üzerinden tedarikçi oranı — YÜZDE. Hesaba girmez.</summary>
    public decimal? DropOrani { get; set; }

    // =====================================================================================
    // FAZ-49 — KURAL MATRİSİ. Alanlar İKİ SINIFA ayrılır ve bu ayrım KASITLIDIR:
    //
    //   (1) KURAL BAYRAKLARI  — rezervasyon/kira akışında GERÇEKTEN uygulanır (guard).
    //   (2) BİLGİ ALANLARI    — yalnız saklanır; hiçbir fiyat/komisyon/defter hesabına GİRMEZ.
    //
    // (2)'nin gerekçesi docs/KARARLAR.md "GENEL POLİTİKA — yeni tutar alanları deftere
    // yazmaz" + "FAZ-49" kararıdır: gerçek para hareketi Kasa/Banka akışından geçer; ikinci bir
    // yol açmak çift-sayım üretir. Bir gün bu oranlar bir hesaba bağlanacaksa AYRI bir para fazı
    // (zorunlu adversarial inceleme) açılır — sessizce bağlamak, alanı "not" sanıp dolduran
    // kullanıcının faturasını değiştirir.
    // =====================================================================================

    /// <summary>Kaynağın üst grubu (bilgi/raporlama). null = belirtilmemiş (geçmiş kayıtlar).</summary>
    public ReservationSourceGroup? KaynakGrubu { get; set; }

    // ---- (1) KURAL BAYRAKLARI — UYGULANIR ------------------------------------------------

    /// <summary>KURAL: bu kaynaktan gelen rezervasyon/kira UZATILAMAZ
    /// (<c>RentalService.ExtendAsync</c> + rezervasyon bitişini ileri alma reddedilir).</summary>
    public bool Uzatamaz { get; set; }

    /// <summary>KURAL: rezervasyon tarihleri (başlangıç/bitiş) DEĞİŞTİRİLEMEZ
    /// (<c>ReservationService.UpdateAsync</c>; diğer alanların düzenlenmesi serbest kalır).</summary>
    public bool RezTarihleriDegisemez { get; set; }

    /// <summary>KURAL: bu kaynakta provizyon (bloke) alınmaz — <c>RentalService.ProvizyonAlAsync</c> reddeder.</summary>
    public bool ProvizyonYok { get; set; }

    /// <summary>KURAL: km sınırsız — bu kaynaktan açılan rezervasyon/kirada <c>KmLimit</c> 0'a
    /// (=sınırsız) sabitlenir; dönüşte fazla km bedeli çıkmaz (<c>ReturnMath</c> yalnız KmLimit&gt;0'da hesaplar).</summary>
    public bool KmSinirsiz { get; set; }

    /// <summary>KURAL: tek yön (drop) kapalı — dönüş ofisi çıkış ofisiyle AYNI olmalıdır.</summary>
    public bool AyniYonDrop { get; set; }

    /// <summary>KURAL: bu kaynakta izin verilen en fazla gün. null/0 = sınır yok. Oluşturma ve
    /// uzatmada aşılırsa temiz red.</summary>
    public int? MaxGun { get; set; }

    // ---- (2) BİLGİ ALANLARI — HESABA GİRMEZ ----------------------------------------------

    /// <summary>BİLGİ: maliyet yansıtma bayrağı (canlı parite; tüketicisi yok).</summary>
    public bool MaliyetYansitma { get; set; }

    /// <summary>BİLGİ: erken dönüş davranışı matriste işaretli mi.</summary>
    public bool MatrisErken { get; set; }
    /// <summary>BİLGİ: geç dönüş davranışı matriste işaretli mi.</summary>
    public bool MatrisGecikme { get; set; }
    /// <summary>BİLGİ: iptal davranışı matriste işaretli mi.</summary>
    public bool MatrisIptal { get; set; }
    /// <summary>BİLGİ: no-show davranışı matriste işaretli mi.</summary>
    public bool MatrisNoShow { get; set; }
    /// <summary>BİLGİ: uzatma davranışı matriste işaretli mi (uzatma YASAĞI için <see cref="Uzatamaz"/>).</summary>
    public bool MatrisUzatma { get; set; }

    /// <summary>BİLGİ: sigorta kaynağı/poliçe referans no (serbest metin).</summary>
    public string? SigortaKaynakNo { get; set; }
    /// <summary>BİLGİ: drop kaynağı referans no (serbest metin).</summary>
    public string? DropKaynakNo { get; set; }
    /// <summary>BİLGİ: provizyon seçeneği (serbest metin; POS akışına bağlı DEĞİL).</summary>
    public string? ProvizyonSecenek { get; set; }
    /// <summary>BİLGİ: muafiyet seçeneği (serbest metin).</summary>
    public string? MuafiyatSecenek { get; set; }

    /// <summary>BİLGİ: SCDW paketi kaynak fiyatına dahil mi.</summary>
    public bool ScdwDahil { get; set; }
    /// <summary>BİLGİ: CDW paketi kaynak fiyatına dahil mi.</summary>
    public bool CdwDahil { get; set; }
    /// <summary>BİLGİ: LCF paketi kaynak fiyatına dahil mi.</summary>
    public bool LcfDahil { get; set; }
    /// <summary>BİLGİ: PAI paketi kaynak fiyatına dahil mi.</summary>
    public bool PaiDahil { get; set; }

    // Ek hizmet VARSAYILAN TUTARLARI — para birimi tenant tabanı; fiyat motoru bunları OKUMAZ
    // (ek hizmet satırları RentalAddOn/FeeLineService üzerinden gelir).
    /// <summary>BİLGİ: bebek koltuğu varsayılan tutarı. Fiyata girmez.</summary>
    public decimal? BebekKoltugu { get; set; }
    /// <summary>BİLGİ: navigasyon varsayılan tutarı. Fiyata girmez.</summary>
    public decimal? Navigasyon { get; set; }
    /// <summary>BİLGİ: ek sürücü varsayılan tutarı. Fiyata girmez.</summary>
    public decimal? EkSurucu { get; set; }
    /// <summary>BİLGİ: wifi varsayılan tutarı. Fiyata girmez.</summary>
    public decimal? Wifi { get; set; }

    // ORANLAR (KARARLAR.md FAZ-49) — YÜZDE (12,5 = %12,5). Komisyon/önödeme/indirim/puan
    // hiçbir hesaba GİRMEZ; yalnız kaynak sözleşmesinin kaydıdır.
    /// <summary>BİLGİ: acente/broker komisyon oranı — YÜZDE. Hesaba girmez.</summary>
    public decimal? KomisyonOrani { get; set; }
    /// <summary>BİLGİ: ön ödeme oranı — YÜZDE. Hesaba girmez.</summary>
    public decimal? OnOdemeOrani { get; set; }
    /// <summary>BİLGİ: kaynak indirim oranı — YÜZDE. Fiyat motoru OKUMAZ (indirim tarife/kampanya kuralından gelir).</summary>
    public decimal? IndirimOrani { get; set; }
    /// <summary>BİLGİ: puan/prim oranı — YÜZDE. Hesaba girmez.</summary>
    public decimal? PuanOrani { get; set; }

    /// <summary>BİLGİ: kaynak bildirim e-posta adresi (gönderim entegrasyonu BLOKE — stub).</summary>
    public string? MailAdres { get; set; }
    /// <summary>BİLGİ: otomatik mail gönderilsin mi (e-posta entegrasyonu credential ister — gönderim YOK).</summary>
    public bool OtomatikMailGitme { get; set; }

    /// <summary>BİLGİ: risk analizi yapılmasın işareti. <b>Mevcut risk guard'ı GEVŞETMEZ</b> —
    /// cari risk limiti kontrolü (RentalService) bu bayraktan bağımsız çalışmaya devam eder.</summary>
    public bool RiskAnalizYapma { get; set; }
    /// <summary>BİLGİ: şube görebilsin işareti. Şube kapsamı (BranchScope) bu alandan ETKİLENMEZ.</summary>
    public bool SubeGor { get; set; }
    /// <summary>BİLGİ: acente fiyat değiştirebilir işareti (acente paneli BLOKE — D8).</summary>
    public bool AcenteFiyatDegistir { get; set; }
    /// <summary>BİLGİ: kaynak listelerde gizlensin işareti (açılır liste filtresi bu fazın kapsamı dışında).</summary>
    public bool Gizle { get; set; }
    /// <summary>BİLGİ: yalnız müşteri ödemesi kabul edilir işareti (tahsilat yönlendirmesi yok).</summary>
    public bool SadeceMusteriOdeme { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
