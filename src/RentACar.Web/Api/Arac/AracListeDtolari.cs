using RentACar.Application.Common;

namespace RentACar.Web.Api.Arac;

/// <summary>
/// Araç listesi satırı — Blazor <c>VehicleList</c> 49 sütun paritesi (48 veri sütunu + işlem). Enum'lar ADLA.
/// Son sekiz alan başka tablolardan CANLI çözülür (yalnız görünen sayfa için).
/// </summary>
public sealed record AracListeSatiri(
    Guid Id, string Plaka, string? Marka, string? Tip, string? DetayTipi, string? Grup, string? Segment, string? Sipp,
    int? ModelYili, string? Renk, string? Vites, string? Yakit, int Km, string? Sube, string Durum,
    string? FiloDurum, string? SasiNo, string? MotorNo, int? MotorGucu, string? AracSahibi,
    string? HgsNo, string? OgsNo, string? KasaTipi, int? SonBakimKm,
    decimal? AlimBedeli, DateTimeOffset? FiloGirisTarih, DateTimeOffset? FiloCikisTarih, DateTimeOffset? TescilTarihi,
    string? AlimYapilanFirma, string? RuhsatNo, decimal? IkinciElDeger,
    decimal? TsbKaskoDegeri, bool YedekAnahtar, bool KarLastigi, string? LastikDurumu,
    bool ZIzni, string? SonDurum, string? Konum, string? TakipNo, string? TeypKodu, string? Aciklama,
    string? AktifKiraSozlesmeNo, bool AcikServis, bool AcikBaf, bool SatisVar,
    DateTimeOffset? KaskoBitis, decimal? KaskoPrim, string? KrediKurulusu, DateTimeOffset? KrediSonTarih);

/// <summary>Özet şerit (kapsamdaki TÜM filo): toplam, müsait, serviste, doluluk % (tam sayı, Blazor ile aynı yuvarlama).</summary>
public sealed record AracOzeti(int Toplam, int Musait, int Serviste, int Doluluk);

/// <summary>"Modele göre grupla" görünümü (aynı filtre, ilk 200 araç).</summary>
public sealed record AracModelGrubu(
    string Etiket, string? Grup, string YilAralik, int Toplam, int Musait, int Kirada, int Serviste,
    IReadOnlyList<AracModelGrubuAraci> Araclar);

public sealed record AracModelGrubuAraci(
    Guid Id, string Plaka, int? ModelYili, string? Renk, string? Vites, string? Yakit, int Km, string? Sube,
    string Durum, string? Sipp);

public sealed record AracModelGruplari(int ToplamArac, bool Kirpildi, IReadOnlyList<AracModelGrubu> Gruplar);

/// <summary>Detaylı araç listesi satırı (Blazor <c>VehicleDetayList</c> 49 sütun). Aktif kira müşterisi KVKK kuralıyla.</summary>
public sealed record AracDetayliSatir(
    Guid Id, string Plaka, string? Marka, string? Tip, string? Grup, string? Sube, string Durum,
    string? BelgeNo, string? RuhsatSahibi, string? SozNo, string? AraciAlan,
    string? AlimYapilanFirma, DateTimeOffset? AlimTarihi, decimal? AlimBedeli,
    bool AlisEuro, decimal? AlisEuroFiyat, decimal? SatisEuroFiyat,
    string? KrediBanka, DateTimeOffset? MuayeneBitis, DateTimeOffset? KaskoBitis, DateTimeOffset? TrafikBitis,
    string? AktifKiraMusteri, string? AktifKiraSozlesmeNo, DateTimeOffset? AktifKiraBitis,
    string? Kiralayan, int? KiraGun, decimal? KiraFiyat, DateTimeOffset? KiraBitTar, DateTimeOffset? KiraBekTar,
    int? SonTeslimKm, DateTimeOffset? SonTeslimTarihi, string? AssistanFirma, string? HgsFirma, int? DisKmLimit,
    string? TsbKodu, decimal? TsbKaskoDegeri, string? OdemeSekli,
    decimal? SatisHedefFiyat, DateTimeOffset? IhaleTarihi, string? IhaleFirmasi, DateTimeOffset? NoterSatisTarihi,
    DateTimeOffset? FiloGirisTarih, DateTimeOffset? FiloCikisTarih, string? PasifSebep, string? SonDurum,
    string? OzelKod1, string? OzelKod2, string? OzelKod3, string? OzelKod4, string? OzelKod5);

/// <summary>Araç Güncel Durum satırı (Blazor <c>FleetStatus</c>). Müşteri adı/telefonu <c>MusteriGorunumu</c> kuralıyla.</summary>
public sealed record AracDurumSatiri(
    Guid VehicleId, string Plaka, string? Marka, string? Tip, string? Grup, string? FiloDurum, string Durum,
    Guid? AktifKiraId, string? KiraSozlesmeNo, string? MusteriAd, string? MusteriTel, DateTimeOffset? KiraBitTar,
    int? KiraKalanGun, decimal? KiraBakiye, string? RezMusteriAd, DateTimeOffset? RezBasTar,
    string? AcikServisNo, string? ServisAtolye, string? AktifBafNo, string? BafPersonelAd, string? DosyaNo,
    string? PasifSebep, string? Konum, string? TakipNo, string? HgsNo, bool KarLastigi,
    bool WebRezKapat, bool OfisRezKapat, int Km, string? Sube,
    bool Kirada, bool Serviste, bool Bafta);

/// <summary>Araç durumu yanıtı: sayfa + FİLTREYE UYAN tüm satırların sayaçları (Blazor üst satırı).</summary>
public sealed record AracDurumYaniti(Sayfa<AracDurumSatiri> Liste, int Kirada, int Serviste, int Bafta);

/// <summary>Araç detayı (Blazor <c>VehicleDetail</c>): başlık + kira/servis/ceza/hasar geçmişi + km serisi.</summary>
public sealed record AracDetayDto(
    Guid Id, string Plaka, string? Marka, string? Grup, string? Sube, string Durum, int Km,
    IReadOnlyList<AracKiraOzeti> Kiralar, IReadOnlyList<AracServisOzeti> Servisler,
    IReadOnlyList<AracCezaOzeti> Cezalar, IReadOnlyList<AracHasarOzeti> Hasarlar,
    IReadOnlyList<AracKmKaydi> KmKayitlari);

public sealed record AracKiraOzeti(Guid Id, string SozlesmeNo, DateTimeOffset BasTar, DateTimeOffset BitTar, string Durum, decimal GenelToplam);
public sealed record AracServisOzeti(Guid Id, string No, string Tip, DateTimeOffset GirisTarihi, string Durum, decimal ToplamIscilik);
public sealed record AracCezaOzeti(Guid Id, string No, string? CezaTuru, DateTimeOffset TebligTarihi, decimal Tutar, string Durum);
public sealed record AracHasarOzeti(Guid Id, string No, DateTimeOffset AcilisTarihi, decimal? TahminiTutar, string Durum);
/// <summary>Km zaman serisi kaydı; <c>Kaynak</c>: <c>Donus</c> | <c>Servis</c> | <c>Manuel</c>.</summary>
public sealed record AracKmKaydi(DateTimeOffset Tarih, int Km, string Kaynak);

/// <summary>Fotoğraf künyesi; içerik <c>/araclar/{id}/fotograflar/{fotoId}</c> (ve <c>/kucuk</c>) uçlarından.</summary>
public sealed record AracFotoDto(Guid Id, int Sira, string ContentType);
