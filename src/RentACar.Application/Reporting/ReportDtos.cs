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

/// <summary>FAZ-27 — karşılaştırmalı analiz pivotunda tek satır (bir kırılım değeri × aylar).</summary>
/// <param name="Kirilim">Kırılım değeri (araç grubu / rez kaynağı / çıkış noktası). Boş değer "(belirtilmemiş)".</param>
/// <param name="Aylar">Ay anahtarı ("yyyy-MM") → hücre değeri. Veri olmayan ay HİÇ bulunmaz (0 varsayılır).</param>
public sealed record KarsilastirmaliSatirDto(string Kirilim, IReadOnlyDictionary<string, decimal> Aylar)
{
    public decimal Toplam => Aylar.Values.Sum();
    public decimal Ay(string anahtar) => Aylar.TryGetValue(anahtar, out var v) ? v : 0m;
}

/// <summary>
/// FAZ-27 — karşılaştırmalı durum analizi (hacim pivotu).
///
/// <para><b>Tutar üretmez</b> — yalnız ADET ya da GÜN sayar. Bu yüzden "rapor yalnız defterden"
/// kuralı burada geçerli değil: sayılan şey kaynak varlığın kendisi (kira/rezervasyon satırı),
/// para değil. Çift-sayım riski parasal değildir.</para>
/// </summary>
public sealed record KarsilastirmaliAnalizDto(
    IReadOnlyList<string> AyAnahtarlari, IReadOnlyList<KarsilastirmaliSatirDto> Satirlar,
    string Tablo, string VeriTuru, string Kirilim)
{
    /// <summary>Ay bazında sütun toplamı.</summary>
    public decimal AyToplami(string ay) => Satirlar.Sum(s => s.Ay(ay));
    public decimal GenelToplam => Satirlar.Sum(s => s.Toplam);
}

/// <summary>FAZ-27 filtresi. Seçenekler SABİT: canlının serbest pivot ızgarası yerine üç boyut.</summary>
public sealed class KarsilastirmaliAnalizFilter
{
    /// <summary>"Kira" (varsayılan) ya da "Rezervasyon".</summary>
    public string Tablo { get; set; } = "Kira";
    /// <summary>"Adet" (varsayılan) ya da "Gun".</summary>
    public string VeriTuru { get; set; } = "Adet";
    /// <summary>"AracGrubu" (varsayılan), "RezKaynagi" ya da "CikisNoktasi".</summary>
    public string Kirilim { get; set; } = "AracGrubu";
    /// <summary>Başlangıç tarihine göre pencere. Boşsa son 12 ay.</summary>
    public DateTimeOffset? Bas { get; set; }
    public DateTimeOffset? Bit { get; set; }
    /// <summary>Çıkış ofisi (tam eşleşme) — "İşlem Şube" karşılığı.</summary>
    public string? Ofis { get; set; }
}

/// <summary>
/// FAZ-78 — ek hizmet raporu SATIR-BAZLI detay satırı (canlı <c>extralar_raporu.aspx</c>).
///
/// <para>Tutarlar kalemin KENDİ değerleridir (kayıt anında hesaplanmış), raporda yeniden
/// hesaplanmaz. <paramref name="IlkTahsilat"/> kiranın EN ERKEN tahsilat tutarıdır — kalemin
/// tahsilatı değil (sistemde kalem-bazlı tahsilat izi yok); kolon başlığı da bunu söyler.</para>
/// </summary>
public sealed record EkHizmetDetayRow(
    Guid AddOnId, Guid RentalId, string SozlesmeNo, DateTimeOffset BasTar, DateTimeOffset BitTar,
    string Plaka, string MusteriAd, string? RezKaynagi, string? CikisOfisi,
    string Ad, decimal Miktar, decimal BirimNetFiyat, decimal KdvOrani,
    decimal Net, decimal Kdv, decimal Brut,
    DateTimeOffset EklenmeTarihi, string? SatanPersonel, decimal? IlkTahsilat, bool SistemKalemi);

/// <summary>FAZ-78 — ek hizmet detay filtresi.</summary>
public sealed class EkHizmetDetayFilter
{
    /// <summary>Kalem eklenme tarihi aralığı (özet raporla AYNI pencere tanımı).</summary>
    public DateTimeOffset? Bas { get; set; }
    public DateTimeOffset? Bit { get; set; }
    /// <summary>Ek hizmet adı / sözleşme no / plaka / müşteri içinde geçen metin.</summary>
    public string? Ara { get; set; }
    /// <summary>Rezervasyon kaynağı (kiranın bağlı olduğu rezervasyondan).</summary>
    public string? RezKaynagi { get; set; }
    /// <summary>Kiranın çıkış ofisi (tam eşleşme).</summary>
    public string? Ofis { get; set; }
    public Guid? PersonelId { get; set; }
    /// <summary><c>true</c> → sistem ücret kalemleri (SYS-*) gizlenir.</summary>
    public bool SistemKalemleriniGizle { get; set; }
    public int EnFazla { get; set; } = 2000;
}

