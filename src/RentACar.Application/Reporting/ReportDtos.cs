using RentACar.Application.Pricing;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Reporting;

/// <summary>
/// Düz defter satırı (repo'dan toplama için). Base = Amount×Rate (yerel para) repo'da hesaplanır.
/// </summary>
public sealed record LedgerRowDto(
    DateTimeOffset Tarih, LedgerAccountType AccountType, LedgerDirection Direction,
    string SourceType, string? Aciklama, decimal Base);

/// <summary>Defter satırı (yürüyen bakiyeli) — kasa/banka defteri görünümü.</summary>
public sealed record LedgerLineDto(
    DateTimeOffset Tarih, string SourceType, string? Aciklama,
    decimal Borc, decimal Alacak, decimal YuruyenBakiye);

/// <summary>Kasa & banka giriş/çıkış/bakiye özeti (yerel para, base).</summary>
public sealed record CashboxSummaryDto(
    decimal KasaGiris, decimal KasaCikis, decimal KasaBakiye,
    decimal BankaGiris, decimal BankaCikis, decimal BankaBakiye);

/// <summary>Cari defter satırı (bakiye/yaşlandırma için) — ad çözümlenmiş, base tutar.</summary>
public sealed record CariLedgerRowDto(
    Guid CariId, string Ad, LedgerDirection Direction, decimal Base, DateTimeOffset Tarih);

/// <summary>
/// Cari bakiye satırı. <paramref name="Bakiye"/> pozitif = müşteri borçlu (alacağımız).
///
/// <para>FAZ-62: net bakiyenin YANINA brüt <paramref name="ToplamBorc"/>/<paramref name="ToplamAlacak"/>
/// eklendi. Net hesabı DEĞİŞMEDİ (<c>Σ SignedBase</c>); ikisi ayrı görünüyor çünkü "1.000 borç −
/// 1.000 tahsilat" ile "hiç hareket yok" net'te aynı (0) görünüyordu. Kart bilgileri (telefon/mail/
/// banka/döviz…) filtre ve iletişim için taşınır — defter matematiğine GİRMEZ.</para>
/// </summary>
public sealed record CariBalanceDto(
    Guid CariId, string Ad, decimal Bakiye,
    decimal ToplamBorc = 0m, decimal ToplamAlacak = 0m,
    string? Telefon = null, string? Email = null, string? Banka = null,
    string? Doviz = null, string? OzelKod = null, string? Sinif = null,
    bool Kurumsal = false, bool Pasif = false);

/// <summary>Cari kart bilgileri (bakiye raporunun kolon/filtre ihtiyacı). Defter DEĞİL.</summary>
public sealed record CariKartDto(
    Guid CariId, string? Telefon, string? Email, string? Banka, string? Doviz,
    string? OzelKod, string? Sinif, bool Kurumsal, bool Pasif, string? VergiNo);

/// <summary>
/// Cari bakiye listesi filtresi. Tüm alanlar opsiyonel; boş filtre = mevcut davranış (tüm bakiyeli
/// cariler) — filtre eklenmiş olması eski çağrıları daraltmaz.
/// </summary>
public sealed class CariBakiyeFilter
{
    /// <summary>Ad / telefon / e-posta / vergi no içinde geçen metin (küçük-büyük harf duyarsız).</summary>
    public string? Ara { get; set; }
    /// <summary>Özel cari tipi (Yurtiçi/Yurtdışı/2.El/Grup İçi/Standart).</summary>
    public string? OzelKod { get; set; }
    /// <summary>Müşteri sınıfı/segmenti.</summary>
    public string? Sinif { get; set; }
    /// <summary>Cari varsayılan dövizi (TL/EURO/USD).</summary>
    public string? Doviz { get; set; }
    /// <summary><c>true</c> = yalnız kurumsal, <c>false</c> = yalnız bireysel, <c>null</c> = hepsi.</summary>
    public bool? Kurumsal { get; set; }
    /// <summary>"borclu" = yalnız bakiyesi pozitif, "alacakli" = yalnız negatif, boş = hepsi.</summary>
    public string? BakiyeTuru { get; set; }
    /// <summary>Mutlak bakiye alt sınırı (küçük bakiyeleri gizlemek için).</summary>
    public decimal? MinTutar { get; set; }
}

