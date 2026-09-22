using RentACar.Domain.Entities;

namespace RentACar.Application.Finance;

/// <summary>Toplu nakit işleminde tek satır: belge + dengeli defter kümesi. Kira tahsilat deltası artık
/// repo'da tx'ten türetilir (Tip × TersKayitMi yönü + kira dövizi birimi — denetim K2).</summary>
public sealed record CashPosting(
    CashTransaction Tx, IReadOnlyList<AccountLedgerEntry> Entries);

public interface ICashRepository
{
    Task<IReadOnlyList<CashTransaction>> ListAsync(CancellationToken ct = default);

    /// <summary>FAZ-67 — süzgeçli nakit işlem listesi; cari adı/özel kodu çözümlenmiş.</summary>
    Task<IReadOnlyList<NakitIslemSatirDto>> SearchIslemlerAsync(
        CashFilter? filter = null, CancellationToken ct = default);
    Task<CashTransaction?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Verilen işlemin zaten bir ters kaydı var mı? (idempotency).</summary>
    Task<bool> HasReversalAsync(Guid originalId, CancellationToken ct = default);

    /// <summary>F1.4 — bu kiracıda verilen <c>IslemAnahtari</c> ile yazılmış bir kasa/banka işlemi var mı?
    /// Anahtarlı çift gönderimin sonucunu tutara/zamanlamaya bağlı olmaktan çıkarmak için servis ön-kontrolü
    /// (tahsis/bakiye çitlerinden ÖNCE) kullanır.</summary>
    Task<bool> IslemAnahtariVarMiAsync(Guid islemAnahtari, CancellationToken ct = default);

    /// <summary>F4.4a — verilen <c>IslemAnahtari</c> ile yazılmış kasa/banka işlemi (yoksa null). Deterministik
    /// anahtarın "zaten kaydedilmiş" dalında kaydın GERÇEKTEN aynı işleme (ör. aynı kiranın dönem tahsilatına)
    /// ait olduğunu doğrulamak için.</summary>
    Task<CashTransaction?> FindByIslemAnahtariAsync(Guid islemAnahtari, CancellationToken ct = default);

    /// <summary>
    /// Belge + DENGELİ defter kümesi + (kira bağlıysa) Tahsilat/Bakiye'yi TEK transaction'da işler. Kira
    /// tahsilat deltası tx'ten türetilir: yön = Tip(Tahsilat:+/Ödeme:−) × TersKayitMi(−); birim = kira dövizi
    /// (TRY kira → TL-baz AmountInBase; FX kira → tahsilat AYNI dövizde zorunlu, ham Amount — K2). Güncelleme
    /// ATOMİK SQL (Tahsilat = Tahsilat + delta — eşzamanlı tahsilatta kayıp yok, O1). No boşluksuz tahsis
    /// edilir. Defter kayıtları immutable (DB trigger).
    /// </summary>
    Task PostAsync(
        CashTransaction tx, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default);

    /// <summary>
    /// Toplu nakit işlemi: çok satır dengeli kayıt + No tahsisi TEK transaction'da (ATOMİK hep-ya-hiç).
    /// Bir satır geçersiz/çakışırsa hiçbiri yazılmaz; No boşluğu oluşmaz. IslemAnahtari kısmi unique index
    /// → aynı toplu işlemin çift-submit'i UniqueViolation ile tüm batch'i geri alır (idempotent).
    /// </summary>
    Task PostBatchAsync(IReadOnlyList<CashPosting> items, CancellationToken ct = default);

    /// <summary>Cari bakiye (yerel para) = Σ (Borç +, Alacak −). Pozitif = müşteri borçlu.</summary>
    Task<decimal> GetCariBalanceAsync(Guid cariId, CancellationToken ct = default);

    /// <summary>Cari'nin elde tutulan depozito bakiyesi (roadmap I3): Σ Depozito (Alacak:+ Borç:−) = tutulan tutar.</summary>
    Task<decimal> GetDepozitoBakiyeAsync(Guid cariId, CancellationToken ct = default);

