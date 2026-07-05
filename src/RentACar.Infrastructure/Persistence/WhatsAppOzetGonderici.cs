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
public static class WhatsAppOzetGonderici
{
    private const string Tur = "OpOzet";
    private const string TemplateName = "operasyon_ozet";

    public static async Task SendDailyAsync(
        DbContextOptions<AppDbContext> options, Guid tenantId, IWhatsAppService wa,
        DateTimeOffset now, TimeZoneInfo tz, ILogger log, CancellationToken ct = default)
    {
        var ist = TimeZoneInfo.ConvertTime(now, tz);
        if (ist.Hour < 8) return; // O4: sabah 08:00'den önce gönderme ("0/0/0" engeli)
        var gun = DateOnly.FromDateTime(ist.DateTime); // İstanbul günü — TEK kaynak (idempotency + pencere)
        var sys = new SystemTenantContext { TenantId = tenantId };

        // --- context1 (DB): ayar + kontrol + özet + slot-talep ---
        OperasyonOzet ozet;
        string alici;
        await using (var db = new AppDbContext(options, sys, sys))
        {
            await OpenWithGucAsync(db, tenantId, ct);
            var s = await db.TenantSettings.FirstOrDefaultAsync(ct);
            if (s is null || !s.WhatsAppGunlukOzet || string.IsNullOrWhiteSpace(s.WhatsAppNumarasi)) return;
            alici = Normalize(s.WhatsAppNumarasi);

            var mevcut = await db.WhatsAppGonderimler.FirstOrDefaultAsync(x => x.Gun == gun && x.Tur == Tur, ct);
            if (mevcut is { Basarili: true }) return; // zaten başarıyla gönderildi (madde1: yalnız başarılıysa atla)
            ozet = await OperasyonOzetUretici.BuildAsync(db, gun, tz, ct);

            if (mevcut is null)
            {
                db.WhatsAppGonderimler.Add(new WhatsAppGonderim
                {
                    TenantId = tenantId, // K1: interceptor'sız raw context → ELLE damgala
                    Gun = gun, Tur = Tur, Alici = alici, Basarili = false
                });
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                { return; } // race: başka tetik kaptı → gönderme
            }
        } // ← bağlantı KAPANIR

        // --- HTTP (DB bağlantısı YOK) ---
        var pars = new Dictionary<string, string>
        {
            ["1"] = ozet.Cikis.ToString(), ["2"] = ozet.Donus.ToString(), ["3"] = ozet.AcikRez.ToString(),
            ["4"] = ozet.Tahsilat.ToString("N2"), ["5"] = ozet.Kiradaki.ToString(),
            ["6"] = ozet.Bosta.ToString(), ["7"] = ozet.Serviste.ToString(),
        };
        var ozetText = $"Bugün beklenen: {ozet.Cikis} çıkış, {ozet.Donus} dönüş, {ozet.AcikRez} açık rez. " +
                       $"Tahsilat: {ozet.Tahsilat:N2} TL. Filo: {ozet.Kiradaki} kirada, {ozet.Bosta} boşta, {ozet.Serviste} serviste.";
        bool basarili;
        string? hata = null;
        try { basarili = await wa.SendTemplateAsync(alici, TemplateName, pars, ct); }
        catch (Exception ex) { basarili = false; hata = ex.Message; log.LogWarning(ex, "WhatsApp özet gönderilemedi (tenant {T}).", tenantId); }

        // --- context2 (DB): sonucu yaz ---
        await using (var db2 = new AppDbContext(options, sys, sys))
        {
            await OpenWithGucAsync(db2, tenantId, ct);
            var row = await db2.WhatsAppGonderimler.FirstOrDefaultAsync(x => x.Gun == gun && x.Tur == Tur, ct);
            if (row is not null)
            {
                row.Basarili = basarili;
                row.Ozet = ozetText;
                row.HataMesaji = hata;
                await db2.SaveChangesAsync(ct);
            }
        }
    }

    private static async Task OpenWithGucAsync(AppDbContext db, Guid tenantId, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct); // GUC bağlantı ömrünce açık kalmalı
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.tenant_id', {tenantId.ToString()}, false)", ct);
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