/// <summary>
/// FAZ-68 — Tahsilat raporu SÖZLEŞME-SATIRI mutabakat satırı.
///
/// <para>Amaç bir sözleşmenin üç rakamını yan yana koymak: <b>ne kadar borçlandı</b>
/// (<paramref name="GenelToplam"/>), <b>ne kadarı faturalandı</b> (<paramref name="Faturalanan"/>),
/// <b>ne kadarı tahsil edildi</b> (<paramref name="Tahsilat"/>). Aradaki farklar operasyonun nerede
/// eksik kaldığını gösterir.</para>
///
/// <para><paramref name="DefterTahsilat"/> aynı tahsilatın KASA HAREKETLERİNDEN yeniden toplanmış
/// hâlidir; <paramref name="Tahsilat"/> ise sözleşme satırında tutulan bakiyedir. İkisi normalde
/// EŞİTTİR — <paramref name="TahsilatAyrimi"/> sıfırdan farklıysa o sözleşmede bir tutarsızlık var
/// demektir. Mutabakat raporunun asıl işi budur.</para>
/// </summary>
public sealed record TahsilatMutabakatRowDto(
    Guid RentalId, string SozlesmeNo, string? Plaka, Guid MusteriId, string MusteriAd,
    DateTimeOffset BasTar, RentalStatus Durum, string Doviz,
    decimal Matrah, decimal DamgaVergisi, decimal GenelToplam,
    decimal Tahsilat, decimal DefterTahsilat, decimal Faturalanan,
    decimal MusteriBakiye)
{
    /// <summary>Sözleşmenin kalan borcu (GenelToplam − Tahsilat).</summary>
    public decimal Bakiye => GenelToplam - Tahsilat;
    /// <summary>Henüz faturalanmamış tutar (GenelToplam − Faturalanan). Negatif = fazla faturalanmış.</summary>
    public decimal FaturaFarki => GenelToplam - Faturalanan;
    /// <summary>Sözleşme satırı ile kasa hareketleri arasındaki fark. SIFIR OLMALI.</summary>
    public decimal TahsilatAyrimi => Tahsilat - DefterTahsilat;
    public bool Tutarsiz => TahsilatAyrimi != 0m;
}

/// <summary>FAZ-68 — mutabakat satır modu filtresi.</summary>
public sealed class TahsilatMutabakatFilter
{
    /// <summary>Sözleşme no / plaka / müşteri adı içinde geçen metin.</summary>
    public string? Ara { get; set; }
    public Guid? MusteriId { get; set; }
    /// <summary>Kira başlangıç tarihi aralığı.</summary>
    public DateTimeOffset? Bas { get; set; }
    public DateTimeOffset? Bit { get; set; }
    public RentalStatus? Durum { get; set; }
    /// <summary>"acik" = bakiyesi kalanlar, "kapali" = bakiyesi sıfır, boş = hepsi.</summary>
    public string? BakiyeDurumu { get; set; }
    /// <summary><c>true</c> → yalnız sözleşme/kasa tutarsızlığı olan satırlar.</summary>
    public bool YalnizTutarsiz { get; set; }
    public int EnFazla { get; set; } = 2000;
}

/// <summary>
/// FAZ-61 — Extre özeti satırı: FATURA seviyesinde müşteri + plaka + vade görünümü.
///
/// <para><b>"Açık tutar" DEĞİL, BRÜT tutardır.</b> Sistemde fatura-bazlı tahsilat mahsubu YOKTUR:
/// tahsilatlar cari bakiyesine yazılır, tek tek faturalara kapatılmaz. Dolayısıyla "bu faturanın
/// ne kadarı ödendi" sorusunun veriye dayalı bir cevabı yok. Yaşlandırma raporu da aynı gerekçeyle
/// brüt çalışır. Uydurulmuş bir mahsup yerine brüt gösterilir ve ekranda bu açıkça yazılır;
/// carinin gerçek net durumu <c>CariBalanceDto</c>'dadır.</para>
///
/// <para>İade faturaları NEGATİF işaretlidir (DB'de pozitif saklanırlar).</para>
/// </summary>
public sealed record ExtreOzetiRowDto(
    Guid FaturaId, string FaturaNo, DateTimeOffset Tarih, DateTimeOffset? VadeTarihi,
    Guid CariId, string CariAd, string? Plaka, string? SozlesmeNo, string? CikisOfisi,
    decimal Tutar, string Doviz, decimal Kur, bool IadeMi)
{
    /// <summary>İade işaretli brüt tutar (kaynak dövizinde).</summary>
    public decimal IsaretliTutar => IadeMi ? -Tutar : Tutar;
    /// <summary>İade işaretli brüt tutarın TL karşılığı.</summary>
    public decimal IsaretliTutarTl => IsaretliTutar * Kur;

    /// <summary>Vadeye kalan/geçen gün (asOf'a göre). Vade yoksa null. Negatif = gecikmiş.</summary>
    public int? KalanGun(DateTimeOffset asOf)
        => VadeTarihi is { } v ? (v.UtcDateTime.Date - asOf.UtcDateTime.Date).Days : null;
}

/// <summary>Extre özeti filtresi.</summary>
public sealed class ExtreOzetiFilter
{
    public Guid? CariId { get; set; }
    /// <summary>Kiranın çıkış ofisi (tam eşleşme). Kirasız (manuel) faturaları eler.</summary>
    public string? Ofis { get; set; }
    /// <summary>Plaka (kısmi).</summary>
    public string? Plaka { get; set; }
    /// <summary>Fatura tarihi alt/üst sınırı.</summary>
    public DateTimeOffset? Bas { get; set; }
    public DateTimeOffset? Bit { get; set; }
    /// <summary><c>true</c> → yalnız vadesi GEÇMİŞ faturalar (vade &lt; asOf). Vadesiz kayıtlar düşer.</summary>
    public bool YalnizGecikmis { get; set; }
    public int EnFazla { get; set; } = 2000;
}

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

// ---------------------------------------------------------------------------
// FAZ-77 — filo & doluluk grafik derinliği (D4; envanter/gün SAYIMI, para toplamı YOK)
// ---------------------------------------------------------------------------

