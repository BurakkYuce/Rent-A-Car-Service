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
/// yerine manuel yol olan <see cref="DonemTahsilatService.KesVeTahsilEtAsync"/> kullanılır: guard'lı,
/// idempotent (fatura Kesildi→mevcut; tahsilat deterministik <c>RowKey(rentalId, donemSira)</c>
/// anahtarı) ve para matematiği job ile TEK KOPYA.</para>
///
/// <para><b>Sözleşme-bazlı yürütme:</b> her dönem TEK TEK çalıştırılır; birinin hatası diğerlerini
/// durdurmaz (sonuçta "kaç başarılı / kaç atlandı" döner). Toplu tek-transaction olsaydı tek bir
/// bozuk sözleşme tüm partiyi geri alırdı.</para>
/// </summary>
public sealed class OtomatikTahsilatService(
    IFaturaDonemRepository repository, DonemTahsilatService donemTahsilat, ICurrentUser currentUser)
{
    private readonly IFaturaDonemRepository _repository = repository;
    private readonly DonemTahsilatService _donemTahsilat = donemTahsilat;
    private readonly ICurrentUser _currentUser = currentUser;

    /// <summary>Tek seferde çalıştırılabilecek en fazla dönem — kazara "hepsini seç" ile
    /// yüzlerce tahsilat postlanmasın (toplu para yazan yüzeylerdeki 500 sınırıyla aynı ruh).</summary>
    public const int MaxSecim = 200;

    /// <summary>
    /// Aday listesinin DÖVİZ KIRILIMLI toplamı (adversarial L1). Native tutarları tek sayıda
    /// toplamak (EUR + TRY) anlamsız bir rakam üretiyordu; ekran bu saf kuralı kullanır ki
    /// gösterim ile test aynı şeyi konuşsun.
    /// </summary>
    public static IReadOnlyList<(string Doviz, decimal Toplam)> DovizToplamlari(
        IEnumerable<OtomatikTahsilatAdayi> adaylar)
        => [.. adaylar.GroupBy(a => a.Doviz)
                .Select(g => (Doviz: g.Key, Toplam: g.Sum(x => x.KiraTutar)))
                .OrderBy(x => x.Doviz, StringComparer.Ordinal)];

    /// <summary>
    /// Atlananların kullanıcıya gidecek hâli (adversarial M3): ilk <paramref name="limit"/> satır +
    /// gizlenen varsa SAYISINI söyleyen bir satır. Sadece kesmek "atlanan yok" gibi okunuyordu;
    /// URL uzunluk sınırı yüzünden tamamı da gönderilemiyor.
    /// </summary>
    public static IReadOnlyList<string> AtlananGoster(IReadOnlyList<string> hepsi, int limit = 10)
    {
        if (hepsi.Count <= limit) return hepsi;
        var g = hepsi.Take(limit).ToList();
        g.Add($"… ve {hepsi.Count - limit} kayıt daha (toplam {hepsi.Count}).");
        return g;
    }

    /// <summary>Aday listesi. Salt okuma ama para bilgisi (cari bakiye) taşıdığı için
    /// <see cref="Permission.FinanceWrite"/> istenir — ekranın kendisi zaten finans rolüne kapalı.</summary>
    public async Task<IReadOnlyList<OtomatikTahsilatAdayi>> AdaylarAsync(
        OtomatikTahsilatFiltre? filtre = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        // KOPYA kurulur: çağıranın nesnesini mutasyona uğratmak (adversarial L3) sayfa yeniden
        // kullandığında sessizce kapsam sızdırabilirdi.
        var k = filtre ?? new OtomatikTahsilatFiltre();
        var kapsam = BranchScope.EffectiveFilter(_currentUser);
        var f = new OtomatikTahsilatFiltre
        {
            SozlesmeNo = k.SozlesmeNo, VadeMin = k.VadeMin, VadeMax = k.VadeMax,
            SadeceBakiyeli = k.SadeceBakiyeli,
            // Şube kapsamı DAİMA uygulanır (BranchScope tek kural; bugün yalnız Operatör'ü kısıtlar).
            SubeIdler = kapsam.SubeId is { } sid ? [sid] : null,
            SubeAdi = kapsam.SubeAd
        };
        return await _repository.AdaylarAsync(f, ct);
    }

    /// <summary>
    /// Seçili dönemleri çalıştırır. <paramref name="tahsilatYap"/> false ise yalnız fatura kesilir.
    /// </summary>
    public async Task<OtomatikTahsilatSonuc> CalistirAsync(
        IReadOnlyCollection<(Guid RentalId, int DonemSira)> secim, bool tahsilatYap,
        LedgerAccountType hesap, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (secim.Count == 0) throw new ValidationException("En az bir dönem seçilmelidir.");
        if (secim.Count > MaxSecim)
            throw new ValidationException($"Tek seferde en çok {MaxSecim} dönem çalıştırılabilir.");
        if (hesap is not (LedgerAccountType.Kasa or LedgerAccountType.Banka))
            throw new ValidationException("Hesap Kasa ya da Banka olmalıdır.");

        // ADAY ÇİTİ: yalnız kullanıcının GÖREBİLDİĞİ (şube kapsamı + vadesi gelmiş + Planlandi)
        // dönemler çalıştırılabilir. Uydurma bir POST ile kapsam dışı sözleşme tetiklenemesin.
        var adayListe = await AdaylarAsync(null, ct);
        var adaylar = adayListe.ToDictionary(a => (a.RentalId, a.DonemSira), a => a.SozlesmeNo);

        var kesilen = 0; var tahsilat = 0;
        var atlananlar = new List<string>();
        foreach (var (rentalId, donemSira) in secim.Distinct())
        {
            // Mesajlar SÖZLEŞME NO taşır (adversarial M2): çok sözleşmeli çalıştırmada "Dönem 2"
            // yazan iki satır birbirinden ayırt edilemiyordu.
            if (!adaylar.TryGetValue((rentalId, donemSira), out var sozNo))
            {
                // Sessizce atlamak, kullanıcının "çalıştı" sanmasına yol açardı — listeye yazılır.
                atlananlar.Add($"Dönem {donemSira}: kapsam dışı ya da artık kesilebilir değil (liste bayat olabilir).");
                continue;
            }
            try
            {
                var (_, yazildi) = await _donemTahsilat.KesVeTahsilEtDetayAsync(
                    rentalId, donemSira, tahsilatYap, hesap, ct);
                kesilen++;
                // Sayaç GERÇEĞİ söyler (adversarial M1): idempotent yutulan tahsilat sayılmaz.
                if (yazildi) tahsilat++;
                else if (tahsilatYap)
                    atlananlar.Add($"{sozNo} — Dönem {donemSira}: tahsilat daha önce alınmış, tekrar yazılmadı.");
            }
            // Job çekirdeğiyle AYNI genişlik (adversarial M4): beklenmedik bir hata partiyi ortada
            // bırakıp 500 vermemeli — önceki dönemler zaten commit'li, kullanıcı ne yazıldığını görmeli.
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                atlananlar.Add($"{sozNo} — Dönem {donemSira}: {ex.Message}");
            }
        }
        return new OtomatikTahsilatSonuc(kesilen, tahsilat, atlananlar);
    }
}