/// <summary>
/// Cari borç yaşlandırma (v1: BRÜT borç — tahsilat FIFO mahsubu yok). Borç (Debit) satırları
/// yaşa göre kovalanır. Toplam = kovaların toplamı (net bakiye DEĞİL).
/// </summary>
public sealed record AgingRowDto(
    Guid CariId, string Ad, decimal B0_30, decimal B31_60, decimal B61_90, decimal B90Plus, decimal Toplam);

/// <summary>SourceType kırılım kalemi (gelir/gider drill-down).</summary>
public sealed record GelirGiderKalemDto(string SourceType, decimal Tutar);

/// <summary>Dönem-sonu özet mizan satırı (PR-A): hesap-tipi bazında Σ Borç / Σ Alacak / net bakiye (base para).
/// Bakiye = Borç − Alacak (Debit +, Credit −); tüm satırların bakiye toplamı 0 olmalı (defter dengesi).</summary>
public sealed record MizanSatirDto(LedgerAccountType Tip, string Ad, decimal Borc, decimal Alacak, decimal Bakiye);

/// <summary>Filo durum dağılımı + aktif kira (operasyonel rapor).</summary>
public sealed record FleetUtilizationDto(
    int Toplam, int Musait, int Kirada, int Serviste, int Pasif, int Satildi, int AktifKira);

/// <summary>
/// Kira efektif aralığı (doluluk hesabı için ham satır): başlangıç + efektif bitiş
/// (gerçek dönüş varsa o, yoksa planlanan bitiş). İptal kiralar repo'da hariç tutulur.
/// </summary>
public sealed record DolulukKiraRowDto(DateTimeOffset Bas, DateTimeOffset Bit);

/// <summary>
/// Dönem doluluk özeti: araç-gün kapasitesi (AracSayisi×DonemGun) üzerinden kira-gün oranı.
/// KiraGun = Σ (kira efektif aralığı ∩ dönem) takvim-günü (kapsayıcı). Yüzde 2 hane yuvarlı.
/// </summary>
public sealed record DolulukDto(
    int AracSayisi, int DonemGun, int AracGun, int KiraGun, decimal DolulukYuzde);
/// Dönem tahsilat-fatura mutabakatı: kesilen fatura toplamı (İptal hariç, GenelToplam×Kur) vs
/// alınan tahsilat toplamı (ters kayıt hariç, Amount×Rate) + fark. Fark = FaturaToplam − TahsilatToplam
/// (pozitif = tahsil edilmemiş bakiye). Salt sayım/toplam, base para.
/// </summary>
public sealed record TahsilatFaturaDto(
    int FaturaAdet, decimal FaturaToplam, int TahsilatAdet, decimal TahsilatToplam, decimal Fark);

/// <summary>Tek tamamlanmış servis kaydı (ham) — araç plakası çözümlenmiş.</summary>
public sealed record ServiceCostRowDto(Guid VehicleId, string Plaka, ServisTipi Tip, decimal ToplamIscilik);

/// <summary>Araç+tip başına servis maliyet özeti (gruplanmış).</summary>
public sealed record ServiceCostSummaryDto(Guid VehicleId, string Plaka, ServisTipi Tip, decimal Toplam, int Adet);

/// <summary>Periyodik servis (KM-bazlı bakım uyarısı) satırı — roadmap H1. KalanKm = SonrakiBakimKm − GuncelKm.
/// İKİ kaynak (VehicleId bazında MIN(KalanKm) — çift satır yok): (1) servis kaydındaki elle hedef
/// (ServiceRecord.SonrakiBakimKm), (2) OTOMATİK: Vehicle.SonBakimKm + ServisTanim.BakimKm (AracTipi↔Vehicle.Tip,
/// case-insensitive). Hiçbir kaynağı olmayan araç SonrakiBakimKm=null "tanım yok" satırı olarak görünür
/// (sessiz gizleme yok). Kaynak: "Servis" | "Tanım" | null.</summary>
public sealed record PeriyodikServisRow(
    Guid VehicleId, string Plaka, int GuncelKm, int? SonrakiBakimKm, int? KalanKm, string? Kaynak = null);

