namespace RentACar.Web.Api.Sistem;

// ---------------------------------------------------------------- ilanlar

/// <summary>İlan listesi satırı: yayına girmesi için eksikler (<c>Eksikler</c>) ve özellik bayatlığıyla.</summary>
public sealed record ListingRowDto(
    Guid Id, string Baslik, string Durum, bool Yayinda, int AracSayisi, int Adet,
    decimal GunlukFiyat, bool KdvDahil, IReadOnlyList<string> Eksikler, bool OzellikBayat);

/// <summary>Web sitesi özeti: henüz ilana bağlanmamış (kapsamdaki) araç sayısı.</summary>
public sealed record WebsiteSummaryDto(int IlansizAracSayisi, int IlanSayisi, int YayindakiIlanSayisi);

/// <summary>Sihirbaz adım-1 havuzu: "aynı araç" kümesi.</summary>
public sealed record VehicleClusterDto(string Imza, string Baslik, string? YilAralik, IReadOnlyList<ClusterVehicleDto> Araclar);

public sealed record ClusterVehicleDto(Guid Id, string Plaka, string? Sube);

/// <summary>
/// Adım-1 gövdesi. <c>mod</c> = <c>beraber</c> (varsayılan; <c>imzalar</c> gönderilir — bir küme = bir ilan) ya da
/// <c>ayri</c> (<c>aracIdler</c> gönderilir — her araç ayrı ilan).
/// </summary>
public sealed record ListingCreateRequest(string? Mod, IReadOnlyList<string>? Imzalar, IReadOnlyList<Guid>? AracIdler);

public sealed record ListingCreatedDto(Guid Id);

public sealed record ListingFeatureDto(string Etiket, string Deger, bool Gorunur);

public sealed record ListingPhotoDto(Guid AracId, Guid FotoId, string Plaka, int Sira);

public sealed record ListingVehicleDto(Guid Id, string Plaka, string? Sube);

/// <summary>İlan detayı (fiyat + özellik adımları). <c>Ozellikler</c>: kayıtlı satırlar, yoksa araçlardan öneri.</summary>
public sealed record ListingDetailDto(
    Guid Id, string Baslik, string Slug, string Durum, decimal GunlukFiyat, decimal? HaftalikToplam, decimal? AylikToplam,
    bool KdvDahil, int KardesTaslakSayisi, IReadOnlyList<ListingVehicleDto> Araclar,
    IReadOnlyList<ListingFeatureDto> Ozellikler, IReadOnlyList<ListingPhotoDto> Fotograflar, string? Surum);

/// <summary>Adım-2 (fiyat) — tam değiştirme; <c>surum</c> ZORUNLU. Haftalık/aylık TOPLAM tutardır (günlük değil).</summary>
public sealed record ListingPriceRequest(decimal? GunlukFiyat, decimal? HaftalikToplam, decimal? AylikToplam, bool KdvDahil = true,
    string? Surum = null);

public sealed record ListingPriceResultDto(int KardesKopyalanan, ListingDetailDto Ilan);

/// <summary>Adım-3 (özellikler) — satırlar TAMAMEN değiştirilir; <c>surum</c> ZORUNLU.</summary>
public sealed record ListingFeaturesRequest(IReadOnlyList<ListingFeatureDto>? Satirlar, string? Surum = null);

/// <summary><c>Yayinda</c> = false: özellikler kaydedildi ama fotoğraf olmadığı için ilan taslakta kaldı.</summary>
public sealed record ListingFeaturesResultDto(bool Yayinda, ListingDetailDto Ilan);

/// <summary>Durum: <c>Yayinda</c> ya da <c>Pasif</c> (taslağa geri alınamaz).</summary>
public sealed record ListingStatusRequest(string? Durum);

// ---------------------------------------------------------------- site içeriği

public sealed record PageRowDto(Guid Id, string Slug, string Baslik, int Sira, bool Yayinda);

public sealed record PageDetailDto(Guid Id, string Slug, string Baslik, string Govde, string? MetaAciklama, int Sira, bool Yayinda,
    string? Surum);

/// <summary>Sayfa gövdesi DÜZ METİNDİR (HTML değil): site onu <c>IcerikMetni</c> bloklarına ayırıp kodlanmış basar.</summary>
public sealed record PageRequest(string? Baslik, string? Govde, string? Slug, string? MetaAciklama, int Sira = 0, bool Yayinda = false,
    string? Surum = null);

