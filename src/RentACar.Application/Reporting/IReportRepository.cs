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

    /// <summary>
    /// FAZ-62 — cari kart bilgileri (telefon/mail/banka/döviz/özel kod/sınıf). Bakiye raporunun
    /// kolon ve filtre ihtiyacı için; defter matematiğinden AYRI tutulur ki bakiye hesabı kart
    /// alanlarındaki bir değişiklikten etkilenmesin.
    /// </summary>
    Task<IReadOnlyList<CariKartDto>> GetCariKartlariAsync(CancellationToken ct = default);

    /// <summary>
    /// FAZ-61 — extre özeti satırları: fatura × cari × kira × araç. İptal faturalar HARİÇ.
    /// Tutar BRÜT (fatura-bazlı tahsilat mahsubu sistemde yok — bkz. <see cref="ExtreOzetiRowDto"/>).
    /// </summary>
    Task<IReadOnlyList<ExtreOzetiRowDto>> GetExtreOzetiRowsAsync(
        ExtreOzetiFilter? filter, DateTimeOffset asOf, CancellationToken ct = default);

    /// <summary>
    /// FAZ-68 — tahsilat raporu satır modu: sözleşme başına borç/faturalanan/tahsilat mutabakatı.
    /// Dönem-toplamı modu (<see cref="GetTahsilatFaturaAsync"/>) DEĞİŞMEZ, bu ayrı bir görünümdür.
    /// </summary>
    Task<IReadOnlyList<TahsilatMutabakatRowDto>> GetTahsilatMutabakatRowsAsync(
        TahsilatMutabakatFilter? filter, CancellationToken ct = default);

    /// <summary>
    /// FAZ-78 — ek hizmet SATIR-BAZLI detay (kira/araç/müşteri/personel çözümlenmiş).
    /// Mevcut ÖZET sorgusu <see cref="GetEkHizmetSalesRowsAsync"/> DEĞİŞMEZ; bu ayrı bir yoldur.
    /// </summary>
    Task<IReadOnlyList<EkHizmetDetayRow>> GetEkHizmetDetayRowsAsync(
        EkHizmetDetayFilter? filter, CancellationToken ct = default);

    /// <summary>
    /// FAZ-27 — karşılaştırmalı hacim pivotu (kira/rezervasyon × adet/gün × kırılım × ay).
    /// Tutar üretmez.
    /// </summary>
    Task<KarsilastirmaliAnalizDto> GetKarsilastirmaliAnalizAsync(
        KarsilastirmaliAnalizFilter filter, CancellationToken ct = default);

    /// <summary>
    /// FAZ-75 — araç başına belge/vade envanteri (Trafik/Kasko/MTV/Muayene + araç master).
    /// TÜM araçlar döner (belgesiz araç da satır alır — eksik belge görünmelidir).
    /// </summary>
    Task<IReadOnlyList<SigortaMuayeneRow>> GetSigortaMuayeneRowsAsync(CancellationToken ct = default);

    /// <summary>Tüm araçların durumları (filo dağılımı için).</summary>
    Task<IReadOnlyList<VehicleStatus>> GetVehicleStatusesAsync(CancellationToken ct = default);

    /// <summary>
    /// İptal olmayan, [from,to] dönemiyle çakışan kiraların efektif aralıkları (Bas + gerçek/planlı
    /// bitiş). Doluluk raporu için. Çakışma: efektifBitiş >= from AND Bas &lt;= to.
    /// </summary>
    Task<IReadOnlyList<DolulukKiraRowDto>> GetRentalIntervalsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>
    /// FAZ-77 — şube kırılımlı filo raporu hamı. Kira/rezervasyon/BAF satırları ARACIN şubesine
    /// göre etiketlenir (tek atıf kuralı, bkz. <see cref="FiloSubeRow"/>).
    /// </summary>
    Task<FiloSubeHamPaket> GetFiloSubeHamAsync(
        DateTimeOffset pencereBas, DateTimeOffset pencereBit, CancellationToken ct = default);

    /// <summary>
    /// FAZ-77 — gün-kırılımlı doluluk hamı: araç envanteri (payda) + dönemle çakışan kira ve
    /// rezervasyon aralıkları, şube/grup/kaynak atıflarıyla. <see cref="GetRentalIntervalsAsync"/>
    /// olduğu gibi durur (geriye uyum).
    /// </summary>
    Task<DolulukAtifPaket> GetDolulukAtifAsync(
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
    Task<IReadOnlyList<PeriyodikServisRow>> GetPeriyodikServisRowsAsync(
        PeriyodikServisFilter? filtre = null, CancellationToken ct = default);

    /// <summary>Dönmüş kiraların (CikisKm+DonusKm dolu) KM detayı (roadmap H1).</summary>
    Task<IReadOnlyList<KmDetayRow>> GetKmDetayRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);

    /// <summary>Rezervasyon kaynak özeti (roadmap H2): BasTar [from,to] filtreli, kaynak başına agrega.</summary>
    Task<IReadOnlyList<RezervasyonKaynakRow>> GetRezervasyonKaynakRowsAsync(
        RezervasyonKaynakFilter filtre, CancellationToken ct = default);

    /// <summary>Fatura dönem listesi (roadmap H2): Tarih [from,to] filtreli, cari adıyla.</summary>
    Task<IReadOnlyList<FaturaDonemRow>> GetFaturaDonemRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);

    /// <summary>Araç durum-takip (roadmap H3): [from,to] her gün için dolu/bakım/boş sayısı (gün kırılımı).</summary>
    Task<IReadOnlyList<AracDurumTakipRow>> GetAracDurumTakipRowsAsync(
        DateTimeOffset from, DateTimeOffset to, string? sube = null, CancellationToken ct = default);

    /// <summary>Müşteri segment (roadmap N3): kira sayısı/ciro/son işlem (kiralardan agrega).</summary>
    Task<IReadOnlyList<MusteriSegmentRow>> GetMusteriSegmentRowsAsync(CancellationToken ct = default);

    /// <summary>Personel çalışma (roadmap N3): personel başına BAF (araç tahsis) sayısı.</summary>
    Task<IReadOnlyList<PersonelCalismaRow>> GetPersonelCalismaRowsAsync(CancellationToken ct = default);

    /// <summary>
    /// Bir günün ([from,to]) operasyonel faaliyet sayaçları + tutarları (yeni rezervasyon/kira,
    /// çıkış/dönüş, tahsilat, fatura). Günlük faaliyet raporu için.
    /// </summary>
    Task<GunlukFaaliyetDto> GetGunlukFaaliyetAsync(
        DateTimeOffset from, DateTimeOffset to, string? sube = null, CancellationToken ct = default);

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

    /// <summary>
    /// Araç karnesi ham paketi: TEK araca scope'lu defter Gelir/Gider satırları (Karlilik atfıyla birebir
    /// aynı kurallar: fark/iade KaynakKiraId, ServisYansitma, Ceza RentalId-fallback) + kaynak-varlık olay
    /// zaman çizelgesi + KPI hamı (kira/servis aralıkları, katedilen km). Vehicle null = bulunamadı/başka
    /// tenant. from/to EKSENLERİ FARKLIDIR (bilinçli): defter satırları EntryDateUtc (ödeme/fatura tarihi),
    /// olaylar KAYNAK tarihi (MTV=Vade, poliçe=Başlangıç, kira=BasTar) — pencere P&L'de para gösterip olayı
    /// gizleyebilir (ör. 2025 vadeli MTV 2026'da ödendi). KPI ham alanları (aralıklar/KiraSayisi/katedilenKm/
    /// SonSatisTarih) pencereden BAĞIMSIZ tüm geçmiştir — KPI'lar sahiplik-penceresi (ömür boyu) metrikleridir.
    /// </summary>
    Task<AracKarneRawDto> GetAracKarneRawAsync(
        Guid vehicleId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);

    /// <summary>
    /// Filo analiz hamı: dönem-pencereli + ömür-boyu Karlilik satırları (aynı atıf kuralları) + araç KPI
    /// alanları (pencere/satış) + İptal-dışı kira aralıkları. Gün/KPI matematiği ReportService'te.
    /// </summary>
    Task<FiloAnalizRawDto> GetFiloAnalizRawAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);
}