/// <summary>
/// Şube kırılımlı filo durumu (canlı <c>arac_genel_durumu_grafik.aspx</c>).
///
/// <para><b>TEK ATIF KURALI: bütün kolonlar ARACIN şubesine göre.</b> Kira/rezervasyon kolonları
/// da sözleşmenin ÇIKIŞ şubesinden değil, aracın bağlı olduğu şubeden sayılır. Karıştırsaydık
/// satır kendi içinde tutarsız olurdu (Filo=10 iken Çıkışlar=15 gibi — başka şubenin aracı bu
/// satırın payına yazılırdı). "Gişe bazlı" (CikisSubeId) görünüm BİLİNÇLİ olarak kapsam dışı.</para>
///
/// <para><b>Doluluk paydası KULLANILABİLİR filo</b> = Filo − Satıldı − Pasif. Satılmış/pasif araç
/// kiralanamaz; paydada tutmak doluluğu sistematik olarak düşük gösterirdi. Payda ≤ 0 ise yüzde
/// <c>null</c> (yalnız pozitif paydayla oran — karne dersi), 0 değil.</para>
/// </summary>
public sealed record FiloSubeRow(
    string Sube, int Filo, int Bos, int Kirada, int Bakimda, int Pasif, int Satildi,
    int Satilik, int Baf, decimal? DolulukYuzde,
    int Cikislar, int Donusler, int Cikacaklar, int Donecekler, int GidenRez);

/// <summary>Şube kırılımlı filo raporu + kullanılan ileri-bakış penceresi (gün).</summary>
public sealed record FiloSubeDto(IReadOnlyList<FiloSubeRow> Satirlar, int PencereGun)
{
    /// <summary>Şube satırlarının toplamı — tenant-geneli KPI kartlarıyla mutabık olmalı.</summary>
    public int ToplamFilo => Satirlar.Sum(x => x.Filo);
}

/// <summary>Doluluk "Karşılaştır" boyutu.</summary>
public enum DolulukBoyut
{
    /// <summary>Kırılım yok — tek seri (tüm filo).</summary>
    Yok = 0,
    Sube = 1,
    AracGrubu = 2,
    /// <summary>Rezervasyon kaynağı — filo BÖLÜNTÜSÜ DEĞİL (bkz. <see cref="DolulukGunlukDto"/>).</summary>
    RezervasyonKaynagi = 3
}

/// <summary>
/// Gün × boyut doluluk satırı. <paramref name="AracSayisi"/> o satırın PAYDASINI belirleyen araç
/// adedi; <paramref name="KiraGun"/>/<paramref name="RezGun"/> o gün aktif kira/rezervasyon adedi
/// (gün başına, kapsayıcı aralık).
/// </summary>
public sealed record DolulukGunRow(
    DateOnly Gun, string Seri, int AracSayisi, int KiraGun, int RezGun,
    decimal? KiraYuzde, decimal? RezYuzde);

/// <summary>
/// Gün-kırılımlı doluluk.
///
/// <para><b>PAYDA UYARISI.</b> Şube/Araç Grubu boyutlarında payda o grubun KENDİ araçlarıdır
/// (gerçek filo bölüntüsü) → yüzdeler grubun kendi doluluğudur ve toplamları genel doluluğa EŞİT
/// DEĞİLDİR. Rezervasyon Kaynağı bir filo bölüntüsü değildir (araç bir kaynağa ait olmaz) → payda
/// TÜM FİLO alınır ve yüzde "kaynak, filo kapasitesinin ne kadarını doldurdu" demektir; bu
/// yüzdeler toplanabilir. İki semantiği tek kolonda etiketsiz sunmak yanıltıcı olurdu.</para>
///
/// <para>Kira kolonu ARACIN şubesine/grubuna göre atanır — payda da öyle (karışık payda yasak).
/// Rezervasyon kaynağı satırlarında kaynak rezervasyonun kendi alanıdır.</para>
/// </summary>
public sealed record DolulukGunlukDto(
    IReadOnlyList<DolulukGunRow> Satirlar, DolulukBoyut Boyut, string PaydaAciklama,
    int DonemGun, int ToplamKiraGun, int ToplamRezGun);

/// <summary>Doluluk ham kira satırı — araç kimliği + şube/grup atfı taşır (FAZ-77 kırılımı için).</summary>
public sealed record DolulukKiraAtifRow(
    DateTimeOffset Bas, DateTimeOffset Bit, Guid VehicleId, string Sube, string Grup);

/// <summary>Doluluk ham rezervasyon satırı — kaynak + aracın şube/grubu.</summary>
public sealed record DolulukRezAtifRow(
    DateTimeOffset Bas, DateTimeOffset Bit, Guid VehicleId, string Sube, string Grup, string Kaynak);

/// <summary>Araç envanteri atıf satırı (payda) — her araç bir şubeye ve bir gruba aittir.</summary>
public sealed record DolulukAracAtifRow(Guid VehicleId, string Sube, string Grup);

/// <summary>Gün-kırılımlı doluluk için ham paket (tek DB turu).</summary>
public sealed record DolulukAtifPaket(
    IReadOnlyList<DolulukAracAtifRow> Araclar,
    IReadOnlyList<DolulukKiraAtifRow> Kiralar,
    IReadOnlyList<DolulukRezAtifRow> Rezervasyonlar);

/// <summary>Şube kırılımlı filo raporu için ham araç satırı.</summary>
public sealed record FiloAracHamRow(Guid Id, string Sube, VehicleStatus Durum, FiloStatus? FiloDurum);

/// <summary>Şube kırılımlı filo raporu için ham kira satırı (araç şubesi atfıyla).</summary>
public sealed record FiloKiraHamRow(string Sube, DateTimeOffset Bas, DateTimeOffset Bit);

/// <summary>Şube kırılımlı filo raporu ham paketi.</summary>
public sealed record FiloSubeHamPaket(
    IReadOnlyList<FiloAracHamRow> Araclar,
    IReadOnlyList<FiloKiraHamRow> Kiralar,
    IReadOnlyList<(string Sube, DateTimeOffset Bas)> Rezervasyonlar,
    IReadOnlyList<string> AcikBafSubeleri);
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
    Guid VehicleId, string Plaka, int GuncelKm, int? SonrakiBakimKm, int? KalanKm, string? Kaynak = null,
    // FAZ-76 — rapor kolonları. Varsayılanlı eklendi: FiloBildirimUretici'nin mevcut kullanımı
    // (VehicleId/Plaka/KalanKm) DEĞİŞMEDEN çalışır.
    string? Marka = null, string? Tip = null, int? ModelYili = null,
    string? Yakit = null, string? Vites = null, string? Sube = null,
    DateTimeOffset? SonServisTarihi = null, int? SonServisKm = null, bool Aktif = true);

