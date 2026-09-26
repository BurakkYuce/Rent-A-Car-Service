using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using RentACar.Application.Integrations;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Firma sahibine günlük operasyon özeti WhatsApp gönderimi. Kendi KISA-ömürlü context'lerini yönetir (job'un açık
/// db'sini almaz) → HTTP sırasında DB bağlantısı KAPALI (O2) + raw context'te TenantId ELLE damgalanır (K1).
/// İdempotency İstanbul günü (DateOnly) — başarısız satır ertesi denemede TEKRAR denenir (sessiz-düşme yok).
/// </summary>
public static class WhatsAppSummarySender
{
    private const string JobType = "OpOzet";
    private const string TemplateName = "operasyon_ozet";

    public static async Task SendDailyAsync(
        DbContextOptions<AppDbContext> options, Guid tenantId, IWhatsAppService wa,
        DateTimeOffset now, TimeZoneInfo tz, ILogger log, CancellationToken ct = default)
    {
        var ist = TimeZoneInfo.ConvertTime(now, tz);
        if (ist.Hour < 8) return; // O4: sabah 08:00'den önce gönderme ("0/0/0" engeli)
        var day = DateOnly.FromDateTime(ist.DateTime); // İstanbul günü — TEK kaynak (idempotency + pencere)
        var sys = new SystemTenantContext { TenantId = tenantId };

        // --- context1 (DB): ayar + kontrol + özet + slot-talep ---
        OperasyonOzet summary;
        string recipient;
        await using (var db = new AppDbContext(options, sys, sys))
        {
            await TenantGuc.OpenAsync(db, tenantId, ct);
            var s = await db.TenantSettings.FirstOrDefaultAsync(ct);
            if (s is null || !s.WhatsAppGunlukOzet || string.IsNullOrWhiteSpace(s.WhatsAppNumarasi)) return;
            recipient = Normalize(s.WhatsAppNumarasi);

            var existing = await db.WhatsAppGonderimler.FirstOrDefaultAsync(x => x.Gun == day && x.Tur == JobType, ct);
            if (existing is { Basarili: true }) return; // zaten başarıyla gönderildi (madde1: yalnız başarılıysa atla)
            summary = await OperationSummaryGenerator.BuildAsync(db, day, tz, ct);

            if (existing is null)
            {
                db.WhatsAppGonderimler.Add(new WhatsAppGonderim
                {
                    TenantId = tenantId, // K1: interceptor'sız raw context → ELLE damgala
                    Gun = day, Tur = JobType, Alici = recipient, Basarili = false
                });
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                { return; } // race: başka tetik kaptı → gönderme
            }
        } // ← bağlantı KAPANIR

        // --- HTTP (DB bağlantısı YOK) ---
        var pars = new Dictionary<string, string>
        {
            ["1"] = summary.Cikis.ToString(), ["2"] = summary.Donus.ToString(), ["3"] = summary.AcikRez.ToString(),
            ["4"] = summary.Tahsilat.ToString("N2"), ["5"] = summary.Kiradaki.ToString(),
            ["6"] = summary.Bosta.ToString(), ["7"] = summary.Serviste.ToString(),
        };
        var summaryText = $"Bugün beklenen: {summary.Cikis} çıkış, {summary.Donus} dönüş, {summary.AcikRez} açık rez. " +
                       $"Tahsilat: {summary.Tahsilat:N2} TL. Filo: {summary.Kiradaki} kirada, {summary.Bosta} boşta, {summary.Serviste} serviste.";
        bool successful;
        string? error = null;
        try { successful = await wa.SendTemplateAsync(recipient, TemplateName, pars, ct); }
        catch (Exception ex) { successful = false; error = ex.Message; log.LogWarning(ex, "WhatsApp özet gönderilemedi (tenant {T}).", tenantId); }

        // --- context2 (DB): sonucu yaz ---
        await using (var db2 = new AppDbContext(options, sys, sys))
        {
            await TenantGuc.OpenAsync(db2, tenantId, ct);
            var row = await db2.WhatsAppGonderimler.FirstOrDefaultAsync(x => x.Gun == day && x.Tur == JobType, ct);
            if (row is not null)
            {
                row.Basarili = successful;
                row.Ozet = summaryText;
                row.HataMesaji = error;
                await db2.SaveChangesAsync(ct);
            }
        }
    }

    /// <summary>Telefonu E.164'e yaklaştır (boşluk/tire sil; 0 ile başlıyorsa +90).</summary>
    private static string Normalize(string phone)
    {
        var p = new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (p.StartsWith("00")) p = "+" + p[2..];
        else if (p.StartsWith('0')) p = "+90" + p[1..];
        else if (!p.StartsWith('+')) p = "+" + p;
        return p;
    }
}
