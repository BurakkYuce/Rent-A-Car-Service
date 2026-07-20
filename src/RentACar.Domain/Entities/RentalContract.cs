using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Kira sözleşmesi (sistemin kalbi — PR #3 çekirdeği). Tenant-owned + auditable.
/// Aktif (Kirada) sözleşmeler için araç+tarih çakışması DB-seviyesi exclusion
/// constraint ile engellenir (double-booking koruması). Teslim (Çıkış KM/yakıt) ve
/// dönüş (Dönüş KM/yakıt/uzatma) alanları PR #4'te doldurulur.
/// </summary>
public class RentalContract : ITenantOwned, IAuditable, IOfficeScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz sözleşme no (örn. KS-000001).</summary>
    public string SozlesmeNo { get; set; } = string.Empty;

    public RentalStatus Durum { get; set; } = RentalStatus.Kirada;

    /// <summary>Hangi rezervasyondan dönüştü (varsa).</summary>
    public Guid? ReservationId { get; set; }

    public Guid MusteriId { get; set; }
    public Guid VehicleId { get; set; }

    public DateTimeOffset BasTar { get; set; }
    public DateTimeOffset BitTar { get; set; }

    public string? CikisOfisi { get; set; }
    /// <summary>Türetilmiş çıkış-şube FK'sı (FAZ 5-C4; CikisOfisi→Location→SubeId; interceptor doldurur).</summary>
    public Guid? CikisSubeId { get; set; }
    string? RentACar.Domain.Common.IOfficeScoped.OfisAdi => CikisOfisi;
    Guid? RentACar.Domain.Common.IOfficeScoped.OfisSubeFk { get => CikisSubeId; set => CikisSubeId = value; }
    public string? DonusOfisi { get; set; }

    // Aşım ücret parametreleri (oluştururken/teslimde girilir; 0 = ücretsiz/sınırsız).
    public int KmLimit { get; set; }            // toplam serbest km (0 = sınırsız)
    public decimal FazlaKmUcret { get; set; }   // aşım km başına
    public decimal YakitBirimUcret { get; set; } // eksik yakıt birimi başına

    // Teslim (PR #4)
    public int? CikisKm { get; set; }
    public int? CikisYakit { get; set; }

    // Dönüş (PR #4)
    public int? DonusKm { get; set; }
    public int? DonusYakit { get; set; }
    public DateTimeOffset? GercekDonusTar { get; set; }

    // Dönüşte hesaplanan ek bedeller
    public int FazlaKm { get; set; }
    public decimal FazlaKmBedeli { get; set; }
    public int EksikYakit { get; set; }
    public decimal YakitBedeli { get; set; }
    public int UzatmaGun { get; set; }
    public decimal UzatmaBedeli { get; set; }

    // ---- Dönüş ek alanları (TürevRent parite; additive) ----
    /// <summary>Aşımdan düşülen bedava km (KM Hediye) — dönüşte girilir; FazlaKm hesabında KmLimit'e eklenir.</summary>
    public int? KmHediye { get; set; }
    /// <summary>Dönüş/bitiş sebebi (Normal/Erken İade/Hasar/Arıza/Değişim/Diğer — serbest metin).</summary>
    public string? BitisSebebi { get; set; }
    /// <summary>Dönüşü teslim alan personel (gevşek referans — Personel.Id; FK yok).</summary>
    public Guid? TeslimAlanPersonelId { get; set; }

    /// <summary>2. sürücü (opsiyonel) — Customer bağı (gevşek referans; PII Customer'da şifreli, sözleşmede
    /// decrypt'li gösterilir). Oluşturma anında yakalanır (create-time otorite).</summary>
    public Guid? IkinciSurucuId { get; set; }

    // ---- Tam teklif bileşenleri (fiyat motoru "Otomatik" — BİLGİ/döküm; Tutar zaten net brütü içerir,
    // bunlar Tutar'a AYRICA katılmaz → çift-sayım yok; KURAL A) ----
    /// <summary>Hediye (bedava) gün sayısı — kiralama kuralından. FaturalananGun = Gun − HediyeGun.</summary>
    public int? HediyeGun { get; set; }
    /// <summary>Faturalanan gün (Gun − HediyeGun) — motordan.</summary>
    public int? FaturalananGun { get; set; }
    /// <summary>Uygulanan iskonto tutarı (brüt) — kural iskonto oranından hesaplandı; Tutar'a zaten yansıdı.</summary>
    public decimal? IskontoTutar { get; set; }
    /// <summary>Hafta sonu farkı (brüt) — Cmt/Pzr günlerine ek; Tutar'a zaten yansıdı.</summary>
    public decimal? HaftaSonuFark { get; set; }

    public int Gun { get; set; }
    public decimal GunlukUcret { get; set; }
    public decimal Tutar { get; set; }          // baz kira tutarı
    public decimal GenelToplam { get; set; }    // Tutar + ek bedeller
    public decimal Tahsilat { get; set; }
    public decimal Bakiye { get; set; }          // GenelToplam - Tahsilat

    // ---- Ödeme-derinlik alanları (roadmap A2; additive, NULLABLE) ----
    // BİLGİ AMAÇLI: deftere/bakiyeye YANSIMAZ (GenelToplam/Tahsilat/Bakiye'yi etkilemez). Provizyon/depozito
    // bloke tutarlar; komisyon acente/kaynak; drop farklı yere teslim; sonra-öde peşin-olmayan oran.
    public decimal? Provizyon { get; set; }
    public decimal? Depozito { get; set; }
    public decimal? KomisyonOran { get; set; }   // %
    public decimal? KomisyonTutar { get; set; }
    public decimal? DropUcreti { get; set; }
    public decimal? SonraOdeOran { get; set; }   // %

    public string? Aciklama { get; set; }

    // ---- Kira formu detay alanları (TürevRent parite, mega-form; TÜMÜ BİLGİ AMAÇLI — para hesabına girmez) ----
    /// <summary>Rezervasyon/satış kaynağı (ReservationSource master'dan seç-veya-yaz).</summary>
    public string? Kaynak { get; set; }

    /// <summary>Promosyon/kampanya kodu (FAZ 3.A5) — create'te fiyat kodlu kuralla çözüldüyse iz.</summary>
    public string? KampanyaKodu { get; set; }
    /// <summary>Uyarı açıklaması (sözleşme açılışında operatöre gösterilecek not).</summary>
    public string? UyariAciklama { get; set; }
    /// <summary>Özel fatura açıklaması (faturaya taşınacak serbest metin).</summary>
    public string? OzelFaturaAciklama { get; set; }
    /// <summary>Fatura listesinde gösterme bayrağı (liste filtresi ileride; şimdilik persist).</summary>
    public bool? FaturaListesindeGizle { get; set; }
    /// <summary>Uçuş no (havalimanı teslimlerinde).</summary>
    public string? UcusNo { get; set; }
    /// <summary>Provizyon (ön otorizasyon) referans no — bilgi; hold/capture yaşam döngüsü POS entegrasyonuyla.</summary>
    public string? ProvizyonNo { get; set; }
    public DateTimeOffset? ProvizyonTarih { get; set; }

    /// <summary>Manuel provizyon yaşam döngüsü (FAZ 4.1): Yok→Alindi→(Kapandi|IadeEdildi).
    /// POS'suz kayıt (IPosService çağrılmaz; kart alanları PCI gereği kalıcı disabled);
    /// deftere YAZMAZ — bilgi/iz (gerçek tahsilat ayrı akış).</summary>
    public ProvizyonDurum ProvizyonDurum { get; set; } = ProvizyonDurum.Yok;
    public DateTimeOffset? ProvizyonKapamaTarih { get; set; }
    /// <summary>Kapamada çekilen tutar (iade edilmişse 0; bilgi).</summary>
    public decimal? ProvizyonKapamaTutar { get; set; }
    public string? OnayKodu { get; set; }
    public string? FirmaKodu { get; set; }
    public string? ProjeAdi { get; set; }
    /// <summary>Özel kod (CustomCode master'dan seç-veya-yaz).</summary>
    public string? OzelKod { get; set; }
    public string? TalepTuru { get; set; }
    public string? GeldigiBirim { get; set; }
    public string? KefilBilgisi { get; set; }
    public string? AssistFirma { get; set; }
    public string? OzelSoforBilgisi { get; set; }
    /// <summary>Ek koşullar / sözleşme özel şartları (yazdırılan sözleşmeye eklenebilir).</summary>
    public string? EkKosullar { get; set; }
    /// <summary>Sözleşme PDF'inde basılacak belge şablonu (BelgeSablon; KiraSozlesmesi türü). Null →
    /// tenant varsayılan şablonu, o da yoksa koddaki sabit metin. Bilgi/sunum alanı — deftere yansımaz.</summary>
    public Guid? BelgeSablonId { get; set; }
    /// <summary>Kira-seviyesi opsiyon (FAZ 4.4; bilgi — deftere yansımaz): net tutar + gün.</summary>
    public decimal? OpsiyonNet { get; set; }
    public int? OpsiyonGun { get; set; }
    /// <summary>Risk limiti aşımında Yönetici onayı (FAZ 4.4) — guard kira açılış GİRİŞİNDE.</summary>
    public bool RiskOnay { get; set; }
    /// <summary>Manuel girilen Findeks puanı (entegrasyon yok; operatör görür).</summary>
    public int? ManuelFindexPuan { get; set; }
    /// <summary>KABİS çıkış/dönüş bildirimi yapıldı işaretleri (entegrasyon stub; operatör takibi).</summary>
    public bool? KabisCikis { get; set; }
    public bool? KabisDonus { get; set; }
    /// <summary>Otomatik uzat bayrağı (davranış ileride; şimdilik persist).</summary>
    public bool? OtomatikUzat { get; set; }

    // ---- Aksesuar durum tespiti (çıkışta "önce" / dönüşte "sonra"; hasar-eksik kanıtı).
    // bool? BİLİNÇLİ: null = hiç işaretlenmedi (eski kayıtlar/atlanan tespit), false = "yok" beyanı. ----
    public bool? AksYedekAnahtarCikis { get; set; }
    public bool? AksYedekAnahtarDonus { get; set; }
    public bool? AksStepneCikis { get; set; }
    public bool? AksStepneDonus { get; set; }
    public bool? AksZincirCikis { get; set; }
    public bool? AksZincirDonus { get; set; }
    public bool? AksIlkYardimCikis { get; set; }
    public bool? AksIlkYardimDonus { get; set; }
    /// <summary>4 lastik durumu serbest metin (ör. "ÖnSol:iyi ÖnSağ:iyi ArkaSol:az ArkaSağ:iyi").</summary>
    public string? AksLastikCikis { get; set; }
    public string? AksLastikDonus { get; set; }

    // ---- TürevRent parite (additive metadata; mevcut para hesabını etkilemez) ----
    public string? KiralamaTuru { get; set; }   // Kısa/Uzun/İkame/Aylık
    public string? FaturalamaTipi { get; set; } // Müşteri Ödemeli/Full Credit/Extralar Müşteriye/Drop Dahil/Diğer
    public string? FiyatTuru { get; set; }       // Otomatik/KDV Dahil Günlük/Günlük/KDV Dahil Toplam/Toplam
    public string? Doviz { get; set; }           // TL/EURO/USD

    /// <summary>Kira dövizinin OLUŞTURMA anındaki TL kuru (TRY=1) — YALNIZ RAPORLAMA (CRM ciro/segment TL-baz;
    /// denetim O5: FX+TL ciroları düz toplanıyordu). Defter/fatura BUNU KULLANMAZ (fatura kuru fatura anında
    /// yakalanır). Retroaktif değişmez (snapshot).</summary>
    public decimal KurSnapshot { get; set; } = 1m;

    /// <summary>Dönemsel faturalama JOB kapısı (FAZ 4.2-B4): true + tenant ayarı açık + Kirada +
    /// DonemBit geçmiş → job dönem faturasını otomatik keser. Manuel kesim bu bayraktan bağımsız.</summary>
    public bool DonemselFaturalama { get; set; }

    /// <summary>NET fiyat modunda (Günlük/Toplam) gross-up ANINDA kullanılan KDV oranı (FAZ 3.A6).
    /// Fatura ayrıştırması ve net-mod guard'ı BU orandan okur — tenant varsayılanı fiyatlama ile
    /// fatura arasında değişse bile matrah operatör niyetinden sapmaz. Null = eski kira (0.20
    /// gross-up'lıydı) veya brüt mod (snapshot gerekmez).</summary>
    public decimal? KdvOranSnapshot { get; set; }

    /// <summary>Kira-seviyesi ÖZEL KDV oranı (kesir 0..1) — fatura kesiminde varsayılan oran
    /// (FAZ 1.4; zincir: kdvRate ?? OzelKdvOran ?? 0.20). Sözleşme Tutar'ını DEĞİŞTİRMEZ; NET fiyat
    /// modlarında fatura kesimi mevcut guard'la reddedilir (matrah niyeti korunur).</summary>
    public decimal? OzelKdvOran { get; set; }

    /// <summary>Kira-seviyesi damga vergisi (bilgi) — fatura kesiminde InvoiceTaxInfo.DamgaVergisi
    /// varsayılanı (FAZ 1.4). Deftere/bakiyeye yansımaz (fatura üstünde bilgi kolonu).</summary>
    public decimal? DamgaVergisi { get; set; }

    /// <summary>Kira ek hizmet kalemleri (bebek koltuğu, GPS…). GenelToplam'a brüt olarak girer.</summary>
    public List<RentalAddOn> EkHizmetler { get; set; } = [];

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