public sealed record PublishRequest(bool Yayinda);

public sealed record FaqDto(Guid Id, string Soru, string Cevap, int Sira, bool Yayinda, string? Surum);

public sealed record FaqRequest(string? Soru, string? Cevap, int Sira = 0, bool Yayinda = false, string? Surum = null);

// ---------------------------------------------------------------- blog

public sealed record BlogRowDto(Guid Id, string Baslik, string Slug, string? Ozet, string Durum, DateTimeOffset? YayinTarihi,
    bool KapakVar, string? AltBaslik, bool AramaDisi);

/// <summary>Blog yazısı. <c>Icerik</c> DÜZ METİNDİR (HTML değil) — bkz. <see cref="BlogPreviewDto"/>.</summary>
public sealed record BlogDetailDto(
    Guid Id, string Baslik, string Slug, string? Ozet, string Icerik, string Durum, DateTimeOffset? YayinTarihi, bool KapakVar,
    string? AltBaslik, string? SeoBaslik, string? MetaAciklama, string? AnahtarKelimeler, string? Yazar, string? KapakAlt,
    bool AramaDisi, bool SlugDondu, string? Surum);

/// <summary>POST/PUT gövdesi; PUT'ta <c>surum</c> ZORUNLU. <c>durum</c>: <c>Taslak</c> | <c>Yayinda</c>.</summary>
public sealed record BlogRequest(
    string? Baslik, string? Slug, string? Ozet, string? Icerik, string? Durum,
    string? AltBaslik, string? SeoBaslik, string? MetaAciklama, string? AnahtarKelimeler, string? Yazar, string? KapakAlt,
    bool AramaDisi = false, string? Surum = null);

/// <summary>Gövde bloğu: <c>tur</c> = <c>Paragraf</c> | <c>Baslik2</c> | <c>Baslik3</c>; <c>metin</c> DÜZ metin
/// (istemci kodlayarak basar — HTML olarak yorumlanmaz).</summary>
public sealed record ContentBlockDto(string Tur, string Metin);

/// <summary>Taslak önizleme: sitedeki ayrıştırma kuralıyla (IcerikMetni) üretilmiş bloklar + arama künyesi.</summary>
public sealed record BlogPreviewDto(
    Guid Id, string Durum, string Adres, string Baslik, string? AltBaslik, string AramaBasligi, string? AramaAciklamasi,
    IReadOnlyList<string> AnahtarKelimeler, bool AramaDisi, IReadOnlyList<ContentBlockDto> Bloklar);

// ---------------------------------------------------------------- gelen talepler

public sealed record BookingRequestRowDto(
    Guid Id, string AdSoyad, string Telefon, string? Email, Guid? IlanId, string? IlanBaslik, string? AracGrupKod,
    DateTimeOffset BasTar, DateTimeOffset BitTar, string? Sube, string? Not, decimal? GosterilenGunlukUcret,
    bool? GosterilenKdvDahil, string Durum, string DurumEtiket, bool Aktif, IReadOnlyList<string> Ilerlemeler,
    Guid? DonusenRezervasyonId, Guid? AtananKullaniciId, string? AtananAd, DateTimeOffset OlusturmaTarihi,
    int NotSayisi, int BekleyenGun);

public sealed record BookingRequestSummaryDto(int Yeni, int? EnEskiGun);

public sealed record BookingRequestPageDto(
    IReadOnlyList<BookingRequestRowDto> Kayitlar, int Toplam, int SayfaNo, int Boyut, BookingRequestSummaryDto Ozet);

public sealed record BookingRequestNoteDto(Guid Id, string Metin, string? Kullanici, DateTimeOffset ZamanUtc);

/// <summary>Dönüştürme adayı: talep tarihlerinde müsait (ve kullanıcının şube kapsamındaki) araç.
/// <c>IlanAraci</c>: talebin geldiği ilanın üyesi (önerilen).</summary>
public sealed record CandidateVehicleDto(Guid Id, string Plaka, string? Marka, string? Tip, string? Grup, string? Sube, bool IlanAraci);

public sealed record BookingRequestStatusRequest(string? Durum);

public sealed record BookingRequestClaimRequest(bool Ustlen);

public sealed record BookingRequestNoteRequest(string? Metin);

public sealed record BookingRequestConvertRequest(Guid? AracId);

public sealed record BookingRequestConvertedDto(Guid RezervasyonId);