/// <summary>Kira KM detay satırı — roadmap H1. KatedilenKm = DonusKm − CikisKm.</summary>
public sealed record KmDetayRow(
    Guid RentalId, string SozlesmeNo, string Plaka, int CikisKm, int DonusKm,
    int KatedilenKm, int KmLimit, int FazlaKm, decimal FazlaKmBedeli);

/// <summary>Rezervasyon kaynak özeti — roadmap H2. Kaynak başına adet/gün/ciro.</summary>
public sealed record RezervasyonKaynakRow(string Kaynak, int Adet, int ToplamGun, decimal ToplamCiro);

/// <summary>Fatura dönem satırı — roadmap H2. Vade/cari/tutar/durum.</summary>
public sealed record FaturaDonemRow(
    Guid InvoiceId, string No, DateTimeOffset Tarih, DateTimeOffset? VadeTarihi,
    string Cari, decimal GenelToplam, string Currency, decimal Kur, string Durum, bool IadeMi);

/// <summary>Araç durum-takip (gün kırılımı) satırı — roadmap H3. Bos = Toplam − Dolu − Bakim (≥0).</summary>
public sealed record AracDurumTakipRow(DateTimeOffset Gun, int ToplamArac, int Dolu, int Bakim, int Bos);

/// <summary>Müşteri CRM segment satırı — roadmap N3. Segment ciro eşiğiyle (VIP/Standart/Pasif).</summary>
public sealed record MusteriSegmentRow(Guid CariId, string Ad, int KiraSayisi, decimal ToplamCiro, DateTimeOffset? SonIslem, string Segment);

/// <summary>Personel çalışma satırı — roadmap N3. BAF (araç tahsis) sayısı.</summary>
public sealed record PersonelCalismaRow(Guid PersonelId, string Ad, int TahsisSayisi);

/// <summary>Dönem gelir-gider özeti + KDV + net kâr + kaynak kırılımı (base para).</summary>
public sealed record GelirGiderDto(
    decimal GelirToplam, decimal GiderToplam,
    decimal KdvTahsil, decimal KdvIndirilecek, decimal NetKar,
    IReadOnlyList<GelirGiderKalemDto> GelirKirilim,
    IReadOnlyList<GelirGiderKalemDto> GiderKirilim);

/// <summary>
/// Günlük faaliyet özeti: bir günün operasyonel sayaçları + tutarları. Yeni rezervasyon/kira
/// (oluşturma tarihi), çıkış (kira başlangıcı), dönüş (gerçek dönüş), tahsilat (adet+tutar, ters
/// kayıt hariç), fatura (adet+tutar, İptal hariç). Salt-okunur sayım/toplam.
/// </summary>
public sealed record GunlukFaaliyetDto(
    int YeniRezervasyon, int YeniKira, int Cikis, int Donus,
    int TahsilatAdet, decimal TahsilatTutar, int FaturaAdet, decimal FaturaTutar);
/// <summary>KDV listesi: bir fatura satırının base para (Kur uygulanmış) tutarları + fatura referansı.</summary>
public sealed record KdvLineRowDto(decimal Oran, decimal Net, decimal Kdv, decimal Brut, Guid InvoiceId);

/// <summary>KDV oranı bazında dönem kırılımı (Net/KDV/Brüt + o oranı içeren fatura adedi).</summary>
public sealed record KdvListesiRowDto(decimal Oran, decimal Net, decimal Kdv, decimal Brut, int FaturaAdet);

/// <summary>Dönem KDV listesi raporu: oran satırları + genel toplamlar + toplam fatura adedi.</summary>
public sealed record KdvListesiDto(
    IReadOnlyList<KdvListesiRowDto> Satirlar,
    decimal ToplamNet, decimal ToplamKdv, decimal ToplamBrut, int FaturaAdet);
/// <summary>Satılan bir kira ek hizmet kalemi (ham): ad + miktar + base para tutarları + kira referansı.</summary>
public sealed record EkHizmetSalesRowDto(
    string Ad, decimal Miktar, decimal Net, decimal Kdv, decimal Brut, Guid RentalId);

/// <summary>Ek hizmet adına göre dönem satış özeti (toplam miktar/net/kdv/brüt + kaç kirada satıldı).</summary>
public sealed record EkHizmetRaporRowDto(
    string Ad, decimal ToplamMiktar, decimal Net, decimal Kdv, decimal Brut, int KiraAdet);

