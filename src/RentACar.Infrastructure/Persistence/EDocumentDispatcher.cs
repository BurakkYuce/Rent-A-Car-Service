using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RentACar.Application.Common;
using RentACar.Application.EInvoicing;
using RentACar.Application.Integrations;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// e-Belge gönderim işi (tenant başına bir koşu; <c>EDocumentJob</c> ve testler çağırır). Kuyruktaki
/// (<see cref="EDocumentStatus.Gonderilecek"/>, vadesi gelmiş) belgeleri entegratöre iletir.
///
/// <para><b>Aynı fatura iki kez gönderilemez (idempotency):</b></para>
/// <list type="number">
/// <item><b>Kilit (kiralama):</b> satır koşullu UPDATE ile alınır (<c>Durum = Gönderilecek AND kilit boş/süresi
/// dolmuş</c>). Eşzamanlı iki koşudan yalnız biri satırı alır; diğeri atlar.</item>
/// <item><b>Önce durum sorusu:</b> satırın daha önce entegratöre ULAŞMIŞ OLABİLECEK bir denemesi varsa
/// (<c>SonDenemeUtc</c> dolu — çöküş, zaman aşımı, kayıp yanıt) gönderimden ÖNCE entegratöre ETTN ile sorulur;
/// biliyorsa belge "gönderildi" işaretlenir, ikinci kez GÖNDERİLMEZ.</item>
/// <item><b>Aynı ETTN:</b> her deneme aynı ETTN ile yapılır; entegratör/GİB aynı ETTN'i ikinci kez kabul etmez.</item>
/// <item><b>Sonuç yazımı kilide bağlı:</b> sonuç yalnız kilit hâlâ bizdeyse yazılır.</item>
/// </list>
///
/// <para><b>KVKK:</b> alıcı TCKN'si yalnız bellekte çözülür ve XML'e girer; XML saklanmaz (yalnız SHA-256 özeti),
/// kayda/loga giden her metin <see cref="EDocumentPolicy.Sanitize"/>'dan geçer. Adaptör istisnası HAM loglanmaz.</para>
///
/// <para><b>Para:</b> tutarlar faturadan olduğu gibi alınır; satır toplamları fatura toplamını tutmuyorsa belge
/// üretilmez (<see cref="EDocumentInputFactory"/>) ve üreticinin toplamları faturayla ikinci kez karşılaştırılır.</para>
/// </summary>
public static class EDocumentDispatcher
{
    public const int BatchSize = 25;

    public sealed record Sonuc(int Gonderilen, int Bekleyen, int Hatali, int Taslak);

    /// <summary>Kuyrukta vadesi gelmiş, kilitsiz belge var mı (iş koşu günlüğünü yalnız iş varsa açsın diye).</summary>
    public static Task<bool> HasDueAsync(AppDbContext db, DateTimeOffset now, CancellationToken ct = default)
        => Due(db, now).AnyAsync(ct);

    private static IQueryable<EDocument> Due(AppDbContext db, DateTimeOffset now)
        => db.EDocuments.AsNoTracking()
            .Where(d => d.Durum == EDocumentStatus.Gonderilecek
                        && (d.SonrakiDenemeUtc == null || d.SonrakiDenemeUtc <= now)
                        && (d.KilitBitisUtc == null || d.KilitBitisUtc < now));