/// <summary>FAZ-76 — periyodik servis raporu filtresi. Hepsi opsiyonel (boş = eski davranış).</summary>
public sealed class PeriyodikServisFilter
{
    public string? Plaka { get; set; }
    public string? Sube { get; set; }
    /// <summary>null = hepsi; true/false = yalnız aktif/pasif araç.</summary>
    public bool? Aktif { get; set; }
    /// <summary>Yalnız kalan km'si bu eşiğin ALTINDA olanlar (yaklaşan bakım). null = hepsi.</summary>
    public int? UyariEsigi { get; set; }
}

/// <summary>Kira KM detay satırı — roadmap H1. KatedilenKm = DonusKm − CikisKm.</summary>
public sealed record KmDetayRow(
    Guid RentalId, string SozlesmeNo, string Plaka, int CikisKm, int DonusKm,
    int KatedilenKm, int KmLimit, int FazlaKm, decimal FazlaKmBedeli,
    // FAZ-76 — araç/sözleşme künyesi (varsayılanlı: mevcut çağıranlar etkilenmez).
    string? Marka = null, string? Tip = null, string? Yakit = null, string? Vites = null,
    DateTimeOffset? BasTar = null, DateTimeOffset? BitTar = null);

/// <summary>Rezervasyon kaynak özeti — roadmap H2. Kaynak başına adet/gün/ciro.</summary>
public sealed record RezervasyonKaynakRow(string Kaynak, int Adet, int ToplamGun, decimal ToplamCiro,
    // FAZ-76 — iptal edilenler AYRI sayılır: toplamdan düşülür ama görünür kalır.
    int IptalAdet = 0);

/// <summary>FAZ-76 — rezervasyon kaynak raporu filtresi.</summary>
public sealed class RezervasyonKaynakFilter
{
    public DateTimeOffset? Bas { get; set; }
    public DateTimeOffset? Bit { get; set; }
    /// <summary>Tarih filtresinin uygulanacağı alan: "Cikis" (varsayılan), "Donus" ya da "Kayit".</summary>
    public string TarihTipi { get; set; } = "Cikis";
    public string? Ofis { get; set; }
    public string? Grup { get; set; }
    /// <summary>
    /// <c>false</c> (varsayılan) → İPTAL rezervasyonlar adet/gün/ciroya GİRMEZ. Eski davranış
    /// iptalleri de sayıyordu — bu bir veri-doğruluğu hatasıydı (bkz. FAZ-76).
    /// </summary>
    public bool IptalleriDahilEt { get; set; }
}

/// <summary>Fatura dönem satırı — roadmap H2. Vade/cari/tutar/durum.</summary>
public sealed record FaturaDonemRow(
    Guid InvoiceId, string No, DateTimeOffset Tarih, DateTimeOffset? VadeTarihi,
    string Cari, decimal GenelToplam, string Currency, decimal Kur, string Durum, bool IadeMi);

/// <summary>Araç durum-takip (gün kırılımı) satırı — roadmap H3. Bos = Toplam − Dolu − Bakim (≥0).</summary>
public sealed record AracDurumTakipRow(DateTimeOffset Gun, int ToplamArac, int Dolu, int Bakim, int Bos,
    /// <summary>O gün AÇIK olan BAF (araç tahsis) adedi — bilgi kolonu, Bos hesabına GİRMEZ.</summary>
    int ToplamBaf = 0);

/// <summary>
/// FAZ-12 Bölüm A — araç durum-takip ARAÇ bazlı satır (canlı <c>arac_durum_takip.aspx</c> grain'i).
///
/// <para>Gün görünümü (<see cref="AracDurumTakipRow"/>) "her gün filoda kaç araç neydi" der; bu
/// satır transpozudur: "seçilen aralıkta BU araç kaç gün doluydu / bakımdaydı / bafta / boştaydı".
/// İkisi aynı ham veriden (araç × gün durumu) türer, biri diğerinin yerine geçmez.</para>
///
/// <para><b>KOVALAR ÇAKIŞMAZ (öncelik: Dolu &gt; Bakım &gt; Baf &gt; Boş).</b> Bir araç aynı gün hem
/// kirada hem serviste görünebilir (veri girişi çakışması); kovalar önceliksiz sayılsaydı toplam
/// aralık gün sayısını aşar, "Boş" negatife düşerdi. Öncelik sayesinde <c>DoluGun + BakimGun +
/// BafGun + BosGun == ToplamGun</c> her satırda GARANTİDİR (kalıcı test kilidi). Kira gelir üreten
/// durum olduğu için önceliği en yüksektir.</para>
///
/// <para>Bu satır SAYIM üretir, para DEĞİL — "P&amp;L yalnız defterden" kuralının kapsamı dışındadır.
/// Canlıdaki "Potansiyel" (boş gün × günlük ücret) kolonu bilinçli olarak YOK: fiyat kaynağı ayrı
/// bir karar (bkz. KARARLAR.md FAZ-79).</para>
/// </summary>
public sealed record AracDurumTakipAracRow(
    Guid VehicleId, string Plaka, string? Sipp, string? Grup, string? Sube, string? AracSahibi,
    int ToplamGun, int DoluGun, int BakimGun, int BafGun, int BosGun);

/// <summary>
/// FAZ-12 — araç durum-takip filtresi. HER İKİ görünüm (gün / araç) de aynı filtreyi kullanır ki
/// görünüm değiştirince kullanıcının seçimi kaybolmasın ve iki görünüm aynı araç kümesini anlatsın.
/// Tüm alanlar aracın KENDİ alanlarına bakar (tek-atıf kuralı: kira/servis/BAF sayımları da aynı
/// araç kümesinden gelir).
/// </summary>
public sealed class AracDurumTakipFilter
{
    /// <summary>Aracın şubesi (tam eşleşme, canlıdaki "Ofis" süzgecinin karşılığı).</summary>
    public string? Sube { get; set; }
    public string? AracSahibi { get; set; }
    public string? Grup { get; set; }
    public string? Sipp { get; set; }
    /// <summary>Plaka — içerir (normalize edilmiş plakaya karşı boşluksuz aranır).</summary>
    public string? Plaka { get; set; }
}