/// <summary>Dönem ek hizmet satış raporu: ek-hizmet satırları + genel toplamlar + toplam kira adedi.</summary>
public sealed record EkHizmetRaporDto(
    IReadOnlyList<EkHizmetRaporRowDto> Satirlar,
    decimal ToplamNet, decimal ToplamKdv, decimal ToplamBrut, int KiraAdet);

/// <summary>
/// Araç-bazlı kârlılık satırı (roadmap B2). Tutarlar DEFTERDEN: Gider = Σ Gider(Debit) AccountRef=araç;
/// Gelir = Σ Gelir(Credit) base, SourceId→Fatura→Kira→Araç ile atfedilir. VehicleId null = "(Atanmamış)"
/// (araca bağlanamayan genel gelir/gider). NetKar = Gelir − Gider.
/// </summary>
public sealed record KarlilikSatirDto(
    Guid? VehicleId, string Plaka, string? Sube, string? Grup, string? Segment, decimal Gelir, decimal Gider, decimal NetKar);

/// <summary>Dönem kârlılık raporu: araç/atanmamış satırları + genel toplamlar (defter Gelir/Gider ile mutabık).</summary>
public sealed record KarlilikDto(
    IReadOnlyList<KarlilikSatirDto> Satirlar, decimal ToplamGelir, decimal ToplamGider, decimal ToplamNetKar);

/// <summary>Çok-boyutlu kârlılık özeti — araç-bazlı P&amp;L'in bir boyuta (grup/şube/segment) göre toplamı.
/// TürevRent'in "araç gelir-gider tablosu × N boyut" paritesi: araç=KarlilikDto, hizmet=EkHizmetRaporDto,
/// grup/şube/segment=bu.</summary>
public sealed record KarlilikOzetSatirDto(string Boyut, int AracAdet, decimal Gelir, decimal Gider, decimal NetKar);
public sealed record KarlilikOzetDto(
    string BoyutAdi, IReadOnlyList<KarlilikOzetSatirDto> Satirlar, decimal ToplamGelir, decimal ToplamGider, decimal ToplamNetKar);

// ---------- Araç Karnesi (araç ön muhasebe 360°) ----------

/// <summary>Araç karnesi başlığı: kimlik + edinim/filo yaşam döngüsü alanları (Vehicle'dan).</summary>
public sealed record AracKarneHeaderDto(
    Guid VehicleId, string Plaka, string? Marka, string? Tip, string? Grup, string? Segment,
    string? Sube, string? AracSahibi, VehicleStatus Durum, int Km,
    decimal? AlimBedeli, DateTimeOffset? AlimTarihi, decimal? IkinciElDeger,
    DateTimeOffset? FiloGirisTarih, DateTimeOffset? FiloCikisTarih,
    DateTimeOffset? SonBakimTarih, int? SonBakimKm);

/// <summary>Yıllık P&amp;L satırı (yıl = UTC takvim yılı; defterden).</summary>
public sealed record AracYilPnlRow(int Yil, decimal Gelir, decimal Gider, decimal NetKar);

/// <summary>Gelir-kaynak / gider-kategori kırılım satırı. YuzdeGelir = Tutar ÷ ToplamGelir (gelir 0 → null).</summary>
public sealed record AracKirilimRow(string Kategori, decimal Tutar, decimal? YuzdeGelir);

/// <summary>Olay zaman çizelgesi satırı — KAYNAK VARLIKTAN; Tutar BİLGİ amaçlı (brüt/native, P&amp;L'e
/// TOPLANMAZ; dövizli kayıtta defterin TL katkısından farklı olabilir). DeftereYansir: true = bu olayın
/// parası defter P&amp;L'inde; false = yalnız bilgi (ör. servis maliyeti — mali belge değil).</summary>
public sealed record AracOlayRow(DateTimeOffset Tarih, string Tur, string Aciklama, decimal? Tutar, bool DeftereYansir);

/// <summary>Defterden araç-scope'lu gelir satırı (Tutar = işaretli base: iade Borç Gelir negatif).</summary>
public sealed record AracLedgerGelirRow(int Yil, string Kaynak, decimal Tutar);