    public static async Task<Sonuc> RunAsync(
        AppDbContext db, Guid tenantId, DateTimeOffset now, EInvoiceIntegratorCatalog catalog, ISecretProtector secrets,
        ILogger log, CancellationToken ct = default, TimeZoneInfo? tz = null)
    {
        var due = await Due(db, now)
            .OrderBy(d => d.CreatedAtUtc).ThenBy(d => d.Id)
            .Select(d => d.Id).Take(BatchSize).ToListAsync(ct);
        if (due.Count == 0) return new Sonuc(0, 0, 0, 0);

        var settings = await db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var connection = EInvoiceConnection.Resolve(settings, catalog, secrets);
        int sent = 0, waiting = 0, failed = 0, draft = 0;

        foreach (var id in due)
        {
            ct.ThrowIfCancellationRequested();
            var lease = LeaseUntil(now);
            if (!await ClaimAsync(db, id, now, lease, ct)) continue; // başka bir koşu aldı
            var doc = await db.EDocuments.AsNoTracking().FirstAsync(d => d.Id == id, ct);

            Outcome outcome;
            // Entegratör çağrıları kilit dolmadan kesilir (adversarial L1): kilit süresini aşan bir çağrı, bu arada
            // yapılan yerel iptalin arkasından belgeyi teslim edebilirdi. Sonuç yazımı (FinishAsync) bu sürenin dışında.
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(EDocumentPolicy.CallBudget(lease, DateTimeOffset.UtcNow));
            try
            {
                outcome = connection.Ready
                    ? await ProcessAsync(db, doc, settings!, connection, secrets, now, lease, tz ?? TenantDay.Slice, budget.Token)
                    : Outcome.ToDraft(EInvoiceReadiness.NotConnected(connection.Gap!));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Süre doldu: belge entegratöre ulaşmış olabilir → yeniden deneme önce durum sorar (SonDenemeUtc yazılıysa).
                outcome = Outcome.Retry("Entegratör kilit süresi içinde yanıt vermedi; yeniden denenecek.");
            }
            catch (EDocumentDataException ex)
            {
                outcome = Outcome.Failed(ex.Message); // mesaj kişisel veri içermez (üretici sözleşmesi)
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or System.Data.Common.DbException
                                           or DbUpdateException or TimeoutException or InvalidOperationException))
            {
                // Beklenmeyen (kod) hatası: aynı veriyle yeniden denemek aynı sonucu verir → Hata, operatör görür.
                // İstisna nesnesi HAM loglanmaz (mesajı alıcı verisi içerebilir); yalnız tür + maskeli mesaj.
                // DB/zaman aşımı hataları yukarı geçer: kilit süresi dolunca belge yeniden alınır.
                log.LogError("e-Belge {EBelgeId}: beklenmeyen hata {Tur}: {Mesaj}", id, ex.GetType().Name, EDocumentPolicy.Sanitize(ex.Message));
                outcome = Outcome.Failed($"Beklenmeyen hata ({ex.GetType().Name}); e-belge gönderilmedi.");
            }

            var finished = !outcome.LeaseLost && await FinishAsync(db, doc, outcome, now, lease, ct);
            if (!finished)
            {
                log.LogWarning("e-Belge {EBelgeId}: sonuç yazılamadı — kilit bu sırada el değiştirdi.", id);
                continue;
            }
            switch (outcome.Status)
            {
                case EDocumentStatus.Gonderildi or EDocumentStatus.TestGonderildi: sent++; break;
                case EDocumentStatus.Gonderilecek: waiting++; break;
                case EDocumentStatus.Hata: failed++; break;
                default: draft++; break;
            }
            log.LogInformation("e-Belge {EBelgeId} (fatura {FaturaId}): {Durum}. {Mesaj}",
                id, doc.InvoiceId, outcome.Status, outcome.Message);
        }
        return new Sonuc(sent, waiting, failed, draft);
    }

    // ------------------------------------------------------------------ tek belge

    private sealed record Outcome(
        EDocumentStatus Status, string? Message, string? IntegratorRef = null, string? IntegratorStatus = null,
        bool Transient = false, bool LeaseLost = false)
    {
        public static readonly Outcome Lost = new(EDocumentStatus.Gonderilecek, null, LeaseLost: true);
        public static Outcome ToDraft(string message) => new(EDocumentStatus.Taslak, message);
        public static Outcome Failed(string message) => new(EDocumentStatus.Hata, message);
        public static Outcome Retry(string message) => new(EDocumentStatus.Gonderilecek, message, Transient: true);
        public static readonly Outcome Refunded = new(EDocumentStatus.Iptal, EDocumentPolicy.RefundedSourceCancelled);

        /// <summary>Entegratör kabul etti. TEST ortamı kabulü ayrı durumdur — GİB'e ulaşmadı (adversarial M2).</summary>
        public static Outcome Sent(bool live, string? reference, string? status) => live
            ? new(EDocumentStatus.Gonderildi, null, reference, status)
            : new(EDocumentStatus.TestGonderildi, EDocumentPolicy.TestEnvironmentSent, reference, status);
    }

    private static async Task<Outcome> ProcessAsync(
        AppDbContext db, EDocument doc, TenantSettings settings, EInvoiceConnection conn, ISecretProtector secrets,
        DateTimeOffset now, DateTimeOffset lease, TimeZoneInfo tz, CancellationToken ct)
    {
        var integrator = conn.Integrator!;
        var credentials = conn.Credentials!;

        // (1) Önceki deneme entegratöre ulaşmış olabilir → önce sor; biliyorsa YENİDEN GÖNDERME.
        // Ortam canlıdan teste çevrildiyse önceki canlı deneme canlıda da sorulur (adversarial N3).
        if (doc.SonDenemeUtc is not null)
        {
            EInvoiceStatusResult status;
            bool foundLive;
            try { (status, foundLive) = await conn.QueryPreviousAttemptAsync(doc, now, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Outcome.Retry(EDocumentPolicy.Sanitize("Entegratörde durum sorgulanamadı: " + ex.Message)!);
            }
            if (!status.Sorgulandi)
                return Outcome.Retry(EDocumentPolicy.Sanitize("Entegratörde durum sorgulanamadı: " + status.Hata)!);
            if (status.Bulundu) return Outcome.Sent(foundLive, status.EntegratorRef, status.Durum);
        }

        // (1b) İadesi kesilmiş fatura GÖNDERİLMEZ (adversarial M1): defterde geri alınmış satış GİB'e gitmesin. Kilit
        // bizde: bundan sonra commit edilmek istenen bir iade kilidi görüp reddedilir (EDocumentQueue.SettleSourceOnRefundAsync).
        // Kesinleşmemiş canlı deneme (adversarial N1): tek "bulunamadı" ile iptal edilmez, süre dolunca yeniden sorulur.
        if (await db.Invoices.AsNoTracking().AnyAsync(i => i.KaynakFaturaId == doc.InvoiceId, ct))
            return EDocumentPolicy.LiveAttemptMayStillLand(doc, now) ? Outcome.Retry(EDocumentPolicy.RefundedAwaitingSettle) : Outcome.Refunded;

        // (2) Veri — fatura değişmez; müşteri TCKN'si yalnız bellekte çözülür.
        var invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == doc.InvoiceId, ct);
        if (invoice is null) return Outcome.Failed("Fatura bulunamadı.");
        if (invoice.IadeMi) return Outcome.ToDraft(EDocumentPolicy.RefundNotSent);
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == invoice.CariId, ct);
        if (customer is null) return Outcome.Failed("Faturanın carisi bulunamadı.");
        var tckn = customer.Tip == CustomerType.Bireysel
            ? secrets.Unprotect(customer.TcKimlikEnc) ?? customer.TcKimlik // ?? eski (backfill öncesi) satır
            : null;

        // (3) Alıcı e-Fatura kullanıcısı mı? Kimlik yoksa (TCKN'siz bireysel) doğrudan e-Arşiv.
        var identity = EDocumentInputFactory.Identify(customer, tckn);
        var profile = UblProfile.EArsivFatura;
        string? recipientAlias = null;
        if (identity.Known)
        {
            EInvoiceRecipientResult recipient;
            try { recipient = await integrator.QueryRecipientAsync(credentials, identity.Id!, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Outcome.Retry(EDocumentPolicy.Sanitize("Alıcı e-Fatura sorgusu yapılamadı: " + ex.Message, tckn)!);
            }
            if (!recipient.Sorgulandi)
                return Outcome.Retry(EDocumentPolicy.Sanitize("Alıcı e-Fatura sorgusu yapılamadı: " + recipient.Hata, tckn)!);
            if (recipient.EFaturaMukellefi)
            {
                profile = EInvoiceReadiness.EInvoiceProfile(settings);
                recipientAlias = recipient.Etiketler.FirstOrDefault();
            }
        }
        var kind = profile == UblProfile.EArsivFatura ? EDocumentKind.EArsiv : EDocumentKind.EFatura;
        var live = credentials.CanliOrtam;

        // Önceki CANLI deneme kesinleşmeden tür ya da ortam değişmez (adversarial N1/N3): yeni numara/ortam kaydı önceki
        // denemenin izini silerdi; geç kabul edilen canlı belge sonra test kabulü ya da başka türle gizlenebilirdi.
        if (doc.SonDenemeCanli == true && (doc.Tur != kind || !live) && EDocumentPolicy.LiveAttemptMayStillLand(doc, now))
            return Outcome.Retry(EDocumentPolicy.AttemptNotSettled(doc, now));

        // (4) Belge — tutar kilidi: üreticinin toplamları faturanın kayıtlı toplamlarıyla birebir. Veri hatası numara
        // almadan önce yakalanır.
        var input = EDocumentInputFactory.Create(new EDocumentSource(invoice, customer, tckn, settings, doc.Ettn), profile, tz);

        // (5) GİB numarası + deneme izi TEK transaction'da, gönderimden ÖNCE kalıcı: buradan sonra çöküş → sonraki koşu
        // önce durum sorar. Numara faturanın iç numarası DEĞİL, türün kendi serisinden (e-Arşiv ≠ e-Fatura; GİB e-Arşiv
        // kılavuzu v1.18 §6); aynı tür + ortamdaki yeniden denemede aynı numara. Kilit kaybedilirse rollback numarayı geri
        // alır (boşluk yok).
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var number = doc.BelgeNo is { } kept && doc.Tur == kind && doc.SonDenemeCanli == live
            ? kept
            : await NextDocumentNoAsync(db, doc.TenantId, settings, kind, live, TimeZoneInfo.ConvertTime(invoice.Tarih, tz).Year, ct);
        var built = UblInvoiceBuilder.Build(input with { No = number });
        if (built.Toplamlar.Net != invoice.NetTutar || built.Toplamlar.Kdv != invoice.KdvTutar
            || built.Toplamlar.Odenecek != invoice.GenelToplam)
            return Outcome.Failed("e-Belge toplamı fatura toplamıyla tutmuyor; gönderilmedi (tutarlar değiştirilmez).");

        var hash = Convert.ToHexString(SHA256.HashData(built.Xml)).ToLowerInvariant();
        var marked = await db.EDocuments
            .Where(d => d.Id == doc.Id && d.KilitBitisUtc == lease)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.SonDenemeUtc, now)
                .SetProperty(d => d.SonDenemeCanli, live)
                .SetProperty(d => d.BelgeNo, number)
                .SetProperty(d => d.DenemeSayisi, d => d.DenemeSayisi + 1)
                .SetProperty(d => d.Tur, kind)
                .SetProperty(d => d.Profil, UblInvoiceBuilder.ProfileId(profile))
                .SetProperty(d => d.FaturaTipi, UblInvoiceBuilder.TypeCode(input.Tip))
                .SetProperty(d => d.XmlOzet, hash)
                .SetProperty(d => d.UpdatedAtUtc, now), ct);
        if (marked != 1) return Outcome.Lost; // kilit süresi doldu ve başka koşu aldı → gönderme, sonucu da yazma (rollback)
        await tx.CommitAsync(ct);
        doc.DenemeSayisi++;

        // (6) Gönderim.
        EInvoiceSendResult result;
        try
        {
            result = await integrator.SendAsync(credentials,
                new EInvoiceSubmission(doc.Ettn, number, kind, UblInvoiceBuilder.ProfileId(profile), input.Alici.VknTckn,
                    recipientAlias, built.Xml), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new EInvoiceSendResult(EInvoiceOutcome.GeciciHata, null, null, ex.Message);
        }

        return result.Sonuc switch
        {
            EInvoiceOutcome.Basarili => Outcome.Sent(credentials.CanliOrtam, result.EntegratorRef, result.Durum),
            EInvoiceOutcome.KaliciHata => Outcome.Failed(
                EDocumentPolicy.Sanitize("Entegratör belgeyi reddetti: " + (result.Hata ?? "sebep bildirilmedi"), tckn)!),
            _ => doc.DenemeSayisi >= EDocumentPolicy.MaxAttempts
                ? Outcome.Failed(EDocumentPolicy.Sanitize(
                    $"Deneme sınırı ({EDocumentPolicy.MaxAttempts}) doldu: " + (result.Hata ?? "entegratör yanıt vermedi"), tckn)!)
                : Outcome.Retry(EDocumentPolicy.Sanitize("Geçici hata, yeniden denenecek: " + (result.Hata ?? "entegratör yanıt vermedi"), tckn)!),
        };
    }

    /// <summary>Türün serisinden, ortamın sayacından sıradaki GİB numarası (yıl = fatura tarihinin yılı, sıra her yıl
    /// 1'den). Çağıran aktif transaction'da olmalı. Seri biçimsizse (ayar dışından yazılmış) gürültülü red.</summary>
    private static async Task<string> NextDocumentNoAsync(
        AppDbContext db, Guid tenantId, TenantSettings settings, EDocumentKind kind, bool live, int year, CancellationToken ct)
    {
        var series = EDocumentPolicy.Series(settings, kind);
        if (!Domain.Common.DocumentNo.IsSeriesValid(series))
            throw new EDocumentDataException($"e-Belge seri kodu geçersiz: '{series}'. Ayarlar ekranından düzeltin.");
        var n = await SequenceAllocator.NextAsync(db, tenantId, EDocumentPolicy.DocumentCounterKey(series, year, live), ct);
        return Domain.Common.DocumentNo.FormatInvoice(series, year, n);
    }

    // ------------------------------------------------------------------ kilit + sonuç

    /// <summary>
    /// Kilit bitişi: koşu anı ile GERÇEK saatin büyüğünden itibaren (uzun bir koşuda sonraki belgelerin kilidi koşu
    /// başına göre erken dolmasın) ve TAM SANİYEYE kırpılmış — kilit sahipliği eşitlikle (<c>KilitBitisUtc = @kilit</c>)
    /// doğrulanır; timestamptz mikrosaniye, .NET tick 100 ns olduğundan kırpılmamış değer platforma göre eşleşmeyebilirdi.
    /// </summary>
    private static DateTimeOffset LeaseUntil(DateTimeOffset now)
    {
        var start = now > DateTimeOffset.UtcNow ? now : DateTimeOffset.UtcNow;
        var end = start.ToUniversalTime() + EDocumentPolicy.Lease;
        return new DateTimeOffset(end.Ticks - end.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
    }

    private static async Task<bool> ClaimAsync(AppDbContext db, Guid id, DateTimeOffset now, DateTimeOffset lease, CancellationToken ct)
        => await db.EDocuments
            .Where(d => d.Id == id && d.Durum == EDocumentStatus.Gonderilecek && (d.KilitBitisUtc == null || d.KilitBitisUtc < now))
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.KilitBitisUtc, lease), ct) == 1;

    private static async Task<bool> FinishAsync(AppDbContext db, EDocument doc, Outcome o, DateTimeOffset now, DateTimeOffset lease, CancellationToken ct)
    {
        var next = o.Transient ? now + EDocumentPolicy.Backoff(Math.Max(doc.DenemeSayisi, 1)) : (DateTimeOffset?)null;
        var sentAt = o.Status is EDocumentStatus.Gonderildi or EDocumentStatus.TestGonderildi ? now : (DateTimeOffset?)null;
        var message = EDocumentPolicy.Sanitize(o.Message);
        var n = await db.EDocuments
            .Where(d => d.Id == doc.Id && d.KilitBitisUtc == lease)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Durum, o.Status)
                .SetProperty(d => d.KilitBitisUtc, (DateTimeOffset?)null)
                .SetProperty(d => d.SonrakiDenemeUtc, next)
                .SetProperty(d => d.SonMesaj, message)
                .SetProperty(d => d.GonderimUtc, d => sentAt ?? d.GonderimUtc)
                .SetProperty(d => d.EntegratorRef, d => o.IntegratorRef ?? d.EntegratorRef)
                .SetProperty(d => d.EntegratorDurum, d => o.IntegratorStatus ?? d.EntegratorDurum)
                .SetProperty(d => d.UpdatedAtUtc, now), ct);
        return n == 1;
    }
}