/// <summary>
/// FAZ-12 Bölüm B — araç GÜNLÜK durum satırı (canlı <c>arac_gunluk_durum.aspx</c>): seçilen GÜNDE
/// aktif olan kiranın o güne düşen gelir kesiti.
///
/// <para><b>Bu bir PROJEKSİYONDUR, defter kaydı DEĞİLDİR.</b> Sözleşme tutarı faturalanan gün
/// sayısına düz bölünür; hiçbir yere postlanmaz, hiçbir P&amp;L raporuna girmez. Gelir/gider
/// gerçeği <c>AccountLedgerEntry</c>'dedir (Kârlılık / Filo Analiz / Araç Karnesi).</para>
///
/// <para><b>Kira ve hizmet AYRI bölünür — NEDEN:</b> <c>RentalContract.GenelToplam</c> kira
/// dövizinde tutulurken <c>RentalAddOn</c> tutarları baz parada (TRY) saklanır. GenelToplam'dan ek
/// hizmet brütünü çıkarmak dövizli sözleşmede para birimi karıştırırdı. Bu yüzden
/// <paramref name="GunlukKira"/> = baz kira brütü (Tutar + fazla km + yakıt + uzatma) × KurSnapshot
/// ÷ gün, <paramref name="GunlukHizmet"/> = Σ ek hizmet brütü ÷ gün. TRY sözleşmede
/// <c>GunlukToplam × Gun == GenelToplam</c> birebir tutar.</para>
/// </summary>
public sealed record AracGunlukDurumRow(
    Guid VehicleId, string Plaka, string? Sipp, string? Grup, string? AracSahibi,
    Guid RentalId, string SozlesmeNo, string Musteri, string? CikisOfisi,
    DateTimeOffset BasTar, DateTimeOffset BitTar, int Gun,
    decimal GunlukKira, decimal GunlukHizmet, decimal GunlukToplam);

/// <summary>FAZ-12 Bölüm B — araç günlük durum filtresi (araç ve kira alanları).</summary>
public sealed class AracGunlukDurumFilter
{
    public string? Plaka { get; set; }
    public string? Grup { get; set; }
    public string? Sipp { get; set; }
    public string? AracSahibi { get; set; }
    /// <summary>Kiranın çıkış ofisi (tam eşleşme).</summary>
    public string? Ofis { get; set; }
}

/// <summary>
/// FAZ-12 Bölüm C — ek hizmet satış satırı, ARAÇ kimliğiyle. Ad-bazlı özetin
/// (<see cref="EkHizmetSalesRowDto"/>) araç boyutu eklenmiş hâli; pencere tanımı BİREBİR aynıdır
/// (İptal kira hariç, tarih = kalem eklenme zamanı) — ikisi ayrışırsa pivot toplamı özetten kayar.
/// </summary>
public sealed record EkHizmetAracSalesRow(
    Guid? VehicleId, string Plaka, string? Grup, string? Sipp,
    string Ad, decimal Net, decimal Kdv, decimal Brut, Guid RentalId);

/// <summary>
/// FAZ-12 Bölüm C — araç-bazlı ek hizmet pivot satırı. <paramref name="Hucreler"/> sırası
/// <see cref="EkHizmetAracPivotDto.Kolonlar"/> ile İNDEKS OLARAK hizalıdır (satırda hiç satılmayan
/// hizmet 0 gelir — seyrek sözlük yerine tam vektör, tablo çizimi kaymasın diye).
/// </summary>
public sealed record EkHizmetAracPivotSatir(
    Guid? VehicleId, string Plaka, string? Grup, string? Sipp,
    IReadOnlyList<decimal> Hucreler, decimal Toplam, int KalemAdet);

/// <summary>
/// FAZ-12 Bölüm C (KARARLAR.md "Seçenek B") — ek hizmet raporunun ARAÇ bazlı pivot modu:
/// satır = araç, sütun = ek hizmet adı, hücre = BRÜT tutar. Canlı
/// <c>arac_gelir_gider_tablosu.aspx</c>'in ~25 ek-hizmet kolonunun karşılığı.
///
/// <para>Karlılık / Filo Analiz / Ek Hizmet üçlüsü BİRLEŞTİRİLMEDİ (kullanıcı kararı); bu yalnız
/// mevcut Ek Hizmet raporuna eklenen ikinci bir bakış açısıdır. <b>Değişmez:</b>
/// <paramref name="GenelToplam"/> == ad-bazlı özetin <c>ToplamBrut</c>'u (aynı pencerede) —
/// pivot yalnız aynı parayı başka eksende dizer, yeni para ÜRETMEZ.</para>
/// </summary>
public sealed record EkHizmetAracPivotDto(
    IReadOnlyList<string> Kolonlar,
    IReadOnlyList<EkHizmetAracPivotSatir> Satirlar,
    IReadOnlyList<decimal> KolonToplam,
    decimal GenelToplam);

