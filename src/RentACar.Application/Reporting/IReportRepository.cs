using RentACar.Domain.Enums;

namespace RentACar.Application.Reporting;

/// <summary>
/// Salt-okunur defter sorgusu (raporlama için). Verilen hesap türleri + tarih aralığındaki
/// AccountLedgerEntry satırlarını base tutarıyla (Amount×Rate) düz DTO olarak döndürür.
/// Tenant izolasyonu DbContext/RLS ile otomatiktir.
/// </summary>
public interface IReportRepository
{
    Task<IReadOnlyList<LedgerRowDto>> GetLedgerRowsAsync(
        IReadOnlyCollection<LedgerAccountType> accountTypes,
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);

    /// <summary>
    /// Cari (AccountType=Cari) defter satırları, cari adı çözümlenmiş (Customers join, bellek-içi
    /// DisplayName). <paramref name="asOf"/> verilirse o tarihe kadar. Bakiye + yaşlandırma için.
    /// </summary>
    Task<IReadOnlyList<CariLedgerRowDto>> GetCariLedgerRowsAsync(
        DateTimeOffset? asOf, CancellationToken ct = default);

    /// <summary>Tüm araçların durumları (filo dağılımı için).</summary>
    Task<IReadOnlyList<VehicleStatus>> GetVehicleStatusesAsync(CancellationToken ct = default);

    /// <summary>
    /// İptal olmayan, [from,to] dönemiyle çakışan kiraların efektif aralıkları (Bas + gerçek/planlı
    /// bitiş). Doluluk raporu için. Çakışma: efektifBitiş >= from AND Bas &lt;= to.
    /// </summary>
    Task<IReadOnlyList<DolulukKiraRowDto>> GetRentalIntervalsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
    /// Dönem ([from,to]) tahsilat-fatura mutabakatı: fatura (İptal hariç, GenelToplam×Kur) ve
    /// tahsilat (ters kayıt hariç, Amount×Rate) adet+toplamları. Fark service'te hesaplanır.
    /// </summary>
    Task<TahsilatFaturaDto> GetTahsilatFaturaAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);

    /// <summary>Aktif (Kirada) kira sözleşmesi sayısı.</summary>
    Task<int> GetActiveRentalCountAsync(CancellationToken ct = default);

    /// <summary>
    /// Tamamlanmış servis kayıtları (CikisTarihi aralığında), araç plakası çözümlenmiş.
    /// Servis maliyet özeti için ham satırlar.
    /// </summary>
    Task<IReadOnlyList<ServiceCostRowDto>> GetServiceCostRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);

    /// <summary>Periyodik servis: her aracın en yüksek SonrakiBakimKm'si + güncel km (roadmap H1).</summary>
    Task<IReadOnlyList<PeriyodikServisRow>> GetPeriyodikServisRowsAsync(CancellationToken ct = default);

    /// <summary>Dönmüş kiraların (CikisKm+DonusKm dolu) KM detayı (roadmap H1).</summary>
    Task<IReadOnlyList<KmDetayRow>> GetKmDetayRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);

    /// <summary>Rezervasyon kaynak özeti (roadmap H2): BasTar [from,to] filtreli, kaynak başına agrega.</summary>
    Task<IReadOnlyList<RezervasyonKaynakRow>> GetRezervasyonKaynakRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);

    /// <summary>Fatura dönem listesi (roadmap H2): Tarih [from,to] filtreli, cari adıyla.</summary>
    Task<IReadOnlyList<FaturaDonemRow>> GetFaturaDonemRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);

    /// <summary>Araç durum-takip (roadmap H3): [from,to] her gün için dolu/bakım/boş sayısı (gün kırılımı).</summary>
    Task<IReadOnlyList<AracDurumTakipRow>> GetAracDurumTakipRowsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>
    /// Bir günün ([from,to]) operasyonel faaliyet sayaçları + tutarları (yeni rezervasyon/kira,
    /// çıkış/dönüş, tahsilat, fatura). Günlük faaliyet raporu için.
    /// </summary>
    Task<GunlukFaaliyetDto> GetGunlukFaaliyetAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>
    /// İptal olmayan faturaların satırları (Invoice.Tarih aralığında), base para (× Kur) tutarlarıyla.
    /// KDV oranı bazlı dönem kırılımı (KDV listesi raporu) için ham satırlar.
    /// </summary>
    Task<IReadOnlyList<KdvLineRowDto>> GetKdvLineRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);

    /// <summary>
    /// İptal olmayan kiraların ek hizmet kalemleri (RentalAddOn.CreatedAtUtc aralığında), base para.
    /// Ek hizmet satış raporu (Extralar_Raporu) için ham satırlar.
    /// </summary>
    Task<IReadOnlyList<EkHizmetSalesRowDto>> GetEkHizmetSalesRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);

    /// <summary>
    /// Araç-bazlı kârlılık (roadmap B2): DEFTERDEN. Gider(Debit) AccountRef=araç → araç gideri; Gelir(Credit)
    /// base, SourceId→Fatura→Kira→Araç ile atfedilir. Araca bağlanamayan gelir/gider null VehicleId
    /// "(Atanmamış)" satırına toplanır. Σ satır Gelir/Gider = defter Gelir/Gider toplamı (invariant).
    /// </summary>
    Task<IReadOnlyList<KarlilikSatirDto>> GetKarlilikRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);
}