    /// <summary>Depozito işlemi (Al/İade/Mahsup/İrat) — TEK transaction + (tenant,cari) danışma kilidi.
    /// kontrolEt: bakiye kontrolü KİLİDİN ARKASINDA tx-İÇİNDE yapılır (TOCTOU çiti — adversarial 1.2 Medium:
    /// eşzamanlı iki işlem tutulanı aşamaz). izKaydi (yalnız İrat) verilirse aynı tx'te yazılır; RentalId
    /// çiti (kira bu carinin olmalı) tx içinde. Çift-submit: TENANT-GÖRÜNÜR kayıt varsa sessiz no-op
    /// (I3 sözleşmesi); görünmüyorsa (çapraz-tenant PK çakışması) NET RED — sessiz para kaybı yok.</summary>
    /// <summary>
    /// FAZ-29 — tek cari toplu kapatma: tahsilat + defter + <b>kapatma tahsisleri</b> TEK
    /// transaction'da, <c>(tenant, cari)</c> danışma kilidi ARKASINDA yazılır.
    ///
    /// <para><b>Kilit şart (adversarial H2):</b> kilitsiz sürümde 8 eşzamanlı kapatma çiti geçip
    /// 8 tahsilat yazmış, bakiye −7000'e düşmüştü. Bakiye ve tahsis kontrolleri bu yüzden çağıranda
    /// DEĞİL, burada — kilidin arkasında ve aynı tx içinde — tekrar yapılır.</para>
    /// </summary>
    Task PostCariKapatmaAsync(
        Guid cariId, CashTransaction tx, IReadOnlyList<AccountLedgerEntry> entries,
        IReadOnlyList<KapatmaTahsis> tahsisler, CancellationToken ct = default);

    /// <summary>Verilen fatura id'leri için (fatura → kira) eşlemesi; kirası olmayan fatura
    /// sözlükte YOKTUR (kapatma tahsilatının kira bağını çözmek için).</summary>
    Task<Dictionary<Guid, Guid>> FaturaKiralariAsync(
        IReadOnlyCollection<Guid> faturaIds, CancellationToken ct = default);

    /// <summary>Verilen borç satırları için ŞU ANA KADAR tahsis edilmiş baz tutarlar
    /// (satırId → kapatılan). Ekran "kapalı/kısmi" göstermek, servis çit kurmak için kullanır.</summary>
    Task<Dictionary<Guid, decimal>> GetTahsisToplamlariAsync(
        IReadOnlyCollection<Guid> ledgerEntryIds, CancellationToken ct = default);

    Task PostDepozitoIslemAsync(Guid cariId, bool kontrolEt, DepozitoIrat? izKaydi,
        IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default);

    /// <summary>Cari hesap ekstresi (kronolojik defter satırları).</summary>
    /// <summary>
    /// Cari ekstresi. <paramref name="filter"/> null/boş → carinin TÜM hareketleri (eski davranış).
    /// Filtre verilirse görünen satırlar daralır ve <see cref="CariEkstreSonuc.Devir"/> kapsam
    /// dışında kalan ÖNCEKİ hareketlerin net toplamıyla dolar (yürüyen bakiye doğru devam etsin).
    /// </summary>
    /// <summary>
    /// FAZ-59 — cari↔cari virman geçmişi: künye tablosu + DEFTERDEN okunan tutar (tek kaynak).
    /// </summary>
    /// <summary>FAZ-50 — kasa/banka virman geçmişi (künye + defterden tutar).</summary>
    Task<IReadOnlyList<KasaVirmanSatirDto>> ListKasaVirmanlarAsync(
        KasaVirmanFilter? filter = null, CancellationToken ct = default);

    Task<IReadOnlyList<CariVirmanSatirDto>> ListCariVirmanlarAsync(
        CariVirmanFilter? filter = null, CancellationToken ct = default);

    Task<CariEkstreSonuc> GetCariStatementAsync(
        Guid cariId, CariEkstreFilter? filter = null, CancellationToken ct = default);

    /// <summary>Kira başına kasa/banka işlem SAYISI (ters kayıtlar DAHİL → monoton artan sayaç).
    /// Deterministik tahsilat idempotency anahtarının zamansal bileşeni: yalnız (kira, bakiye)
    /// snapshot'ı aylık kirada aynı değere geri dönüp meşru tahsilatı kilitler; sayaç bunu kırar.
    /// Kayıtsız kira sözlükte YER ALMAZ — tüketici TryGetValue→0 kullanmalı.</summary>
    Task<Dictionary<Guid, int>> GetRentalIslemSayilariAsync(IReadOnlyCollection<Guid> rentalIds, CancellationToken ct = default);
}
