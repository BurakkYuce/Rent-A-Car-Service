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
