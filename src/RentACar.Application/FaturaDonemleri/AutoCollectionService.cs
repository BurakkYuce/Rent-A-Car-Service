using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Application.FaturaDonemleri;

/// <summary>Elle tetikleme sonucu — kaç dönem kesildi, kaç tahsilat yazıldı, neler atlandı.</summary>
public sealed record OtomatikTahsilatSonuc(int Kesilen, int Tahsilat, IReadOnlyList<string> Atlananlar);

/// <summary>
/// FAZ-30 — <b>dönemsel faturalama/tahsilatın ELLE tetiklenmesi.</b> Kullanıcı vadesi gelmiş
/// dönemleri listeler, işaretler ve tek tıkla çalıştırır.
///
/// <para><b>AYARDAN BAĞIMSIZ (kullanıcı kararı):</b> Ayarlar'daki <c>DonemselOtomatikTahsilat</c>
/// anahtarı "her gece kendiliğinden çalışsın mı" sorusunun cevabıdır; kullanıcı ekranda sözleşmeyi
/// seçip açıkça tıkladığında niyet nettir. Bu yüzden elle tetik o anahtara BAKMAZ — ekran yalnız
/// bilgi amaçlı "otomatik job kapalı" uyarısı gösterir.</para>
///
/// <para><b>JOB ÇEKİRDEĞİ KULLANILMAZ — güvenlik gerekçesi:</b> <c>DonemFaturaUretici</c> bir
/// JOB'dur ve bilinçli olarak <see cref="PermissionGuard"/>'sız çalışır (kimliksiz arka plan
/// bağlamı). Onu kullanıcı-yüzeyli bir ekrandan çağırmak yetki kontrolünü BAYPAS ederdi. Bunun
/// yerine manuel yol olan <see cref="PeriodCollectionService.IssueAndCollectAsync"/> kullanılır: guard'lı,
/// idempotent (fatura Kesildi→mevcut; tahsilat deterministik <c>RowKey(rentalId, donemSira)</c>
/// anahtarı) ve para matematiği job ile TEK KOPYA.</para>
///
/// <para><b>Sözleşme-bazlı yürütme:</b> her dönem TEK TEK çalıştırılır; birinin hatası diğerlerini
/// durdurmaz (sonuçta "kaç başarılı / kaç atlandı" döner). Toplu tek-transaction olsaydı tek bir
/// bozuk sözleşme tüm partiyi geri alırdı.</para>
/// </summary>
public sealed class AutoCollectionService(
    IInvoicePeriodRepository repository, PeriodCollectionService periodCollection, ICurrentUser currentUser)
{
    private readonly IInvoicePeriodRepository _repository = repository;
    private readonly PeriodCollectionService _periodCollection = periodCollection;
    private readonly ICurrentUser _currentUser = currentUser;

    /// <summary>Tek seferde çalıştırılabilecek en fazla dönem — kazara "hepsini seç" ile
    /// yüzlerce tahsilat postlanmasın (toplu para yazan yüzeylerdeki 500 sınırıyla aynı ruh).</summary>
    public const int MaxSelection = 200;

    /// <summary>Beklenmeyen (doğrulama dışı) hatada atlananlar listesine yazılan genel metin.</summary>
    public const string UnexpectedErrorMessage = "beklenmeyen bir hata oluştu; bu dönem işlenmedi, daha sonra yeniden deneyin.";

    /// <summary>
    /// Aday listesinin DÖVİZ KIRILIMLI toplamı (adversarial L1). Native tutarları tek sayıda
    /// toplamak (EUR + TRY) anlamsız bir rakam üretiyordu; ekran bu saf kuralı kullanır ki
    /// gösterim ile test aynı şeyi konuşsun.
    /// </summary>
    public static IReadOnlyList<(string Doviz, decimal Toplam)> CurrencyTotals(
        IEnumerable<OtomatikTahsilatAdayi> candidates)
        => [.. candidates.GroupBy(a => a.Doviz)
                .Select(g => (Doviz: g.Key, Toplam: g.Sum(x => x.KiraTutar)))
                .OrderBy(x => x.Doviz, StringComparer.Ordinal)];

    /// <summary>
    /// Atlananların kullanıcıya gidecek hâli (adversarial M3): ilk <paramref name="limit"/> satır +
    /// gizlenen varsa SAYISINI söyleyen bir satır. Sadece kesmek "atlanan yok" gibi okunuyordu;
    /// URL uzunluk sınırı yüzünden tamamı da gönderilemiyor.
    /// </summary>
    public static IReadOnlyList<string> ShowSkipped(IReadOnlyList<string> all, int limit = 10)
    {
        if (all.Count <= limit) return all;
        var g = all.Take(limit).ToList();
        g.Add($"… ve {all.Count - limit} kayıt daha (toplam {all.Count}).");
        return g;
    }

    /// <summary>Aday listesi. Salt okuma ama para bilgisi (cari bakiye) taşıdığı için
    /// <see cref="Permission.FinanceWrite"/> istenir — ekranın kendisi zaten finans rolüne kapalı.</summary>
    public async Task<IReadOnlyList<OtomatikTahsilatAdayi>> CandidatesAsync(
        OtomatikTahsilatFiltre? filter = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        // KOPYA kurulur: çağıranın nesnesini mutasyona uğratmak (adversarial L3) sayfa yeniden
        // kullandığında sessizce kapsam sızdırabilirdi.
        var k = filter ?? new OtomatikTahsilatFiltre();
        var scope = BranchScope.EffectiveFilter(_currentUser);
        var f = new OtomatikTahsilatFiltre
        {
            SozlesmeNo = k.SozlesmeNo, VadeMin = k.VadeMin, VadeMax = k.VadeMax,
            SadeceBakiyeli = k.SadeceBakiyeli,
            // Şube kapsamı DAİMA uygulanır (BranchScope tek kural; bugün yalnız Operatör'ü kısıtlar).
            SubeIdler = scope.SubeId is { } sid ? [sid] : null,
            SubeAdi = scope.SubeAd
        };
        return await _repository.CandidatesAsync(f, ct);
    }

    /// <summary>
    /// Seçili dönemleri çalıştırır. <paramref name="doCollection"/> false ise yalnız fatura kesilir.
    /// </summary>
    public async Task<OtomatikTahsilatSonuc> RunAsync(
        IReadOnlyCollection<(Guid RentalId, int DonemSira)> selection, bool doCollection,
        LedgerAccountType account, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (selection.Count == 0) throw new ValidationException("En az bir dönem seçilmelidir.");
        if (selection.Count > MaxSelection)
            throw new ValidationException($"Tek seferde en çok {MaxSelection} dönem çalıştırılabilir.");
        if (account is not (LedgerAccountType.Kasa or LedgerAccountType.Banka))
            throw new ValidationException("Hesap Kasa ya da Banka olmalıdır.");

        // ADAY ÇİTİ: yalnız kullanıcının GÖREBİLDİĞİ (şube kapsamı + vadesi gelmiş + Planlandi)
        // dönemler çalıştırılabilir. Uydurma bir POST ile kapsam dışı sözleşme tetiklenemesin.
        var candidateList = await CandidatesAsync(null, ct);
        var candidates = candidateList.ToDictionary(a => (a.RentalId, a.DonemSira), a => a.SozlesmeNo);

        var issued = 0; var collection = 0;
        var skipped = new List<string>();
        foreach (var (rentalId, periodSequence) in selection.Distinct())
        {
            // Mesajlar SÖZLEŞME NO taşır (adversarial M2): çok sözleşmeli çalıştırmada "Dönem 2"
            // yazan iki satır birbirinden ayırt edilemiyordu.
            if (!candidates.TryGetValue((rentalId, periodSequence), out var sozNo))
            {
                // Sessizce atlamak, kullanıcının "çalıştı" sanmasına yol açardı — listeye yazılır.
                skipped.Add($"Dönem {periodSequence}: kapsam dışı ya da artık kesilebilir değil (liste bayat olabilir).");
                continue;
            }
            try
            {
                var (_, written) = await _periodCollection.IssueAndCollectDetailAsync(
                    rentalId, periodSequence, doCollection, account, ct);
                issued++;
                // Sayaç GERÇEĞİ söyler (adversarial M1): idempotent yutulan tahsilat sayılmaz.
                if (written) collection++;
                else if (doCollection)
                    skipped.Add($"{sozNo} — Dönem {periodSequence}: tahsilat daha önce alınmış, tekrar yazılmadı.");
            }
            // Job çekirdeğiyle AYNI genişlik (adversarial M4): beklenmedik bir hata partiyi ortada
            // bırakıp 500 vermemeli — önceki dönemler zaten commit'li, kullanıcı ne yazıldığını görmeli.
            catch (ValidationException ex)
            {
                skipped.Add($"{sozNo} — Dönem {periodSequence}: {ex.Message}");
            }
            // F8.1a adversarial L5: beklenmeyen hatanın HAM mesajı (veritabanı/iç ayrıntı) kullanıcıya dönmez.
            // Application katmanında ILogger yok; ayrıntı Trace'e yazılır (host dinleyicisi loglar).
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                skipped.Add($"{sozNo} — Dönem {periodSequence}: {UnexpectedErrorMessage}");
                System.Diagnostics.Trace.TraceError($"OtomatikTahsilat {rentalId}/{periodSequence}: {ex}");
            }
        }
        return new OtomatikTahsilatSonuc(issued, collection, skipped);
    }
}
