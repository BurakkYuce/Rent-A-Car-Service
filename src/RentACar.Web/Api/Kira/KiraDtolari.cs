using RentACar.Application.Bookings;
using RentACar.Application.FaturaDonemleri;
using RentACar.Domain.Entities;

namespace RentACar.Web.Api.Kira;

// ---------------------------------------------------------------- istek gövdeleri

/// <summary>
/// Yeni kira (<c>POST /kiralar</c>). Alan kümesi Blazor <c>/kiralar/create</c> ucunun <see cref="BookingInput"/>'a
/// taşıdığı alanlarla BİREBİR (whitelist): <c>KmLimit</c>/<c>FazlaKmUcret</c>/<c>YakitBirimUcret</c> ve OTA bedelleri
/// Blazor create'te de YOK (açık kirada güncellemeyle girilir). Müşteri ZORUNLU: yeni müşteri önce
/// <c>POST /kiralar/musteri</c> ile açılır (kira gövdesinde PII taşınmaz).
/// </summary>
public sealed record KiraOlusturIstegi
{
    public required Guid MusteriId { get; init; }
    public required Guid VehicleId { get; init; }
    public required DateTimeOffset BasTar { get; init; }
    public required DateTimeOffset BitTar { get; init; }
    /// <summary>Boş/0 → tarife (fiyat motoru); "Otomatik" fiyat türünde yok sayılır.</summary>
    public decimal? GunlukUcret { get; init; }
    public Guid? IkinciSurucuId { get; init; }
    public string? CikisOfisi { get; init; }
    public string? DonusOfisi { get; init; }
    public string? Aciklama { get; init; }
    // ödeme derinliği (bilgi; deftere yansımaz — DropUcreti hariç: sistem ücret satırı)
    public decimal? Provizyon { get; init; }
    public decimal? Depozito { get; init; }
    public decimal? KomisyonOran { get; init; }
    public decimal? KomisyonTutar { get; init; }
    public decimal? DropUcreti { get; init; }
    public decimal? SonraOdeOran { get; init; }
    public string? KiralamaTuru { get; init; }
    public bool DonemselFaturalama { get; init; }
    public string? FaturalamaTipi { get; init; }
    public string? FiyatTuru { get; init; }
    public string? Doviz { get; init; }
    // kira detay alanları
    public string? Kaynak { get; init; }
    public string? KampanyaKodu { get; init; }
    public string? UyariAciklama { get; init; }
    public string? OzelFaturaAciklama { get; init; }
    public bool? FaturaListesindeGizle { get; init; }
    public string? UcusNo { get; init; }
    public string? ProvizyonNo { get; init; }
    public DateTimeOffset? ProvizyonTarih { get; init; }
    public string? OnayKodu { get; init; }
    public string? FirmaKodu { get; init; }
    public string? ProjeAdi { get; init; }
    public string? OzelKod { get; init; }
    public decimal? OzelKdvOran { get; init; }
    public decimal? DamgaVergisi { get; init; }
    public string? TalepTuru { get; init; }
    public string? GeldigiBirim { get; init; }
    public string? KefilBilgisi { get; init; }
    public string? AssistFirma { get; init; }
    public string? OzelSoforBilgisi { get; init; }
    public string? EkKosullar { get; init; }
    public Guid? BelgeSablonId { get; init; }
    public int? ManuelFindexPuan { get; init; }
    public decimal? OpsiyonNet { get; init; }
    public int? OpsiyonGun { get; init; }
    /// <summary>Risk limiti aşımında onay; rol doğrulaması SERVİSTE (Yönetici/Admin).</summary>
    public bool RiskOnay { get; init; }
    public bool? KabisCikis { get; init; }
    public bool? KabisDonus { get; init; }
    public bool? OtomatikUzat { get; init; }
    public bool? AksYedekAnahtarCikis { get; init; }
    public bool? AksStepneCikis { get; init; }
    public bool? AksZincirCikis { get; init; }
    public bool? AksIlkYardimCikis { get; init; }
    public string? AksLastikCikis { get; init; }
    public string? OdemeSekli { get; init; }
    public string? IkinciSurucuSerbestAd { get; init; }
    public string? IkinciSurucuSerbestSoyad { get; init; }
    public string? IkinciSurucuSerbestTel { get; init; }
    public string? IkinciSurucuSerbestEhliyetSinifi { get; init; }
    /// <summary>Ek hizmet matrisi: fiyat DAİMA tanım snapshot'ı (serbest fiyat yok — önizleme == kayıt).</summary>
    public IReadOnlyList<KiraEkHizmetSecimi>? EkHizmetler { get; init; }