/// <summary>Defterden araç-scope'lu gider satırı (Tutar = base; kategori kaynak varlıktan etiketlenir).</summary>
public sealed record AracLedgerGiderRow(int Yil, string Kategori, decimal Tutar);

/// <summary>Servis aralığı (KPI hamı: serviste geçen gün; Cikis null = hâlâ serviste).</summary>
public sealed record AracServisGunRow(DateTimeOffset Giris, DateTimeOffset? Cikis);

/// <summary>Araç karnesi repo ham paketi — DB erişimi repo'da, matematik ReportService'te (desen).
/// Vehicle null = bulunamadı / başka tenant (RLS) → servis null döndürür → sayfa 404.</summary>
public sealed record AracKarneRawDto(
    Vehicle? Vehicle,
    IReadOnlyList<AracLedgerGelirRow> Gelirler,
    IReadOnlyList<AracLedgerGiderRow> Giderler,
    IReadOnlyList<AracOlayRow> Olaylar,
    IReadOnlyList<DolulukKiraRowDto> KiraAraliklari,
    IReadOnlyList<AracServisGunRow> ServisAraliklari,
    int KiraSayisi, int ToplamKatedilenKm, DateTimeOffset? SonSatisTarih,
    decimal OmurGelir, decimal OmurGider,
    FiloTutSatRow TutSatHam, decimal? GrupOrtDegerOrani,
    IReadOnlyList<AracKmLogRow>? KmLoglari = null);

/// <summary>Km zaman-serisi ham satırı (FAZ 2.5) — Tarih artan sıralı gelir (dönem-km farkı için).</summary>
public sealed record AracKmLogRow(DateTimeOffset Tarih, int Km);

/// <summary>Kurumsal araç KPI bloğu — SAHİPLİK PENCERESİ (ömür boyu) metrikleri; sayfadaki dönem
/// filtresinden bağımsız. Pencere: W_bas = FiloGirisTarih ?? AlimTarihi; W_bit = FiloCikisTarih ??
/// (Satıldıysa son satış tarihi) ?? şimdi. Gün matematiği kapsayıcı takvim günü (GetDolulukAsync deseni).
/// RevPACD = gelir ÷ sahiplik-günü (boş günler dahil gerçek verim); ADR = gelir ÷ kiralanan gün
/// (ortalama günlük fiyat); özdeşlik RevPACD ≈ ADR × Doluluk. Oranlar yalnız pozitif paydayla; aksi null.
/// EkonomikKar = NetKar − GerçekleşenAmortisman (AlimBedeli − IkinciElDeger) — kurumsal alıcının baktığı kâr.</summary>
public sealed record AracKpiDto(
    int SahiplikGun, int KiralananGun, int ServisGun, int BosGun,
    decimal? DolulukYuzde, decimal? RevPacd, decimal? Adr, decimal? KmBasinaMaliyet,
    decimal? NetMarjYuzde, decimal? RoiYuzde, int? GeriOdemeAy, decimal Tco,
    decimal? GerceklesenAmortisman, decimal? AylikAmortisman, decimal? EkonomikKar,
    int ToplamKatedilenKm, int KiraSayisi);

/// <summary>Araç karnesi — tek araç 360° ön muhasebe. P&amp;L DEFTERDEN (Karlilik satırıyla mutabık —
/// parite testi kilitler); olaylar kaynak varlıktan bilgi amaçlı. "(Atanmamış)" gelir/gider tek-araç
/// karnesinde yer almaz (Σ araç kartları + Atanmamış = defter toplamı invaryantı).
/// Kpi ömür-boyu (dönem filtresinden bağımsız); MaliyetModel yalnız AlimBedeli>0 iken (ömür-boyu holding
/// varsayımı; şeffaf makul-varsayım modeli — MaliyetHesapService).</summary>
public sealed record AracKarneDto(
    AracKarneHeaderDto Header,
    decimal ToplamGelir, decimal ToplamGider, decimal ToplamNetKar,
    IReadOnlyList<AracYilPnlRow> YillikPnl,
    IReadOnlyList<AracKirilimRow> GelirKaynak,
    IReadOnlyList<AracKirilimRow> GiderKategori,
    IReadOnlyList<AracOlayRow> Olaylar,
    AracKpiDto Kpi,
    MaliyetHesapSonuc? MaliyetModel,
    TutSatSinyalDto TutSat,
    decimal? BasaBasGunluk = null,
    KalintiProjeksiyonDto? Kalinti = null,
    int? DonemKm = null,
    decimal? DonemKmMaliyet = null);