/// <summary>
/// Müşteri CRM segment satırı — roadmap N3. Segment ciro eşiğiyle (VIP/Standart/Pasif).
///
/// <para>FAZ-41 genişlemesi: iletişim/projeksiyon alanları. Para alanları TL-BAZDIR
/// (<c>KurSnapshot</c> ile çarpılmış) — <see cref="ToplamCiro"/> ile aynı birimde olsunlar diye;
/// karışık-döviz toplamı sessiz bir para hatasıdır.</para>
/// </summary>
/// <param name="OrtalamaKiraBedeli">ToplamCiro / KiraSayisi (kira yoksa 0 — satır zaten kiradan doğar).</param>
/// <param name="OrtalamaKm">(DönüşKm − ÇıkışKm) ortalaması; iki km'si de dolu kira YOKSA <c>null</c>
/// (0 yazmak "hiç yol yapılmadı" iddiası olurdu — dev veride kiraların çoğunda km boş).</param>
/// <param name="IlkKiraZamani">En erken kira başlangıcı (MIN BasTar); <see cref="SonIslem"/> en geç olanı.</param>
/// <param name="HizmetBedeli">Kira ek hizmet kalemleri (<c>RentalAddOn</c>) brüt toplamı, TL-baz.
/// Kira bedelinin KENDİSİ değildir; ToplamCiro'ya zaten dahildir (GenelToplam brütü) — burada
/// yalnız "ne kadarı ek hizmetten geldi" kırılımı için ayrıca gösterilir.</param>
public sealed record MusteriSegmentRow(
    Guid CariId, string Ad, int KiraSayisi, decimal ToplamCiro, DateTimeOffset? SonIslem, string Segment,
    string? Mail = null, string? Tel = null, decimal OrtalamaKiraBedeli = 0m, decimal? OrtalamaKm = null,
    DateTimeOffset? DogumTarihi = null, DateTimeOffset? IlkKiraZamani = null, decimal HizmetBedeli = 0m);

/// <summary>
/// FAZ-41 — müşteri segment raporu süzgeci (canlı <c>musteri_crm.aspx</c> filtre barı).
///
/// <para><see cref="Bas"/>/<see cref="Bit"/> kiranın BASLANGIÇ tarihine (<c>BasTar</c>) uygulanır —
/// <c>SonIslem</c> ve <c>IlkKiraZamani</c> da aynı alandan türediği için pencere tanımı tektir.
/// Pencere daraldığında KiraSayisi/ToplamCiro da daralır (bilinçli: "bu dönemde ne yaptı").</para>
/// </summary>
public sealed class MusteriSegmentFilter
{
    public DateTimeOffset? Bas { get; set; }
    public DateTimeOffset? Bit { get; set; }
    /// <summary>Bu sayıdan AZ kirası olan müşteri listede görünmez (agregadan SONRA uygulanır).</summary>
    public int? MinKiraSayisi { get; set; }
    /// <summary>Kiranın rezervasyon kaynağı (<c>RentalContract.Kaynak</c>, tam eşleşme/harf duyarsız).</summary>
    public string? RezKaynak { get; set; }
    /// <summary>
    /// Kiranın çıkış ofisi/şubesi (<c>RentalContract.CikisOfisi</c>, tam eşleşme).
    ///
    /// <para><b>Neden Guid FK değil:</b> dev veride 22 kiranın yalnız 4'ünde <c>CikisSubeId</c>
    /// doluyken 17'sinde ofis METNİ dolu (FK yalnız Location→Sube eşleşmesi olanlarda doluyor).
    /// FK-only bir süzgeç kullanıcıya "bozuk" görünürdü. Metin süzgeci FK'lı satırları da kapsar
    /// (FK türetildiği metin zaten satırda duruyor).</para>
    /// </summary>
    public string? CikisOfis { get; set; }
}