    /// <summary>Blazor <c>BookingEndpoints</c> create eşlemesinin (ApplyOdemeDerinlik + ApplyKiraDetay) aynısı.</summary>
    public BookingInput ToInput() => new()
    {
        MusteriId = MusteriId, VehicleId = VehicleId, BasTar = BasTar, BitTar = BitTar,
        IkinciSurucuId = IkinciSurucuId,
        GunlukUcret = GunlukUcret ?? 0m, CikisOfisi = Nz(CikisOfisi), DonusOfisi = Nz(DonusOfisi), Aciklama = Nz(Aciklama),
        Provizyon = Provizyon, Depozito = Depozito, KomisyonOran = KomisyonOran, KomisyonTutar = KomisyonTutar,
        DropUcreti = DropUcreti, SonraOdeOran = SonraOdeOran,
        KiralamaTuru = Nz(KiralamaTuru), DonemselFaturalama = DonemselFaturalama, FaturalamaTipi = Nz(FaturalamaTipi),
        FiyatTuru = Nz(FiyatTuru), Doviz = Nz(Doviz),
        Kaynak = Nz(Kaynak), KampanyaKodu = Nz(KampanyaKodu), UyariAciklama = Nz(UyariAciklama),
        OzelFaturaAciklama = Nz(OzelFaturaAciklama), FaturaListesindeGizle = FaturaListesindeGizle,
        UcusNo = Nz(UcusNo), ProvizyonNo = Nz(ProvizyonNo), ProvizyonTarih = ProvizyonTarih,
        OnayKodu = Nz(OnayKodu), FirmaKodu = Nz(FirmaKodu), ProjeAdi = Nz(ProjeAdi), OzelKod = Nz(OzelKod),
        OzelKdvOran = OzelKdvOran, DamgaVergisi = DamgaVergisi,
        TalepTuru = Nz(TalepTuru), GeldigiBirim = Nz(GeldigiBirim), KefilBilgisi = Nz(KefilBilgisi),
        AssistFirma = Nz(AssistFirma), OzelSoforBilgisi = Nz(OzelSoforBilgisi), EkKosullar = Nz(EkKosullar),
        BelgeSablonId = BelgeSablonId, ManuelFindexPuan = ManuelFindexPuan,
        OpsiyonNet = OpsiyonNet, OpsiyonGun = OpsiyonGun, RiskOnay = RiskOnay,
        KabisCikis = KabisCikis, KabisDonus = KabisDonus, OtomatikUzat = OtomatikUzat,
        AksYedekAnahtarCikis = AksYedekAnahtarCikis, AksStepneCikis = AksStepneCikis,
        AksZincirCikis = AksZincirCikis, AksIlkYardimCikis = AksIlkYardimCikis, AksLastikCikis = Nz(AksLastikCikis),
        OdemeSekli = Nz(OdemeSekli),
        IkinciSurucuSerbestAd = Nz(IkinciSurucuSerbestAd), IkinciSurucuSerbestSoyad = Nz(IkinciSurucuSerbestSoyad),
        IkinciSurucuSerbestTel = Nz(IkinciSurucuSerbestTel), IkinciSurucuSerbestEhliyetSinifi = Nz(IkinciSurucuSerbestEhliyetSinifi),
    };

