using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Integrations;
using RentACar.Application.TenantSettings;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Kabis;

/// <summary>
/// KABİS bildirim kuyruğunun TEK mantığı: kayıt, gönderim, sonuç sınıflandırması, yeniden deneme, operatör
/// müdahalesi ve ekran durumu. İstek yolunda DI ile, arka plan işinde job'ın context'i üzerine kurulur
/// (<c>KabisNotificationGenerator</c>) — kural kopyalanmaz.
///
/// <para><b>Akış:</b> teslim/dönüş kira işlemiyle AYNI transaction'da kuyruk kaydı yazar (<see cref="KabisOutbox"/>),
/// commit sonrası hemen bir kez dener (<see cref="TrySendAsync"/>); olmazsa iş geri çekilmeyle yeniden dener.
/// Ayar "teslimi engelle" ise bildirim teslimden ÖNCE senkron yapılır (<see cref="ReportBeforeDeliveryAsync"/>).</para>
///
/// <para><b>Dürüstlük:</b> adaptör/kimlik yoksa kayıt <c>Bekliyor</c> + "bağlı değil" kalır, deneme hakkı yanmaz,
/// asla <c>Bildirildi</c> olmaz. Başarı yalnız karşı sistem bir dış referans döndürdüğünde yazılır.</para>
///
/// <para><b>PII:</b> yük her denemede bellekte kurulur; kimlik değerleri kalıcılaşmaz, log yazılmaz, hata metni
/// <see cref="KabisErrorText"/> ile maskelenir.</para>
/// </summary>
public sealed class KabisNotificationService(
    IKabisRepository repository, ITenantSettingsRepository settings, IKabisService port,
    ISecretProtector secrets, ICurrentUser currentUser)
{
    public const string WaitingForRental = "İade bildirimi için önce kiralama bildiriminin tamamlanması bekleniyor.";
    public const string CancelledNote = "Kira iptal edildi; bildirim yapılmadan kapatıldı.";
    public const string NoReference = "KABİS başarı bildirdi ancak referans döndürmedi; bildirim doğrulanamadı.";

    // ================================================================ kapı + kayıt

    /// <summary>Tenant'ın KABİS kapısı (ayar yoksa kapalı).</summary>
    public async Task<KabisGate> GateAsync(CancellationToken ct = default)
        => KabisCredentials.Gate(await settings.GetAsync(ct));

    /// <summary>Teslim çıkış kaydı tarifi (kapı kapalıysa ya da engelleyici modda — kayıt orada zaten yazıldı — null).</summary>
    public static KabisOutbox? DeliveryOutbox(KabisGate gate, DateTimeOffset now)
        => gate.Etkin ? new KabisOutbox(KabisNoticeType.Kiralama, gate.Kurum, now) : null;

    /// <summary>Dönüş kaydı tarifi — yalnız kirada "gerekli değil" olmayan Kiralama kaydı varsa yazılır.</summary>
    public static KabisOutbox? ReturnOutbox(KabisGate gate, DateTimeOffset actualReturn)
        => gate.Etkin ? new KabisOutbox(KabisNoticeType.Iade, gate.Kurum, actualReturn, RequiresReportedRental: true) : null;

    public static KabisNotification NewRecord(Guid rentalId, KabisNoticeType type, KabisAuthority authority, DateTimeOffset eventUtc)
        => new()
        {
            KiraId = rentalId, Tur = type, Kurum = authority, OlayUtc = eventUtc.ToUniversalTime(),
            Durum = KabisNoticeStatus.Bekliyor, OlusturmaUtc = DateTimeOffset.UtcNow,
        };

    // ================================================================ gönderim

    /// <summary>
    /// Engelleyici mod: teslimden ÖNCE Kiralama bildirimini senkron yapar ve kaydı döner. Kayıt <c>Bildirildi</c>
    /// (ya da operatör kararıyla <c>GerekliDegil</c>) değilse çağıran teslimi reddeder. Kayıt yoksa oluşturulur;
    /// <c>Hata</c>'daysa operatör yeniden teslim denediği için sıfırlanıp hemen denenir.
    /// </summary>
    public async Task<KabisNotification> ReportBeforeDeliveryAsync(
        Guid rentalId, KabisGate gate, DateTimeOffset now, CancellationToken ct = default)
    {
        var row = await repository.FindAsync(rentalId, KabisNoticeType.Kiralama, ct);
        if (row is null)
        {
            await repository.AddAsync(NewRecord(rentalId, KabisNoticeType.Kiralama, gate.Kurum, now), ct);
            row = await repository.FindAsync(rentalId, KabisNoticeType.Kiralama, ct)
                  ?? throw new InvalidOperationException("KABİS kaydı oluşturulamadı.");
        }
        if (row.Durum is KabisNoticeStatus.Bildirildi or KabisNoticeStatus.GerekliDegil) return row;
        if (row.Durum == KabisNoticeStatus.Hata)
            await repository.UpdateAsync(row.Id, r =>
            {
                if (r.Durum != KabisNoticeStatus.Hata) return;
                r.Durum = KabisNoticeStatus.Bekliyor;
                r.DenemeSayisi = 0;
                r.SonrakiDenemeUtc = null;
            }, ct);
        // Teslim anı bu andır (kayıt önceki bir denemede oluşmuş olabilir).
        await repository.UpdateAsync(row.Id, r => { if (r.Durum == KabisNoticeStatus.Bekliyor) r.OlayUtc = now.ToUniversalTime(); }, ct);
        return await SendAsync(row.Id, now, force: true, ct) ?? row;
    }

    /// <summary>
    /// Commit sonrası en iyi çaba ile bir kez dener. Hata fırlatmaz: kayıt kuyrukta kalır ve iş yeniden dener —
    /// teslim/dönüş başarılı yanıtı bildirim arızası yüzünden 500'e dönmemeli.
    /// </summary>
    public async Task TrySendAsync(Guid rentalId, KabisNoticeType type, CancellationToken ct = default)
    {
        try
        {
            var row = await repository.FindAsync(rentalId, type, ct);
            if (row is { Durum: KabisNoticeStatus.Bekliyor })
                await SendAsync(row.Id, DateTimeOffset.UtcNow, force: false, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Bilinçli yutma: satır Bekliyor + kısa sahiplenme süresiyle kalır, iş birkaç dakika içinde alır.
        }
    }

    /// <summary>
    /// Bir kaydı gönderir: ÖNCE atomik sahiplenme (iki gönderici aynı satırı aynı anda gönderemez), sonra yük kurulur,
    /// port çağrılır, sonuç sınıflandırılıp yazılır. Sahiplenilemezse dokunmadan güncel satırı döner.
    /// </summary>
    public async Task<KabisNotification?> SendAsync(Guid id, DateTimeOffset now, bool force, CancellationToken ct = default)
    {
        now = now.ToUniversalTime();
        if (!await repository.TryClaimAsync(id, now, KabisRetryPolicy.Lease, force, ct))
            return await repository.FindByIdAsync(id, ct);
        var row = await repository.FindByIdAsync(id, ct);
        if (row is null) return null;

        var outcome = await AttemptAsync(row, ct);
        await repository.UpdateAsync(id, r => Apply(r, outcome, now), ct);
        return await repository.FindByIdAsync(id, ct);
    }

    /// <summary>İş: vadesi gelmiş bekleyenleri gönderir + iptal edilmiş kiraları uzlaştırır. Kapı kapalıysa no-op.</summary>
    public async Task<int> RunDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        if (!(await GateAsync(ct)).Etkin) return 0;
        var count = await ReconcileCancelledAsync(now, ct);
        foreach (var id in await repository.DueAsync(now, 50, ct))
        {
            await SendAsync(id, now, force: false, ct);
            count++;
        }
        return count;
    }

    /// <summary>
    /// Kira iptali sonrası: bildirilmemiş Kiralama kaydı "gerekli değil" olur; bildirilmişse Iptal kaydı kuyruğa girer.
    /// Hem iptal ucundan (commit sonrası) hem iş uzlaştırmasından çağrılır — idempotent.
    /// </summary>
    public async Task<int> ReconcileCancelledAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var gate = await GateAsync(ct);
        var count = 0;
        foreach (var (rentalId, rentalStatus, hasCancel) in await repository.CancelledRentalCandidatesAsync(ct))
        {
            if (rentalStatus is KabisNoticeStatus.Bekliyor or KabisNoticeStatus.Hata)
            {
                var row = await repository.FindAsync(rentalId, KabisNoticeType.Kiralama, ct);
                if (row is null) continue;
                await repository.UpdateAsync(row.Id, r =>
                {
                    if (r.Durum is not (KabisNoticeStatus.Bekliyor or KabisNoticeStatus.Hata)) return;
                    r.Durum = KabisNoticeStatus.GerekliDegil;
                    r.Gerekce = CancelledNote;
                    r.SonrakiDenemeUtc = null;
                }, ct);
                count++;
            }
            else if (rentalStatus == KabisNoticeStatus.Bildirildi && !hasCancel && gate.Etkin)
            {
                if (await repository.AddAsync(NewRecord(rentalId, KabisNoticeType.Iptal, gate.Kurum, now), ct)) count++;
            }
        }
        return count;
    }

    /// <summary>İptal ucunun commit sonrası çağrısı: uzlaştırma + yeni Iptal kaydını hemen deneme (en iyi çaba).</summary>
    public async Task OnRentalCancelledAsync(Guid rentalId, CancellationToken ct = default)
    {
        try
        {
            if (!(await GateAsync(ct)).Etkin) return;
            await ReconcileCancelledAsync(DateTimeOffset.UtcNow, ct);
            await TrySendAsync(rentalId, KabisNoticeType.Iptal, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // İş uzlaştırması aynı kuralı periyodik koşar; iptal yanıtı bildirim arızasıyla bozulmaz.
        }
    }

    // ================================================================ operatör

    /// <summary>Kira ekranı KABİS bloğu (okuma: OperationsWrite veya FinanceWrite + şube kapsamı).</summary>
    public async Task<KabisRentalStatus> GetForRentalAsync(Guid rentalId, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(currentUser, Permission.OperationsWrite, Permission.FinanceWrite);
        var source = await ScopedSourceAsync(rentalId, ct);
        var gate = await GateAsync(ct);
        var rows = (await repository.ListForRentalAsync(rentalId, ct)).OrderBy(r => r.Tur).Select(View).ToList();
        return new KabisRentalStatus(gate.Etkin, gate.TeslimiEngelle, gate.Kurum, rows, Warning(gate, source, rows));
    }

    /// <summary>Operatör "şimdi yeniden dene": Hata → Bekliyor (deneme sayacı sıfırlanır) ve hemen gönderilir.</summary>
    public async Task<KabisRentalStatus> RetryAsync(Guid rentalId, KabisNoticeType type, CancellationToken ct = default)
    {
        var row = await WritableRowAsync(rentalId, type, ct);
        if (!(await GateAsync(ct)).Etkin)
            throw new ValidationException("KABİS otomatik bildirimi kapalı; Ayarlar > KABİS'ten açın.");
        if (row.Durum is not (KabisNoticeStatus.Bekliyor or KabisNoticeStatus.Hata))
            throw new ValidationException("Yalnız bekleyen ya da hatalı bildirim yeniden denenir.");
        await repository.UpdateAsync(row.Id, r =>
        {
            if (r.Durum != KabisNoticeStatus.Hata) return;
            r.Durum = KabisNoticeStatus.Bekliyor;
            r.DenemeSayisi = 0;
            r.SonrakiDenemeUtc = null;
        }, ct);
        await SendAsync(row.Id, DateTimeOffset.UtcNow, force: true, ct);
        return await GetForRentalAsync(rentalId, ct);
    }

    /// <summary>Operatör kararı: bu bildirim gerekli değil (gerekçe zorunlu). Yalnız bekleyen/hatalı kayıtta.</summary>
    public async Task<KabisRentalStatus> MarkNotRequiredAsync(
        Guid rentalId, KabisNoticeType type, string? reason, CancellationToken ct = default)
    {
        var text = RequiredText(reason, 256, "gerekce", "Gerekçe");
        var row = await WritableRowAsync(rentalId, type, ct);
        if (row.Durum is not (KabisNoticeStatus.Bekliyor or KabisNoticeStatus.Hata))
            throw new ValidationException("Yalnız bekleyen ya da hatalı bildirim 'gerekli değil' işaretlenebilir.");
        var user = currentUser.UserId;
        await repository.UpdateAsync(row.Id, r =>
        {
            if (r.Durum is not (KabisNoticeStatus.Bekliyor or KabisNoticeStatus.Hata)) return;
            r.Durum = KabisNoticeStatus.GerekliDegil;
            r.Gerekce = text;
            r.IsaretleyenKullaniciId = user;
            r.SonrakiDenemeUtc = null;
        }, ct);
        return await GetForRentalAsync(rentalId, ct);
    }

    /// <summary>Operatör kaydı: bildirim KABİS portalından ELLE yapıldı (referans opsiyonel). Yalnız bekleyen/hatalı kayıtta.</summary>
    public async Task<KabisRentalStatus> MarkManuallyReportedAsync(
        Guid rentalId, KabisNoticeType type, string? reference, CancellationToken ct = default)
    {
        var refText = OptionalText(reference, 128, "referans", "Referans");
        var row = await WritableRowAsync(rentalId, type, ct);
        if (row.Durum is not (KabisNoticeStatus.Bekliyor or KabisNoticeStatus.Hata))
            throw new ValidationException("Yalnız bekleyen ya da hatalı bildirim 'elle bildirildi' işaretlenebilir.");
        var user = currentUser.UserId;
        var now = DateTimeOffset.UtcNow;
        await repository.UpdateAsync(row.Id, r =>
        {
            if (r.Durum is not (KabisNoticeStatus.Bekliyor or KabisNoticeStatus.Hata)) return;
            r.Durum = KabisNoticeStatus.Bildirildi;
            r.Elle = true;
            r.DisReferans = refText;
            r.Gerekce = "Portaldan elle bildirildi.";
            r.IsaretleyenKullaniciId = user;
            r.BildirimUtc = now;
            r.SonHata = null;
            r.SonrakiDenemeUtc = null;
        }, ct);
        return await GetForRentalAsync(rentalId, ct);
    }

    /// <summary>Bildirilemeyen (Bekliyor/Hata) kayıtlar — şube kapsamlı; kapı kapalıysa boş.</summary>
    public async Task<IReadOnlyList<KabisProblemRow>> ListProblemsAsync(CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(currentUser, Permission.OperationsWrite, Permission.FinanceWrite);
        if (!(await GateAsync(ct)).Etkin) return [];
        return await repository.ListProblemsAsync(BranchScope.EffectiveFilter(currentUser), 500, ct);
    }

    /// <summary>Panel sayacı: bildirilemeyen kira sayısı (şube kapsamlı). Kapı kapalıysa null (kutu gösterilmez).</summary>
    public async Task<int?> ProblemCountAsync(CancellationToken ct = default)
    {
        if (!(await GateAsync(ct)).Etkin) return null;
        return await repository.CountProblemRentalsAsync(BranchScope.EffectiveFilter(currentUser), ct);
    }

    // ================================================================ iç

    private sealed record Outcome(KabisSonucTuru Kind, string? ExternalRef, string? Error, bool PortCalled,
        TimeSpan? Wait = null, bool NotRequired = false);

    private async Task<Outcome> AttemptAsync(KabisNotification row, CancellationToken ct)
    {
        var setting = await settings.GetAsync(ct);
        var (credentials, missing) = KabisCredentials.Resolve(setting, secrets);
        if (credentials is null) return new Outcome(KabisSonucTuru.BagliDegil, null, missing, PortCalled: false);

        var source = await repository.LoadSourceAsync(row.KiraId, ct);
        if (source is null)
            return new Outcome(KabisSonucTuru.KaliciHata, null, "Kira sözleşmesi bulunamadı.", PortCalled: false);

        IReadOnlyList<string> known = [credentials.Sifre];
        try
        {
            KabisSonuc result;
            switch (row.Tur)
            {
                case KabisNoticeType.Kiralama:
                {
                    if (source.Durum == RentalStatus.Iptal)
                        return new Outcome(KabisSonucTuru.KaliciHata, null, CancelledNote, false, NotRequired: true);
                    var built = KabisPayloadBuilder.Rental(source, row.OlayUtc, secrets.Unprotect);
                    known = [.. built.Secrets, credentials.Sifre];
                    if (built.Notice is null)
                        return new Outcome(KabisSonucTuru.KaliciHata, null, built.Error, PortCalled: false);
                    result = await port.NotifyRentalAsync(credentials, built.Notice, ct);
                    break;
                }
                case KabisNoticeType.Iade:
                case KabisNoticeType.Iptal:
                {
                    var rental = await repository.FindAsync(row.KiraId, KabisNoticeType.Kiralama, ct);
                    if (rental is null || rental.Durum == KabisNoticeStatus.GerekliDegil)
                        return new Outcome(KabisSonucTuru.KaliciHata, null,
                            "Kiralama bildirimi gerekli değil olarak kapatıldı; bu bildirim de gerekmez.", false, NotRequired: true);
                    if (rental.Durum != KabisNoticeStatus.Bildirildi)
                        return new Outcome(KabisSonucTuru.GeciciHata, null, WaitingForRental, false,
                            Wait: KabisRetryPolicy.DependencyDelay);
                    result = row.Tur == KabisNoticeType.Iade
                        ? await port.NotifyReturnAsync(credentials, KabisPayloadBuilder.Return(source, rental.DisReferans, row.OlayUtc), ct)
                        : await port.CancelAsync(credentials, source.SozlesmeNo, rental.DisReferans, "Kira sözleşmesi iptal edildi.", ct);
                    break;
                }
                default:
                    return new Outcome(KabisSonucTuru.KaliciHata, null, "Bilinmeyen bildirim türü.", false);
            }

            if (result.Tur == KabisSonucTuru.Basarili && string.IsNullOrWhiteSpace(result.DisReferans))
                return new Outcome(KabisSonucTuru.GeciciHata, null, NoReference, PortCalled: true);
            return new Outcome(result.Tur, result.DisReferans?.Trim(),
                KabisErrorText.Sanitize(result.Hata, known), PortCalled: result.Tur != KabisSonucTuru.BagliDegil);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // İstisna metni de maskelenir (URL/gövde yankısı kimlik taşıyabilir); tür adı tanı için kalır.
            return new Outcome(KabisSonucTuru.GeciciHata, null,
                KabisErrorText.Sanitize($"KABİS isteği başarısız ({ex.GetType().Name}): {ex.Message}", known), PortCalled: true);
        }
    }

    private static void Apply(KabisNotification r, Outcome o, DateTimeOffset now)
    {
        r.SahiplenmeBitisUtc = null; // gönderim bitti — her dalda sahiplenme bırakılır
        if (o.Kind == KabisSonucTuru.Basarili)
        {
            // Karşı sistem kabul ettiyse gerçek budur: operatörün arada verdiği "gerekli değil" kararı da ezilir.
            r.Durum = KabisNoticeStatus.Bildirildi;
            r.DisReferans = o.ExternalRef;
            r.BildirimUtc = now;
            r.SonHata = null;
            r.SonrakiDenemeUtc = null;
            r.DenemeSayisi++;
            return;
        }
        if (r.Durum != KabisNoticeStatus.Bekliyor) return; // arada operatör kapattı — başarısız deneme onu geri açmaz
        r.SonHata = KabisErrorText.Sanitize(o.Error);
        if (o.NotRequired)
        {
            r.Durum = KabisNoticeStatus.GerekliDegil;
            r.Gerekce = o.Error;
            r.SonHata = null;
            r.SonrakiDenemeUtc = null;
            return;
        }
        switch (o.Kind)
        {
            case KabisSonucTuru.BagliDegil:
                r.SonrakiDenemeUtc = now + KabisRetryPolicy.NotConfiguredDelay; // deneme hakkı YANMAZ
                break;
            case KabisSonucTuru.GeciciHata when !o.PortCalled:
                r.SonrakiDenemeUtc = now + (o.Wait ?? KabisRetryPolicy.DependencyDelay);
                break;
            case KabisSonucTuru.GeciciHata:
                r.DenemeSayisi++;
                if (KabisRetryPolicy.Exhausted(r.DenemeSayisi))
                {
                    r.Durum = KabisNoticeStatus.Hata;
                    r.SonrakiDenemeUtc = null;
                }
                else r.SonrakiDenemeUtc = now + KabisRetryPolicy.Delay(r.DenemeSayisi);
                break;
            default: // KaliciHata: veri/yetki reddi — operatör düzeltmeden tekrar denenmez
                if (o.PortCalled) r.DenemeSayisi++;
                r.Durum = KabisNoticeStatus.Hata;
                r.SonrakiDenemeUtc = null;
                break;
        }
    }

    private async Task<KabisRentalSource> ScopedSourceAsync(Guid rentalId, CancellationToken ct)
    {
        var source = await repository.LoadSourceAsync(rentalId, ct)
                     ?? throw new ValidationException("Kira sözleşmesi bulunamadı.");
        BranchScope.RequireInScope(currentUser, source.CikisSubeId, source.CikisOfisi);
        return source;
    }

    private async Task<KabisNotification> WritableRowAsync(Guid rentalId, KabisNoticeType type, CancellationToken ct)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        await ScopedSourceAsync(rentalId, ct); // kapsam kontrolü durum kontrolünden ÖNCE
        return await repository.FindAsync(rentalId, type, ct)
               ?? throw new ValidationException("Bu kira için bu türde KABİS bildirim kaydı yok.");
    }

    private static KabisNoticeView View(KabisNotification r) => new(
        r.Tur, r.Durum, r.Kurum, r.OlayUtc, r.DenemeSayisi, r.SonHata, r.DisReferans, r.Elle, r.Gerekce,
        r.SonDenemeUtc, r.Durum == KabisNoticeStatus.Bekliyor ? r.SonrakiDenemeUtc : null, r.BildirimUtc);

    private static string? Warning(KabisGate gate, KabisRentalSource source, IReadOnlyList<KabisNoticeView> rows)
    {
        if (!gate.Etkin) return null;
        if (rows.FirstOrDefault(r => r.Durum == KabisNoticeStatus.Hata) is { } failed)
            return $"KABİS {Label(failed.Tur)} bildirimi yapılamadı: {failed.SonHata ?? "bilinmeyen hata"} " +
                   "Sorunu giderip yeniden deneyin ya da portaldan elle bildirip işaretleyin.";
        if (rows.FirstOrDefault(r => r.Durum == KabisNoticeStatus.Bekliyor) is { } waiting)
            return $"KABİS {Label(waiting.Tur)} bildirimi henüz yapılmadı" +
                   (waiting.SonHata is { } e ? $": {e}" : " (sırada).");
        if (source.CikisKm is not null && source.Durum != RentalStatus.Iptal && rows.All(r => r.Tur != KabisNoticeType.Kiralama))
            return "Bu kira için KABİS kiralama kaydı yok (KABİS açılmadan önce teslim edilmiş olabilir); portaldan bildirildiğini doğrulayın.";
        return null;
    }

    public static string Label(KabisNoticeType t) => t switch
    {
        KabisNoticeType.Kiralama => "kiralama",
        KabisNoticeType.Iade => "iade",
        _ => "iptal",
    };

    private static string RequiredText(string? value, int max, string field, string label)
        => OptionalText(value, max, field, label) ?? throw new ValidationException($"{label} zorunludur.", field);

    private static string? OptionalText(string? value, int max, string field, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var t = value.Trim();
        if (t.Length > max) throw new ValidationException($"{label} en fazla {max} karakter olabilir.", field);
        return t;
    }
}