/// <summary>
/// FAZ-41 — segment süzgecinin açılır liste seçenekleri. <b>FİLTRESİZ</b> kira kümesinden türetilir:
/// filtrelenmiş listeden türetilseydi bir seçimden sonra diğer seçenekler kaybolur, süzgeç kendini
/// kilitlerdi. Master tablodan değil KİRALARDAN okunur — böylece her seçenek en az bir satır getirir
/// ve serbest-metin girilmiş (mastera hiç eklenmemiş) ofis/kaynak değerleri de erişilebilir kalır.
/// </summary>
public sealed record MusteriSegmentSecenekleri(IReadOnlyList<string> Kaynaklar, IReadOnlyList<string> Ofisler);

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
///
/// <para><b>FAZ-79 — SÖZLEŞME (bozulması Critical):</b> ilk sekiz alan (<paramref name="Gelir"/>,
/// <paramref name="Gider"/>, <paramref name="NetKar"/> dahil) P&amp;L'dir ve YALNIZ DEFTERDEN gelir.
/// Aşağıdaki tüm ek alanlar <b>REFERANS / BİLGİ</b>dir; kaynak-varlık master alanından (araç kartı),
/// tarife matrisinden veya cari defterinden okunur ve <paramref name="Gelir"/>/<paramref name="Gider"/>/
/// <paramref name="NetKar"/> hesabına <b>ASLA</b> katılmaz. Servis bu alanları yalnız <c>with</c> ile
/// ekler — para alanlarına dokunmaz (kırılgan regresyon testi kilitler).</para>
/// </summary>
/// <param name="Sipp">SIPP/ACRISS kodu — araç kartı, yoksa araç grubu. Bilgi.</param>
/// <param name="Otopark">Aracın bağlı olduğu şube (FK çözümlü ad; FK yoksa serbest metin). Bilgi.</param>
/// <param name="RezKaynagi">Dönem içindeki kiralarda EN ÇOK görülen rezervasyon kaynağı. Bilgi.</param>
/// <param name="CariAd">Dönemdeki SON kiranın müşterisi. Bilgi.</param>
/// <param name="CariBakiye">O müşterinin GÜNCEL net cari bakiyesi (pozitif = borçlu) — cari defterinden;
/// araç P&amp;L'i DEĞİLDİR, dönem filtresinden de bağımsızdır.</param>
/// <param name="ReferansAylikMaliyet">Araç kartındaki "Aylık Maliyet" master alanı (Ana Maliyet). Deftere
/// GİRMEZ — gerçek gider Giderler modülünden postlanır; ikisi mutabık olmak ZORUNDA DEĞİLDİR.</param>
/// <param name="ReferansFiloYonetimMaliyeti">Araç kartındaki "Filo Yönetim Maliyeti" master alanı
/// (Yönetim Maliyeti). Deftere GİRMEZ.</param>
/// <param name="PotansiyelGelir">Dönem kapasitesi × onaylı tarife matrisi günlük fiyatı (KARARLAR.md
/// FAZ-79: kaynak RateMatrix, RateCard DEĞİL). Tarife çözülemezse null ("Hesaplanmadı"). Deftere GİRMEZ.</param>
/// <param name="HesaplananKdv">Bu araca atfedilen SATIŞ belgelerinin KDV'si (defterdeki Kdv hesabı, işaretli:
/// iade negatif). P&amp;L'e girmez — Gelir zaten NET taşınır; yalnız "KDV Dahil" gösterim modunu besler.</param>
/// <param name="DolulukYuzde">Sahiplik penceresi doluluk yüzdesi (cap-100). ÖMÜR BOYU — sayfa dönem
/// filtresinden bağımsız (karışık-payda yasak; Araç Karnesi/Filo Analiz ile aynı tanım).</param>
/// <param name="RevPacd">Ömür geliri ÷ sahiplik günü (araç başına günlük verim). ÖMÜR BOYU.</param>
/// <param name="Adr">Ömür geliri ÷ kiralanan gün (ortalama günlük fiyat). ÖMÜR BOYU.</param>
public sealed record KarlilikSatirDto(
    Guid? VehicleId, string Plaka, string? Sube, string? Grup, string? Segment, decimal Gelir, decimal Gider, decimal NetKar,
    // ---- BURADAN AŞAĞISI DEFTER-DIŞI REFERANS/BİLGİ — P&L toplamına KARIŞMAZ ----
    string? Sipp = null, string? Otopark = null, string? RezKaynagi = null,
    string? CariAd = null, decimal? CariBakiye = null,
    decimal? ReferansAylikMaliyet = null, decimal? ReferansFiloYonetimMaliyeti = null,
    decimal? PotansiyelGelir = null, decimal? HesaplananKdv = null,
    decimal? DolulukYuzde = null, decimal? RevPacd = null, decimal? Adr = null,
    int SahiplikGun = 0, int KiralananGun = 0, int KiraAdet = 0)
{
    /// <summary>Ana + Yönetim referans maliyeti (canlının "Toplam Maliyet" kolonu). İkisi de boşsa null —
    /// 0 yazmak "maliyeti sıfır" yanılsaması üretirdi. DEFTER GİDERİ DEĞİLDİR.</summary>
    public decimal? ReferansToplamMaliyet
        => ReferansAylikMaliyet is null && ReferansFiloYonetimMaliyeti is null
            ? null : (ReferansAylikMaliyet ?? 0m) + (ReferansFiloYonetimMaliyeti ?? 0m);

    /// <summary>Gelirin KDV dahil gösterimi (bilgi). KDV bilinmiyorsa null — Gelir'i "KDV dahil" diye
    /// göstermek yanıltır. <see cref="Gelir"/> DEĞİŞMEZ.</summary>
    public decimal? GelirKdvDahil => HesaplananKdv is { } k ? Gelir + k : null;
}

/// <summary>KDV gösterim modu (canlı "Kdv Durum"). SALT GÖSTERİM: Gelir/Gider/NetKar DEĞERLERİ hiçbir
/// modda değişmez; yalnız ekranda ayrıca KDV/KDV-dahil referans kolonlarının gösterilip gösterilmediğini
/// belirler (defter zaten net taşır, KDV ayrı hesaptadır).</summary>
public enum KdvDurum
{
    /// <summary>Varsayılan — defterdeki net tutarlar gösterilir.</summary>
    Kdvsiz = 0,
    /// <summary>Net tutarların YANINA hesaplanan KDV + KDV dahil referans kolonları eklenir.</summary>
    KdvDahil = 1
}

/// <summary>Dönem kârlılık raporu: araç/atanmamış satırları + genel toplamlar (defter Gelir/Gider ile mutabık).
/// <para>FAZ-79: <paramref name="ToplamPotansiyelGelir"/>/<paramref name="ToplamReferansMaliyet"/>/
/// <paramref name="ToplamHesaplananKdv"/> REFERANS toplamlarıdır — <paramref name="ToplamGelir"/>/
/// <paramref name="ToplamGider"/>/<paramref name="ToplamNetKar"/> ile mutabık olmaları BEKLENMEZ ve
/// birbirlerine EKLENMEZ.</para></summary>
public sealed record KarlilikDto(
    IReadOnlyList<KarlilikSatirDto> Satirlar, decimal ToplamGelir, decimal ToplamGider, decimal ToplamNetKar,
    KdvDurum KdvDurum = KdvDurum.Kdvsiz,
    decimal? ToplamPotansiyelGelir = null, decimal? ToplamReferansMaliyet = null,
    decimal? ToplamHesaplananKdv = null);

/// <summary>Çok-boyutlu kârlılık özeti — araç-bazlı P&amp;L'in bir boyuta (grup/şube/segment/otopark/SIPP/
/// rez. kaynağı) göre toplamı. referans sistem'in "araç gelir-gider tablosu × N boyut" paritesi:
/// araç=KarlilikDto, hizmet=EkHizmetRaporDto, boyut=bu.
/// <para>FAZ-79: <paramref name="AracBasiGelir"/> = Gelir ÷ AracAdet (yalnız pozitif paydayla);
/// <paramref name="DolulukYuzde"/> HAVUZ hesabıdır (Σ kiralanan ÷ Σ sahiplik) — satır yüzdelerinin
/// ortalaması DEĞİL (karışık-payda yasak). Referans kolonları P&amp;L'e katılmaz.</para></summary>
public sealed record KarlilikOzetSatirDto(
    string Boyut, int AracAdet, decimal Gelir, decimal Gider, decimal NetKar,
    decimal? AracBasiGelir = null, decimal? DolulukYuzde = null,
    decimal? PotansiyelGelir = null, decimal? ReferansToplamMaliyet = null);
