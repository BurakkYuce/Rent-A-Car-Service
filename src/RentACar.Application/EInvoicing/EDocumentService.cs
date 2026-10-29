using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Application.Integrations;
using RentACar.Application.TenantSettings;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.EInvoicing;

/// <summary>Bir tenant'ın çözülmüş entegratör bağlantısı. <see cref="Gap"/> doluysa bağlı değildir.</summary>
public sealed record EInvoiceConnection(IEInvoiceIntegrator? Integrator, EInvoiceCredentials? Credentials, string? Gap)
{
    public bool Ready => Gap is null && Integrator is not null && Credentials is not null;

    /// <summary>Ayar + kayıtlı adaptör + çözülmüş sır. Sır çözülemezse (anahtar halkası uyumsuz) bağlı değildir.</summary>
    public static EInvoiceConnection Resolve(Domain.Entities.TenantSettings? s, EInvoiceIntegratorCatalog catalog, ISecretProtector secrets)
    {
        if (EInvoiceReadiness.Gap(s, catalog) is { } gap) return new EInvoiceConnection(null, null, gap);
        var secret = secrets.Unprotect(s!.EFaturaSifreEnc);
        if (string.IsNullOrEmpty(secret))
            return new EInvoiceConnection(null, null, "entegratör şifresi çözülemedi — şifreyi yeniden girin");
        return new EInvoiceConnection(catalog.Find(s.EFaturaEntegrator)!,
            new EInvoiceCredentials(s.EFaturaKullanici!.Trim(), secret, s.EFaturaGondericiEtiket, s.EFaturaPostaKutusuEtiket,
                s.EFaturaCanliOrtam), null);
    }

    /// <summary>
    /// Önceki denemenin (<see cref="EDocument.SonDenemeUtc"/>) entegratöre ulaşıp ulaşmadığını sorar. Ortam canlıdan
    /// teste çevrildiyse önce CANLI ortamda da sorulur (adversarial N3): test ortamının "bulunamadı"sı canlıdaki belgeyi
    /// bilemez. Önceki deneme testte, şimdiki ortam canlıysa yalnız canlıda sorulur — test kabulü GİB'e ulaşmadı.
    /// Dönen <c>Live</c>: belgenin bulunduğu (bulunmadıysa sorulan) ortam. İstisnalar çağırana geçer.
    /// <para>Canlı deneme KESİNLEŞMEDEN (<see cref="EDocumentPolicy.LiveAttemptMayStillLand"/>) canlının "bulunamadı"sı
    /// olduğu gibi döner, test ortamına SORULMAZ (#410 adversarial M1): test → canlı → test geçişinde aynı ETTN'in eski test
    /// kabulü bulunup belge "test gönderildi" sayılır, ardından geç gelen canlı kabul yerel iptalle gizlenirdi.</para>
    /// </summary>
    public async Task<(EInvoiceStatusResult Status, bool Live)> QueryPreviousAttemptAsync(EDocument doc, DateTimeOffset now, CancellationToken ct)
    {
        var kind = doc.Tur ?? EDocumentKind.EArsiv;
        if (doc.SonDenemeCanli == true && !Credentials!.CanliOrtam)
        {
            var live = await Integrator!.QueryStatusAsync(Credentials with { CanliOrtam = true }, doc.Ettn, kind, ct);
            if (!live.Sorgulandi || live.Bulundu || EDocumentPolicy.LiveAttemptMayStillLand(doc, now)) return (live, true);
        }
        return (await Integrator!.QueryStatusAsync(Credentials!, doc.Ettn, kind, ct), Credentials!.CanliOrtam);
    }

    public override string ToString() => $"EInvoiceConnection {{ Ready = {Ready}, Gap = {Gap} }}";
}

/// <summary>Kullanıcı eylemi sonucu: e-belge kaydının yeni durumu.</summary>
public sealed record EDocumentActionResult(Guid InvoiceId, EDocumentStatus Durum, string? Mesaj);

