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

/// <summary>Cari bakiye satırı. Pozitif = müşteri borçlu (alacağımız).</summary>
public sealed record CariBalanceDto(Guid CariId, string Ad, decimal Bakiye);

/// <summary>
/// Cari borç yaşlandırma (v1: BRÜT borç — tahsilat FIFO mahsubu yok). Borç (Debit) satırları
/// yaşa göre kovalanır. Toplam = kovaların toplamı (net bakiye DEĞİL).
/// </summary>
public sealed record AgingRowDto(
    Guid CariId, string Ad, decimal B0_30, decimal B31_60, decimal B61_90, decimal B90Plus, decimal Toplam);

/// <summary>SourceType kırılım kalemi (gelir/gider drill-down).</summary>
public sealed record GelirGiderKalemDto(string SourceType, decimal Tutar);

/// <summary>Filo durum dağılımı + aktif kira (operasyonel rapor).</summary>
public sealed record FleetUtilizationDto(
    int Toplam, int Stokta, int Musait, int Kirada, int Serviste, int Pasif, int Satildi, int AktifKira);

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

/// <summary>Periyodik servis (KM-bazlı bakım uyarısı) satırı — roadmap H1. KalanKm = SonrakiBakimKm − GuncelKm.</summary>
public sealed record PeriyodikServisRow(Guid VehicleId, string Plaka, int GuncelKm, int SonrakiBakimKm, int KalanKm);

/// <summary>Kira KM detay satırı — roadmap H1. KatedilenKm = DonusKm − CikisKm.</summary>
public sealed record KmDetayRow(
    Guid RentalId, string SozlesmeNo, string Plaka, int CikisKm, int DonusKm,
    int KatedilenKm, int KmLimit, int FazlaKm, decimal FazlaKmBedeli);

/// <summary>Rezervasyon kaynak özeti — roadmap H2. Kaynak başına adet/gün/ciro.</summary>
public sealed record RezervasyonKaynakRow(string Kaynak, int Adet, int ToplamGun, decimal ToplamCiro);

/// <summary>Fatura dönem satırı — roadmap H2. Vade/cari/tutar/durum.</summary>
public sealed record FaturaDonemRow(
    Guid InvoiceId, string No, DateTimeOffset Tarih, DateTimeOffset? VadeTarihi,
    string Cari, decimal GenelToplam, string Durum, bool IadeMi);

/// <summary>Araç durum-takip (gün kırılımı) satırı — roadmap H3. Bos = Toplam − Dolu − Bakim (≥0).</summary>
public sealed record AracDurumTakipRow(DateTimeOffset Gun, int ToplamArac, int Dolu, int Bakim, int Bos);

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
    Guid? VehicleId, string Plaka, string? Sube, string? Grup, decimal Gelir, decimal Gider, decimal NetKar);

/// <summary>Dönem kârlılık raporu: araç/atanmamış satırları + genel toplamlar (defter Gelir/Gider ile mutabık).</summary>
public sealed record KarlilikDto(
    IReadOnlyList<KarlilikSatirDto> Satirlar, decimal ToplamGelir, decimal ToplamGider, decimal ToplamNetKar);