/// <summary>Kalıntı projeksiyonu (FAZ 2.4): 2-hane yuvarlak yıllık oran (gösterilen == kullanılan),
/// OranGozlenen=false → şeffaf varsayılan 0.85 (yaş&lt;1 / alım verisi yok); 12/24 ay sonu tahmin.</summary>
public sealed record KalintiProjeksiyonDto(
    decimal YillikOran, bool OranGozlenen, decimal Deger12Ay, decimal Deger24Ay);

/// <summary>Filo özet kartı (FAZ 2.4): tut/sat adayı (sinyal ≥2) araç sayısı + adayların 12-ay-sonu
/// tahmini kalıntı toplamı (aday satılırsa geri kazanım — muhafazakâr uç: bugünkü değer değil).</summary>
public sealed record TutSatAdayOzetDto(int AracSayisi, decimal TahminiGeriKazanim12Ay);

// ---------- Filo Analiz Panosu ----------

/// <summary>Filo analiz satırı — TÜM filo araçları listelenir (dönemde hareketi olmayan araç 0 P&amp;L
/// ile görünür — gizli zararlı/boşta araç panodan kaçmaz). P&amp;L sütunları DÖNEM-pencereli (Karlilik ile
/// mutabık); KPI sütunları (Doluluk/ROI/KmMaliyet/YasAy) ÖMÜR BOYU (karne semantiğiyle birebir).
/// ROI: satılmışta kapanış getirisi (satış gelirde, alım düşülür), aktifte defter ROI. Silinmiş aracın
/// defter kalıntısı "(bilinmeyen araç)" satırı olarak korunur (mutabakat) — kohorta ve karne linkine girmez.</summary>
public sealed record FiloAnalizRow(
    Guid VehicleId, string Plaka, string? Grup, string? Segment, string? Sube,
    decimal Gelir, decimal Gider, decimal NetKar,
    decimal? DolulukYuzde, decimal? RoiYuzde, decimal? KmBasinaMaliyet,
    int SahiplikGun, int KiralananGun, int? YasAy,
    int TutSatSinyal = 0,
    decimal? SinifEndeks = null);

/// <summary>Yaş kohortu satırı — alım tarihine göre kova (0-1/1-2/2-3/3+ yıl); ortalamalar yalnız
/// değeri olan araçlar üzerinden (kurumsal "cost-per-km eğrisi" görünümü).</summary>
public sealed record FiloKohortRow(string Kova, int AracAdet, decimal? OrtKmMaliyet, decimal? OrtDoluluk);

/// <summary>Filo analiz panosu. Toplamlar dönem-pencereli defterle mutabık: Σ satır + Atanmamış = defter
/// Gelir/Gider (invaryant — Karlilik ile aynı). Atanmamış ayrı gösterilir (satır değil).</summary>
public sealed record FiloAnalizDto(
    IReadOnlyList<FiloAnalizRow> Satirlar,
    decimal ToplamGelir, decimal ToplamGider, decimal ToplamNetKar,
    decimal AtanmamisGelir, decimal AtanmamisGider,
    IReadOnlyList<FiloKohortRow> YasKohortu,
    FiloHavuzKpiDto? HavuzKpi = null,
    TutSatAdayOzetDto? TutSatAday = null);

/// <summary>Filo-geneli HAVUZ KPI'ları (FAZ 2.3) — Σ gelir/gün havuzlarından hesaplanır; araç-bazlı
/// KPI'ların ortalaması DEĞİL (karışık-payda yasak). Ömür-boyu semantik (satır KPI'larıyla aynı):
/// gelir = Σ araç ömür geliri (defter, araca atanabilen), günler = Σ sahiplik/kiralanan. Silinmiş
/// aracın defter kalıntısı havuza girmez (sahiplik günü yok — paydasız gelir RevPACD'yi şişirir).
/// Doluluk cap-100 (karne konvansiyonu); oranlar yalnız pozitif paydayla, aksi null.</summary>
public sealed record FiloHavuzKpiDto(
    int SahiplikGun, int KiralananGun, decimal OmurGelir,
    decimal? DolulukYuzde, decimal? RevPacd, decimal? Adr);