/// <summary>
/// e-Belge kullanıcı eylemleri: durum okuma, (yeniden) gönderim kuyruğuna alma ve iptal. Gönderimin KENDİSİ burada
/// değil, gönderim işindedir (kuyruk + idempotent ETTN) — kullanıcı isteği entegratöre gitmeyi beklemez.
/// </summary>
public sealed class EDocumentService(
    IEDocumentRepository repository,
    IInvoiceRepository invoices,
    ITenantSettingsRepository settings,
    EInvoiceIntegratorCatalog catalog,
    ISecretProtector secrets,
    ICurrentUser currentUser)
{
    public const int ReasonMaxLength = 256;

    public Task<EDocument?> GetByInvoiceAsync(Guid invoiceId, CancellationToken ct = default)
        => repository.FindByInvoiceAsync(invoiceId, ct);

    public Task<IReadOnlyDictionary<Guid, EDocument>> ListByInvoicesAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct = default)
        => repository.ListByInvoicesAsync(invoiceIds, ct);

    /// <summary>Ayarlardan bağlantı durumu (ağ yok). Ayar ekranı ve kullanıcı eylemleri bunu gösterir.</summary>
    public async Task<string?> ConnectionGapAsync(CancellationToken ct = default)
        => EInvoiceConnection.Resolve(await settings.GetAsync(ct), catalog, secrets).Gap;

    /// <summary>Faturanın iadesi kesilmiş mi (e-belgesi gönderilmez; gönderilmiş e-Arşiv'i ancak o zaman iptal edilir).</summary>
    public Task<bool> HasRefundAsync(Guid invoiceId, CancellationToken ct = default)
        => invoices.RefundExistsForAsync(invoiceId, ct);

    /// <summary>
    /// Faturayı e-belge gönderim kuyruğuna alır (Taslak/Hata/TestGonderildi → Gönderilecek; kayıt yoksa — özelliğin
    /// öncesinden kalan fatura — oluşturulur). Entegratör bağlı değilse REDDEDİLİR (kuyruğa sahte umut yazılmaz). İadesi
    /// kesilmiş fatura reddedilir. Zaten kuyruktaysa sessiz başarı; gönderilmiş/iptal edilmişse ret. FinanceWrite.
    /// </summary>
    public async Task<EDocumentActionResult> QueueAsync(Guid invoiceId, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.FinanceWrite);
        var invoice = await invoices.FindAsync(invoiceId, ct) ?? throw new ValidationException("Fatura bulunamadı.");
        if (invoice.IadeMi) throw new ValidationException(EDocumentPolicy.RefundNotSent);
        if (invoice.Durum == InvoiceStatus.Iptal) throw new ValidationException("İptal edilmiş fatura için e-belge gönderilmez.");
        if (await invoices.RefundExistsForAsync(invoiceId, ct)) throw new ValidationException(EDocumentPolicy.RefundedSourceNotSent);
        if (EInvoiceConnection.Resolve(await settings.GetAsync(ct), catalog, secrets).Gap is { } gap)
            throw new ValidationException(EInvoiceReadiness.NotConnected(gap));

        var status = await repository.QueueAsync(invoiceId, DateTimeOffset.UtcNow, ct);
        return new EDocumentActionResult(invoiceId, status, null);
    }

    /// <summary>
    /// e-Belgeyi iptal eder. FinanceReverse + gerekçe zorunlu.
    /// <list type="bullet">
    /// <item><b>Gönderilmiş e-Arşiv</b> → yalnız fatura İADE EDİLDİYSE entegratörde iptal (adversarial M3: iadesiz
    /// iptal defterde satış + KDV bırakıp GİB'de belgesiz, düzeltilemez fatura üretirdi); başarısızsa durum DEĞİŞMEZ.</item>
    /// <item><b>Test ortamına gönderilmiş</b> → yerel iptal (GİB'e ulaşmadı).</item>
    /// <item><b>Gönderilmiş e-Fatura</b> → reddedilir: e-Fatura gönderenin tek taraflı iptaliyle geri alınamaz.</item>
    /// <item><b>Gönderilmemiş</b> (Taslak/Hata/Gönderilecek) → yerel iptal: belge bir daha gönderilmez. Gönderim işi o
    /// an satırı tutuyorsa (kilit) reddedilir — yarışta "iptal edildi" deyip arkadan gönderilmesin. Önceki canlı deneme
    /// varsa entegratöre sorulur; "bulunamadı" ancak <see cref="EDocumentPolicy.AttemptSettle"/> dolduysa yeterlidir.</item>
    /// </list>
    /// </summary>
    public async Task<EDocumentActionResult> CancelAsync(Guid invoiceId, string? reason, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.FinanceReverse);
        var why = reason?.Trim();
        if (string.IsNullOrEmpty(why)) throw new ValidationException("İptal gerekçesi zorunludur.", "gerekce");
        if (why.Length > ReasonMaxLength) throw new ValidationException($"İptal gerekçesi en fazla {ReasonMaxLength} karakter olabilir.", "gerekce");

        var doc = await repository.FindByInvoiceAsync(invoiceId, ct)
            ?? throw new ValidationException("Bu fatura için e-belge kaydı yok.");
        var now = DateTimeOffset.UtcNow;

        // Daha önce gönderim DENENMİŞ ama sonucu "gönderildi" yazılamamış belge (kayıp yanıt, deneme sınırı) entegratöre
        // ulaşmış olabilir: "gönderilmeden iptal" demeden önce entegratöre sorulur. Ulaşmışsa kayıt gönderildi olarak
        // düzeltilir ve gönderilmiş belge kuralı uygulanır — yerelde iptal edilip GİB'de yaşayan belge kalmasın.
        if (doc.Durum is EDocumentStatus.Taslak or EDocumentStatus.Hata or EDocumentStatus.Gonderilecek && doc.SonDenemeUtc is not null)
        {
            var conn = EInvoiceConnection.Resolve(await settings.GetAsync(ct), catalog, secrets);
            if (!conn.Ready)
                throw new ValidationException(
                    "Bu belge için daha önce gönderim denendi; entegratör bağlı olmadan sonucu doğrulanamaz, yerel iptal yapılamaz.");
            EInvoiceStatusResult status;
            var live = conn.Credentials!.CanliOrtam;
            try { (status, live) = await conn.QueryPreviousAttemptAsync(doc, now, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { status = new EInvoiceStatusResult(false, false, null, null, ex.Message); }
            if (!status.Sorgulandi)
                throw new ValidationException(EDocumentPolicy.Sanitize(
                    "Önceki gönderim denemesinin sonucu entegratörden alınamadı; daha sonra yeniden deneyin: " + status.Hata)!);
            if (status.Bulundu)
            {
                if (!await repository.MarkSentAsync(doc.Id, [doc.Durum], status.EntegratorRef, status.Durum, live, now, ct))
                    throw new ValidationException("e-Belge şu anda gönderiliyor; birkaç dakika sonra yeniden deneyin.");
                doc = await repository.FindByInvoiceAsync(invoiceId, ct) ?? doc;
            }
            // Tek "bulunamadı" yanıtı, istemci zaman aşımından sonra sunucunun geç kabul ettiği belgeyi göremez
            // (adversarial N1): canlı deneme kesinleşmeden yerel iptal yok.
            else if (EDocumentPolicy.LiveAttemptMayStillLand(doc, now))
                throw new ValidationException(EDocumentPolicy.AttemptNotSettled(doc, now));
        }

        switch (doc.Durum)
        {
            case EDocumentStatus.Iptal:
                return new EDocumentActionResult(invoiceId, doc.Durum, doc.SonMesaj);

            case EDocumentStatus.TestGonderildi:
            {
                // Test kabulünden önce kesinleşmemiş bir canlı deneme varsa yerel iptal yok (#410 adversarial M1).
                if (doc.SonDenemeCanli == true && EDocumentPolicy.LiveAttemptMayStillLand(doc, now))
                    throw new ValidationException(EDocumentPolicy.AttemptNotSettled(doc, now));
                var message = EDocumentPolicy.Sanitize("Test ortamına gönderilmiş e-belge iptal edildi (GİB'e ulaşmamıştı): " + why);
                if (!await repository.MarkCancelledAsync(doc.Id, [EDocumentStatus.TestGonderildi], requireLeaseFree: true, message, now, ct))
                    throw new ValidationException("e-Belge durumu bu sırada değişti; sayfayı yenileyin.");
                return new EDocumentActionResult(invoiceId, EDocumentStatus.Iptal, message);
            }

            case EDocumentStatus.Gonderildi:
            {
                if (doc.Tur != EDocumentKind.EArsiv)
                    throw new ValidationException(
                        "Gönderilmiş e-Fatura entegratörden iptal edilemez: alıcının iade faturası ya da GİB portalından iptal talebi gerekir.");
                if (!await invoices.RefundExistsForAsync(invoiceId, ct))
                    throw new ValidationException(EDocumentPolicy.SentArchiveNeedsRefund);
                var conn = EInvoiceConnection.Resolve(await settings.GetAsync(ct), catalog, secrets);
                if (!conn.Ready) throw new ValidationException(EInvoiceReadiness.NotConnected(conn.Gap!));
                EInvoiceCancelResult result;
                // Gönderildi = CANLI kabul: iptal canlıya gider, ortam sonradan teste çevrilmiş olsa da (#410 adversarial M3 —
                // test ortamı aynı ETTN'i eski test kabulünden tanıyıp "iptal edildi" der, canlı belge yaşamaya devam ederdi).
                try { result = await conn.Integrator!.CancelAsync(conn.Credentials! with { CanliOrtam = true }, doc.Ettn, EDocumentKind.EArsiv, why, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    result = new EInvoiceCancelResult(false, ex.Message);
                }
                if (!result.Basarili)
                    throw new ValidationException(EDocumentPolicy.Sanitize("e-Arşiv belgesi iptal edilemedi: " + (result.Hata ?? "entegratör yanıt vermedi"))!);
                var message = EDocumentPolicy.Sanitize("e-Arşiv belgesi entegratörde iptal edildi: " + why);
                if (!await repository.MarkCancelledAsync(doc.Id, [EDocumentStatus.Gonderildi], requireLeaseFree: false, message, now, ct))
                    throw new ValidationException("e-Belge durumu bu sırada değişti; sayfayı yenileyin.");
                return new EDocumentActionResult(invoiceId, EDocumentStatus.Iptal, message);
            }

            default:
            {
                var message = EDocumentPolicy.Sanitize("e-Belge gönderilmeden iptal edildi: " + why);
                if (!await repository.MarkCancelledAsync(doc.Id,
                        [EDocumentStatus.Taslak, EDocumentStatus.Hata, EDocumentStatus.Gonderilecek], requireLeaseFree: true, message, now, ct))
                    throw new ValidationException("e-Belge şu anda gönderiliyor; birkaç dakika sonra yeniden deneyin.");
                return new EDocumentActionResult(invoiceId, EDocumentStatus.Iptal, message);
            }
        }
    }
}

/// <summary>e-Belge kayıtlarının kalıcılığı (tenant-owned, RLS).</summary>
public interface IEDocumentRepository
{
    Task<EDocument?> FindByInvoiceAsync(Guid invoiceId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, EDocument>> ListByInvoicesAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct = default);

    /// <summary>Satır kilidi altında kuyruğa alır; kayıt yoksa oluşturur. Dönen değer yeni durum. Gönderilmiş/iptal
    /// kayıtta <see cref="ValidationException"/>.</summary>
    Task<EDocumentStatus> QueueAsync(Guid invoiceId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Koşullu iptal: durum <paramref name="expected"/> içindeyse (ve istenirse kilit boşsa) İptal yazar.
    /// Koşul tutmazsa false (hiçbir şey yazılmaz).</summary>
    Task<bool> MarkCancelledAsync(Guid id, IReadOnlyCollection<EDocumentStatus> expected, bool requireLeaseFree,
        string? message, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Entegratörün TANIDIĞI (önceki denemede ulaşmış) belgeyi gönderildi olarak düzeltir. Koşul: durum
    /// <paramref name="expected"/> içinde ve gönderim kilidi boş. Koşul tutmazsa false.</summary>
    Task<bool> MarkSentAsync(Guid id, IReadOnlyCollection<EDocumentStatus> expected, string? integratorRef, string? integratorStatus,
        bool liveEnvironment, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Bu seriyle <paramref name="kind"/> DIŞINDA bir türde GİB belge numarası verilmiş mi (seri takası koruması).
    /// <paramref name="kind"/> null → herhangi bir türde (iç fatura serisi için).</summary>
    Task<bool> SeriesUsedByOtherKindAsync(string series, EDocumentKind? kind, CancellationToken ct = default);
}