public sealed record KarlilikOzetDto(
    string BoyutAdi, IReadOnlyList<KarlilikOzetSatirDto> Satirlar, decimal ToplamGelir, decimal ToplamGider, decimal ToplamNetKar);

// ---------- FAZ-79 — Karlılık çok-boyutlu genişleme (DEFTER-DIŞI referans hamı) ----------

/// <summary>Karlılık satırını zenginleştiren araç MASTER alanları (defter DEĞİL) + sahiplik penceresi
/// girdileri. Para alanları (<paramref name="AylikMaliyet"/>/<paramref name="FiloYonetimMaliyeti"/>)
/// yalnız REFERANS kolonuna akar.</summary>
public sealed record KarlilikAracMetaRow(
    Guid Id, string? Sipp, string? Otopark, string? GrupKod, string? SubeAdi,
    decimal? AylikMaliyet, decimal? FiloYonetimMaliyeti,
    DateTimeOffset? AlimTarihi, DateTimeOffset? FiloGirisTarih, DateTimeOffset? FiloCikisTarih,
    VehicleStatus Durum, DateTimeOffset? SonSatisTarih);

/// <summary>Karlılık satırının kira-türevli bilgi alanları (İptal hariç): doluluk günü + rez. kaynağı +
/// müşteri. Tutar TAŞIMAZ — kira tutarının P&amp;L'e sızma yolu bilinçli olarak kapalıdır.</summary>
public sealed record KarlilikKiraMetaRow(
    Guid VehicleId, DateTimeOffset Bas, DateTimeOffset Bit, string? Kaynak, Guid MusteriId);

/// <summary>Araca atfedilmiş SATIŞ-belgesi KDV'si (defterdeki Kdv hesabı, işaretli). VehicleId null =
/// atfedilemeyen. P&amp;L'e girmez; "KDV Dahil" gösterim modunun kaynağıdır.</summary>
public sealed record KarlilikKdvRow(Guid? VehicleId, decimal Kdv);

/// <summary>FAZ-79 ham paketi. <paramref name="Omur"/> pencereden BAĞIMSIZ Karlilik satırlarıdır
/// (Doluluk/RevPACD/ADR ömür-KPI paydaları için; from/to yoksa çağıran ile aynı liste).
/// <paramref name="Tarifeler"/> onay/aktiflik filtresi UYGULANMAMIŞ ham matris satırlarıdır —
/// eleme paylaşılan çözümleyicide (<c>RateMatrisCozumleme</c>) yapılır, ikinci bir kural yazılmaz.</summary>
public sealed record KarlilikEkRawDto(
    IReadOnlyList<KarlilikSatirDto> Omur,
    IReadOnlyList<KarlilikAracMetaRow> Araclar,
    IReadOnlyList<KarlilikKiraMetaRow> Kiralar,
    IReadOnlyList<KarlilikKdvRow> KdvSatirlari,
    IReadOnlyList<RateMatrix> Tarifeler);

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


// ---------------------------------------------------------------------------
// FAZ-75 — sigorta/muayene birleşik rapor (salt okuma; para toplamı YOK)
// ---------------------------------------------------------------------------

/// <summary>
/// Araç başına belge/vade özeti: Trafik + Kasko + MTV + Muayene + Z-izni/Seyrüsefer TEK satırda.
///
/// <para><b>Mevcut <c>/vade</c> panosunun yerine GEÇMEZ:</b> orası "yaklaşan vadeler" akışıdır
/// (kova + kalan gün), burası araç bazlı BELGE ENVANTERİDİR — her araç bir satır, boş belgeler de
/// görünür. İki farklı soru.</para>
/// </summary>
public sealed record SigortaMuayeneRow(
    Guid VehicleId, string Plaka, string? Marka, string? Tip, int? ModelYili,
    string? Yakit, string? Vites, string? Sube, string? Grup,
    string? SasiNo, string? MotorNo, string? AracSahibi, string? BelgeNo, string? Kimde,
    DateTimeOffset? TrafikBitis, DateTimeOffset? KaskoBitis,
    DateTimeOffset? MuayeneBitis, DateTimeOffset? MtvVade, bool MtvOdendi,
    bool ZIzni, DateTimeOffset? ZIzniBitis, DateTimeOffset? SeyrusiferBitis)
{
    /// <summary>Verilen türün bitişi (filtre/sıralama için) — yoksa null.</summary>
    public DateTimeOffset? Bitis(SigortaMuayeneTur tur) => tur switch
    {
        SigortaMuayeneTur.Trafik => TrafikBitis,
        SigortaMuayeneTur.Kasko => KaskoBitis,
        SigortaMuayeneTur.Muayene => MuayeneBitis,
        SigortaMuayeneTur.Mtv => MtvVade,
        SigortaMuayeneTur.ZIzni => ZIzniBitis,
        SigortaMuayeneTur.Seyrusefer => SeyrusiferBitis,
        _ => null
    };
}

/// <summary>Birleşik rapor tür filtresi.</summary>
public enum SigortaMuayeneTur
{
    Hepsi = 0, Trafik = 1, Kasko = 2, Muayene = 3, Mtv = 4, ZIzni = 5, Seyrusefer = 6
}

/// <summary>Birleşik rapor filtresi. <c>AracSahibi</c> boş = tümü.</summary>
public sealed class SigortaMuayeneFilter
{
    public SigortaMuayeneTur Tur { get; set; } = SigortaMuayeneTur.Hepsi;
    /// <summary>Araç sahibi (serbest metin, harf duyarsız). Canlıdaki "Bizim/Dış" ayrımı bu alandan.</summary>
    public string? AracSahibi { get; set; }
    public string? Plaka { get; set; }
    /// <summary>Yalnız seçilen türün bitişi bu tarihten ÖNCE olanlar (vade taraması).</summary>
    public DateTimeOffset? BitisEnGec { get; set; }
}