    internal static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>Ek hizmet seçimi (tanım + miktar). Miktar ≤ 0 → 1 (Blazor matrisiyle aynı).</summary>
public sealed record KiraEkHizmetSecimi(Guid TanimId, decimal? Miktar);

// F4.1 adversarial L2: işlem gövdelerindeki zorunlu alanlar NULLABLE bildirilir ve UÇTA doğrulanır — JSON'da
// eksik alan sessizce 0'a (yakıt 0 → sahte eksik-yakıt bedeli) düşmesin, 400 errors[alan] dönsün. (Bağlama
// katmanındaki `required` eksik alanı da reddeder ama alan adı taşımaz; SPA hatayı alanın altında gösteremez.)

/// <summary>Teslim: ikisi de ZORUNLU (eksik → 400 <c>errors[alan]</c>). Yakıt 0–12.</summary>
public sealed record TeslimIstegi(int? CikisKm = null, int? CikisYakit = null);

/// <summary>Dönüş: km, yakıt ve gerçek dönüş ZORUNLU (eksik → 400 <c>errors[alan]</c>).</summary>
public sealed record DonusIstegi(
    int? DonusKm = null, int? DonusYakit = null, DateTimeOffset? GercekDonus = null,
    int? KmHediye = null, string? BitisSebebi = null, Guid? TeslimAlanPersonelId = null);

/// <summary>Uzatma: yeni bitiş ZORUNLU (eksik → 400 <c>errors[yeniBitTar]</c>).</summary>
public sealed record UzatIstegi(DateTimeOffset? YeniBitTar = null);

/// <summary>Provizyon kapama: <c>Iade=true</c> → serbest bırak (kapama tutarı 0); tutar boş → bloke tutarın tamamı.</summary>
public sealed record ProvizyonKapatIstegi(decimal? KapamaTutar = null, bool Iade = false);

/// <summary>Ek hizmet: tanım ve miktar ZORUNLU (eksik → 400 <c>errors[alan]</c>).</summary>
public sealed record EkHizmetEkleIstegi(Guid? EkHizmetTanimId = null, decimal? Miktar = null);

/// <summary>Kira formundan hızlı müşteri (Blazor <c>/kiralar/musteri-olustur</c>). PII CustomerService'te
/// şifrelenir; yanıt YALNIZ kimlik + etiket döner (girilen TC/ehliyet/telefon geri yansıtılmaz).</summary>
public sealed record MusteriHizliIstegi(
    string? Ad = null, string? Soyad = null, string? Unvan = null, string? TcKimlik = null,
    string? CepTel = null, string? Email = null, string? Il = null, string? Ilce = null,
    DateTimeOffset? DogumTarihi = null, string? EhliyetNo = null, string? EhliyetSinifi = null,
    DateTimeOffset? EhliyetTarihi = null, string? EhliyetYeri = null);

// ---------------------------------------------------------------- yanıtlar

/// <summary>Oluşturma sonucu. <c>Uyari</c>: kira AÇILDI ama bir ek hizmet eklenemedi (Blazor'daki
/// "Kira açıldı ancak ek hizmet eklenemedi" bandı) — kira geri alınmaz, kalem detaydan eklenir.</summary>
public sealed record KiraOlusturYaniti(Guid Id, string SozlesmeNo, string? Uyari);

public sealed record MusteriHizliYaniti(Guid Id, string Etiket);

/// <summary>
/// Satır başına "Tahsil Et" verisi (Blazor pano/liste hızlı tahsilat formunun gizli alanları).
/// <c>Anahtar</c> = <c>TahsilatAnahtar.Uret(kira, bakiye, işlem sayısı)</c> — SUNUCUDA, ekran yüklenirken
/// üretilir; SPA tahsilat isteğinde GERİ GÖNDERİR (başlık bunu ezemez). Yalnız FinanceWrite'lı oturuma ve
/// yalnız tahsil edilebilir satıra (bakiye &gt; 0, iptal değil) dolar; aksi null.
/// </summary>
public sealed record TahsilatBilgisi(Guid Anahtar, Guid CariId, Guid RentalId, string Doviz, decimal VarsayilanTutar);

public sealed record KiraListeSatiri(
    Guid Id, string SozlesmeNo, Guid MusteriId, string MusteriAd, string Plaka,
    DateTimeOffset BasTar, DateTimeOffset BitTar, DateTimeOffset? VadeTar, int Gun,
    int? HediyeGun, int? FaturalananGun, decimal Tutar, decimal Bakiye, string? Doviz,
    string? Kaynak, string? CikisOfisi, string? DonusOfisi,
    decimal? Provizyon, decimal? Depozito, decimal? KomisyonOran, decimal? KomisyonTutar,
    string? OnayKodu, string? ProjeAdi, string? AssistFirma, string? OzelSoforBilgisi,
    string Durum, bool Faturali, TahsilatBilgisi? Tahsilat);

/// <summary>Liste alt satırı (Blazor "N sözleşme • N kirada • N faturasız"), aynı filtrelerle.</summary>
public sealed record KiraListeOzeti(int Toplam, int Kirada, int Faturasiz);

public sealed record KiraFiltreSecenekleri(IReadOnlyList<string> Sahipler, IReadOnlyList<string> Gruplar);

public sealed record KiraTarafDto(Guid Id, string Ad);

public sealed record KiraAracDto(
    Guid Id, string Plaka, string? Marka, string? Tip, int? ModelYili, string? Vites, string? Yakit,
    string? Grup, string? Segment, int Km, string? Sube, string? Konum);

public sealed record EkHizmetKalemiDto(
    Guid Id, Guid EkHizmetTanimId, string Ad, decimal Miktar, decimal BirimNetFiyat, decimal KdvOrani,
    decimal NetTutar, decimal KdvTutar, decimal Toplam);

/// <summary>Dövizli kirada güncel kurla TL karşılığı (BİLGİ — fatura kuru kesim anında sabitlenir). Kur yoksa null.</summary>
public sealed record KiraDovizBilgisi(string Kod, decimal? GuncelKur, decimal? GenelToplamTl);

/// <summary>Aktif paylaşım linki. <c>Yol</c> göreli (<c>/sozlesme/{token}</c>) — mutlak adresi SPA kendi
/// kökünden kurar (Host başlığına güvenilmez).</summary>
public sealed record KiraPaylasimLinki(
    string Yol, int ErisimSayisi, DateTimeOffset? SonErisimUtc, DateTimeOffset OlusturmaUtc,
    DateTimeOffset AnlikGoruntuUtc, bool Bayat);

/// <summary>Paylaşım barı (yalnız OperationsWrite): aktif link + ön-doldurma (müşterinin kayıtlı GSM/e-postası).</summary>
public sealed record KiraPaylasimBari(KiraPaylasimLinki? Link, string? MusteriTel, string? MusteriEmail, string Konu);

/// <summary>Oturumun bu ekrandaki etkin izinleri (düğme durumları; asıl kapı sunucuda).</summary>
public sealed record KiraYetkileri(bool Operasyon, bool Silme, bool Finans);

/// <summary>
/// Kira formu modeli (<c>GET /kiralar/{id}</c>). Alt kayıtlar (fatura, ceza+HGS, dönem planı, dış hizmet,
/// kaynak rezervasyon, karne özeti) ayrı uçlardadır — her biri üst kaydın şube kapsamından geçer.
/// <para><c>Tahsilat</c> (F4.4): sabit paneldeki tahsilat formunun deterministik anahtarı — liste/pano satırıyla
/// AYNI üretim (<c>TahsilatAnahtar.Uret(kira, bakiye, işlem sayısı)</c>; Blazor pano/liste ve SPA aynı anahtara
/// düşer). Yalnız FinanceWrite'lı oturuma ve iptal olmayan kiraya dolar; liste satırından farklı olarak bakiye
/// ≤ 0'da da dolar (Blazor sabit paneli fazla/ön tahsilata da açıktır; <c>VarsayilanTutar</c> o zaman ≤ 0 —
/// SPA ön-doldurmaz). Kira her yüklendiğinde güncel durumun anahtarıdır; bayat anahtar
/// <c>POST finans/tahsilat</c>'ta 409 <c>mukerrer</c> alır (sunucu yeniden hesaplar).</para>
/// </summary>
public sealed record KiraDetayYaniti(
    KiraSozlesmesiDto Kira,
    KiraTarafDto Musteri,
    KiraTarafDto? IkinciSurucu,
    KiraAracDto? Arac,
    string? IslemSubeAdi,
    string? TeslimAlanPersonelAd,
    string? TeslimEdenPersonelAd,
    IReadOnlyList<EkHizmetKalemiDto> EkHizmetler,
    KiraDovizBilgisi? Doviz,
    KiraPaylasimBari? Paylasim,
    KiraYetkileri Yetkiler,
    TahsilatBilgisi? Tahsilat);

public sealed record MusaitAracDto(
    Guid Id, string Plaka, string? Marka, string? Tip, int? ModelYili, string? Vites, string? Yakit,
    string? Grup, string? Segment, int Km, string? Sube, string? Konum);

/// <summary>Yeni kira formu varsayılanları (FAZ-82 tenant ayarı + sabit seçenek listeleri). YALNIZ ön-doldurma.</summary>
public sealed record KiraFormVarsayilanlari(
    int CikisYakit, string? FiyatTuru, IReadOnlyList<string> FiyatTurleri, IReadOnlyList<string> KiralamaTurleri,
    IReadOnlyList<string> FaturalamaTipleri, IReadOnlyList<string> Dovizler, IReadOnlyList<string> OdemeSekilleri,
    DateTimeOffset BasTarEnGec);

public sealed record KiraFaturaDto(
    Guid Id, string No, DateTimeOffset Tarih, decimal NetTutar, decimal KdvTutar, decimal GenelToplam,
    string Currency, string Durum, string Tur);

public sealed record KiraCezaDto(Guid Id, string No, string CezaTuru, decimal Tutar, decimal Kalan, string Durum, DateTimeOffset TebligTarihi);

public sealed record HgsGecisDto(DateTimeOffset Zaman, string Gecis, decimal Tutar);

public sealed record KiraCezaHgsYaniti(IReadOnlyList<KiraCezaDto> Cezalar, IReadOnlyList<HgsGecisDto> HgsGecisleri);

public sealed record KiraDonemDto(
    int DonemSira, DateTimeOffset DonemBas, DateTimeOffset DonemBit, string Durum, decimal Tahakkuk,
    Guid? InvoiceId, decimal? KesilenTutar)
{
    public static KiraDonemDto From(FaturaDonemOnizleme d)
        => new(d.DonemSira, d.DonemBas, d.DonemBit, d.Durum.ToString(), d.Tahakkuk, d.InvoiceId, d.KesilenTutar);
}

public sealed record KiraDisHizmetDto(
    Guid Id, string No, string AlinanHizmet, string? HizmetAlinanFirma, decimal HizmetBedeli, string Currency,
    decimal TedarikciKomisyonOran, string Durum, DateTimeOffset Tarih)
{
    public static KiraDisHizmetDto From(DisHizmetAlimi d)
        => new(d.Id, d.No, d.AlinanHizmet, d.HizmetAlinanFirma, d.HizmetBedeli, d.Currency,
            d.TedarikciKomisyonOran, d.Durum.ToString(), d.Tarih);
}

/// <summary>Kiranın kaynak rezervasyonu (OTA kutuları). Manuel kirada <c>Rezervasyon = null</c>.</summary>
public sealed record KaynakRezervasyonYaniti(KaynakRezervasyonDto? Rezervasyon);

public sealed record KaynakRezervasyonDto(
    Guid Id, string ReservationNo, string Durum, DateTimeOffset BasTar, DateTimeOffset BitTar,
    string? Kaynak, string? TalepTuru,
    decimal? OtaKiraBedeli, decimal? OtaDropBedeli, decimal? OtaBebekKoltugu, decimal? OtaNavigasyon,
    decimal? OtaLcf, decimal? OtaCdw, decimal? OtaScdw, decimal? OtaEkSurucu);

/// <summary>Karne özeti (kira formundaki doluluk kutusu): aracın ÖMÜR-BOYU doluluk yüzdesi.</summary>
public sealed record KarneOzetiDto(Guid VehicleId, decimal? DolulukYuzde);