/// <summary>Filo analiz repo ham paketi. KarlilikPencere = dönem-filtreli araç P&amp;L satırları;
/// KarlilikOmur = pencereden bağımsız (from/to null ise aynı liste). Kiralar İptal-dışı, efektif bitişli.</summary>
public sealed record FiloAnalizRawDto(
    IReadOnlyList<KarlilikSatirDto> KarlilikPencere,
    IReadOnlyList<KarlilikSatirDto> KarlilikOmur,
    IReadOnlyList<FiloAracRow> Araclar,
    IReadOnlyList<FiloKiraRow> Kiralar,
    IReadOnlyList<FiloTutSatRow> TutSatHam);

/// <summary>Filo aracı KPI hamı (kimlik boyutları + sahiplik penceresi alanları + son tamamlanmış satış).
/// Pano satırları BU listeden tohumlanır (adversarial F-D: dönemde hareketi olmayan araç da görünür).</summary>
public sealed record FiloAracRow(
    Guid Id, string Plaka, string? Grup, string? Segment, string? Sube,
    decimal? AlimBedeli, DateTimeOffset? AlimTarihi,
    DateTimeOffset? FiloGirisTarih, DateTimeOffset? FiloCikisTarih,
    VehicleStatus Durum, DateTimeOffset? SonSatisTarih,
    decimal? IkinciElDeger = null);

/// <summary>Filo kirası (İptal hariç): efektif aralık (GercekDonusTar ?? BitTar) + km çifti.</summary>
public sealed record FiloKiraRow(Guid VehicleId, DateTimeOffset Bas, DateTimeOffset Bit, int? CikisKm, int? DonusKm);

// ---------- Tut/Sat (defleet) sinyali — FAZ 2.2 ----------

/// <summary>Tut/Sat eşikleri (şeffaf varsayım; appsettings "TutSat" bölümüyle override edilebilir).
/// DegerOrani: son-12-ay araç gideri ÷ İkinciElDeğer eşiği. SinifKati: sınıf (Grup) ortalamasının katı.</summary>
public sealed record TutSatEsikleri(decimal DegerOrani = 0.45m, decimal SinifKati = 1.5m)
{
    public static readonly TutSatEsikleri Varsayilan = new();
}

/// <summary>Tut/Sat sinyal sonucu: 0-3 kural tetiklendi + gerekçe metinleri (karne kartı / filo kolonu).</summary>
public sealed record TutSatSinyalDto(int Sinyal, IReadOnlyList<string> Gerekceler);

// ---------- FAZ 6.2 — dashboard/bildirim derinliği ----------

/// <summary>Tut/Sat ham paketinin araç meta satırı (grup ortalaması + sinyal hesabı girdisi).</summary>
public sealed record TutSatAracRow(Guid Id, string Plaka, string? Grup, decimal? IkinciElDeger);

/// <summary>Filo tut/sat hamı + araç metası — FiloAnaliz raw'ı ile FiloBildirimUretici'nin TEK
/// doğruluk kaynağı (OrtakSorgular.TutSatHamAsync; O12a vade-birleşimi deseni).</summary>
public sealed record TutSatHamPaket(IReadOnlyList<FiloTutSatRow> Ham, IReadOnlyList<TutSatAracRow> Araclar);

/// <summary>Aylık gelir trend noktası (Home mini-trend). AyBas = ayın 1'i UTC.</summary>
public sealed record AylikGelirNokta(DateTimeOffset AyBas, decimal Gelir);

/// <summary>Aylık gelir+gider+net trend noktası (Finans Analiz 12-ay grafiği). AyBas = ayın 1'i UTC;
/// değerler GetGelirGiderAsync ay-penceresi toplamları (iade netlenmiş, base TL).</summary>
public sealed record AylikGelirGiderNokta(DateTimeOffset AyBas, decimal Gelir, decimal Gider, decimal NetKar);

/// <summary>Filo tut/sat hamı: araç-başına son-12-ay / önceki-12-ay gider (defter, base) ve km.</summary>
public sealed record FiloTutSatRow(Guid VehicleId, decimal Gider12, decimal GiderOnceki12, int Km12, int KmOnceki12);
