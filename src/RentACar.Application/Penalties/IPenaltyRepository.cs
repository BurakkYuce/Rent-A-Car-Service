using RentACar.Domain.Entities;

namespace RentACar.Application.Penalties;

public interface IPenaltyRepository
{
    Task<IReadOnlyList<Penalty>> ListAsync(CancellationToken ct = default);
    Task<Penalty?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>FAZ-60 — filtreli + bilgi kolonları çözülmüş liste (canlı ceza_listesi.aspx).</summary>
    Task<IReadOnlyList<PenaltyRow>> ListRowsAsync(PenaltyFilter? filter, CancellationToken ct = default);

    /// <summary>Bir kiraya bağlı cezalar (kira formu "Ceza/Geçişler" alt-sekmesi).</summary>
    Task<IReadOnlyList<Penalty>> ListByRentalAsync(Guid rentalId, CancellationToken ct = default);

    /// <summary>Bir cezanın kalemleri (sıraya göre).</summary>
    Task<IReadOnlyList<PenaltySatir>> ListSatirAsync(Guid penaltyId, CancellationToken ct = default);

    /// <summary>Bir cezanın ödeme geçmişi (kalem + sıra).</summary>
    Task<IReadOnlyList<PenaltyOdeme>> ListOdemeAsync(Guid penaltyId, CancellationToken ct = default);

    /// <summary>No boşluksuz tahsis edip ekler — başlık + kalemler TEK transaction.</summary>
    Task CreateAsync(Penalty penalty, IReadOnlyList<PenaltySatir> satirlar, CancellationToken ct = default);

    /// <summary>Durum geçişi (İptal vb.). Yansıtma için ReflectAsync, ödeme için PostOdemeAsync.</summary>
    Task<bool> UpdateAsync(Guid id, Action<Penalty> apply, CancellationToken ct = default);

    /// <summary>
    /// #286 adversarial M1 — kilitli durum geçişi (iptal). Yansıtma ve ödemeyle AYNI sırada kilit alır:
    /// <c>pg_advisory_xact_lock("ceza:{tenant}:{cezaId}")</c> → ceza satırı <c>FOR UPDATE</c>; <paramref name="apply"/>
    /// kilit altında okunan GÜNCEL satırla çağrılır (durum/ödenen yeniden denetlenir). Kayıt yoksa false.
    /// </summary>
    Task<bool> UpdateLockedAsync(Guid id, Action<Penalty> apply, CancellationToken ct = default);

    /// <summary>
    /// Yansıtma: cezayı Yansitildi'ye çevirir ve DENGELİ defter kümesini yazar — TEK
    /// transaction. Satır kilidiyle (FOR UPDATE) idempotenttir (zaten yansıtılmışsa false).
    /// </summary>
    Task<bool> ReflectAsync(
        Guid id, Func<Penalty, IReadOnlyList<AccountLedgerEntry>> buildEntries, CancellationToken ct = default);

    /// <summary>
    /// FAZ-60 KISMİ ÖDEME — bir KALEME ödeme yazar ve dengeli defter çiftini postlar.
    ///
    /// <para>Tümü tek transaction ve <c>pg_advisory_xact_lock("ceza:{tenant}:{cezaId}")</c>
    /// arkasında: kalan okuma + aşım çiti + sıra atama + yazma. Kilitsiz "önce oku sonra yaz"
    /// TOCTOU'dur ve bu repoda tam bu sınıf hata canlı para hatası üretmişti.</para>
    ///
    /// <para><paramref name="posting"/> delegesi (ceza, kalem, kalemin GERÇEK kalanı, ödeme
    /// sırası) alır ve (ödeme satırı, defter kümesi) döndürür. Kalan, önbellek kolonundan
    /// DEĞİL, ödeme satırları toplanarak hesaplanır — önbellek bozulsa bile aşım imkânsız.</para>
    /// </summary>
    /// <remarks>F1.4: <paramref name="islemAnahtari"/> verilirse kilidin arkasında, kalan kontrolünden
    /// ÖNCE aranır; varsa <c>MukerrerIslemException</c> (sonuç ilk ödemenin tam/kısmi olmasına bağlı değil).</remarks>
    Task<CezaOdemeSonuc> PostOdemeAsync(
        Guid penaltyId, Guid satirId,
        Func<Penalty, PenaltySatir, decimal, int, (PenaltyOdeme Odeme, IReadOnlyList<AccountLedgerEntry> Entries)> posting,
        CancellationToken ct = default, Guid? islemAnahtari = null);
}
