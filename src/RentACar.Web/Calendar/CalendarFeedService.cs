using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Calendar;

/// <summary>
/// Kimliksiz iCal (.ics) feed üreticisi. Cookie yok → istek scope'unda tenant context yok → RLS default-deny.
/// Bu yüzden izolasyon ELLE: token→user (Users RLS-free platform tablosu) → user.TenantId → SystemTenantContext
/// + GUC (VadeBildirimJob deseni) → tenant-owned veri EF-filter + RLS ile o tenant'a kapalı okunur. Token 32-byte
/// CSPRNG olduğundan tahmin edilemez (feed müşteri adı/plaka içerir). Pasif kullanıcı/tenant → null (404).
/// </summary>
public sealed class CalendarFeedService(IConfiguration config)
{
    public async Task<string?> BuildAsync(string token, CancellationToken ct = default)
    {
        var appConn = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default eksik.");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(appConn).Options;

        // 1) token → user (Users platform tablosu, RLS-free; anonim NullTenantContext ile okunur).
        Guid tenantId;
        string kullanici;
        string? sube; // denetim O4: Operatör feed'i uygulamadaki gibi ŞUBE-kapsamlı (yan kapı kapandı)
        await using (var db0 = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            var user = await db0.Users.AsNoTracking().IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.CalendarToken == token && u.IsActive, ct);
            if (user is null) return null;
            var tenantActive = await db0.Tenants.AsNoTracking()
                .Where(t => t.Id == user.TenantId).Select(t => t.IsActive).FirstOrDefaultAsync(ct);
            if (!tenantActive) return null; // kapatılmış tenant'ın feed'i de durur
            tenantId = user.TenantId;
            kullanici = user.DisplayName;
            // C3: el-klonu kaldırıldı — kural TEK yerde (BranchScope.EffectiveText).
            sube = RentACar.Application.Authorization.BranchScope.EffectiveText(user.Rol, user.AtanmisSube);
        }

        // 2) tenant verisi — SystemTenantContext (EF filter) + set_config GUC (RLS). Çift izolasyon.
        var sys = new SystemTenantContext { TenantId = tenantId };
        await using var db = new AppDbContext(options, sys, sys);
        await TenantGuc.OpenAsync(db, tenantId, ct); // raw-context GUC açılışı (tek doğru yol)

        var araclar = await db.Vehicles.AsNoTracking().Select(v => new { v.Id, v.Plaka, v.Sube }).ToListAsync(ct);
        var plaka = araclar.ToDictionary(v => v.Id, v => v.Plaka);
        // O4: şube-kapsamlıysa araç-bazlı vade olayları yalnız şubenin araçlarına (uygulamadaki Vehicle.Sube kuralı).
        var subeAraclar = sube is null ? null : araclar.Where(v => v.Sube == sube).Select(v => v.Id).ToHashSet();
        var ad = await db.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.DisplayName ?? "", ct);
        string P(Guid id) => plaka.GetValueOrDefault(id, "?");
        string A(Guid id) => ad.GetValueOrDefault(id, "?");
        bool AracKapsamda(Guid id) => subeAraclar is null || subeAraclar.Contains(id);

        var now = DateTimeOffset.UtcNow;
        var altSinir = now.AddDays(-120); // son 120 gün + gelecek (feed boyutu makul; geçmiş vade de görünür)
        var stamp = Utc(now);
        var sb = new StringBuilder(4096);
        sb.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//RentPro//Takvim//TR\r\nCALSCALE:GREGORIAN\r\nMETHOD:PUBLISH\r\n");
        sb.Append("X-WR-CALNAME:").Append(Esc("RentPro — " + kullanici)).Append("\r\n");

        void Timed(string uid, DateTimeOffset start, string summary)
            => sb.Append("BEGIN:VEVENT\r\nUID:").Append(uid).Append("@rentpro\r\nDTSTAMP:").Append(stamp)
                 .Append("\r\nDTSTART:").Append(Utc(start)).Append("\r\nDTEND:").Append(Utc(start.AddMinutes(30)))
                 .Append("\r\nSUMMARY:").Append(Esc(summary)).Append("\r\nEND:VEVENT\r\n");
        void AllDay(string uid, DateTimeOffset date, string summary)
            => sb.Append("BEGIN:VEVENT\r\nUID:").Append(uid).Append("@rentpro\r\nDTSTAMP:").Append(stamp)
                 .Append("\r\nDTSTART;VALUE=DATE:").Append(date.ToString("yyyyMMdd"))
                 .Append("\r\nSUMMARY:").Append(Esc(summary)).Append("\r\nEND:VEVENT\r\n");

        // Rezervasyon çıkış + dönüş (Rezerv/Onayli). Şube kapsamı: çıkış ofisi (uygulama listeleriyle aynı kural).
        foreach (var r in await db.Reservations.AsNoTracking()
            .Where(r => (r.Durum == ReservationStatus.Rezerv || r.Durum == ReservationStatus.Onayli) && r.BitTar >= altSinir
                        && (sube == null || r.CikisOfisi == sube))
            .ToListAsync(ct))
        {
            Timed($"rez-{r.Id}-cikis", r.BasTar, $"Çıkış: {P(r.VehicleId)} — {A(r.MusteriId)}");
            Timed($"rez-{r.Id}-donus", r.BitTar, $"Dönüş: {P(r.VehicleId)} — {A(r.MusteriId)}");
        }
        // Aktif kira dönüşü (Kirada, henüz dönmemiş). Şube kapsamı: çıkış ofisi.
        foreach (var r in await db.Rentals.AsNoTracking()
            .Where(r => r.Durum == RentalStatus.Kirada && r.GercekDonusTar == null
                        && (sube == null || r.CikisOfisi == sube)).ToListAsync(ct))
            Timed($"kira-{r.Id}-donus", r.BitTar, $"Dönüş (kira): {P(r.VehicleId)} — {A(r.MusteriId)}");

        // Vade: sigorta (Trafik/Kasko), muayene, MTV (ödenmemiş). Şube kapsamı: aracın şubesi.
        foreach (var p in await db.InsurancePolicies.AsNoTracking().Where(p => p.Bitis >= altSinir).ToListAsync(ct))
            if (AracKapsamda(p.VehicleId))
                AllDay($"sig-{p.Id}", p.Bitis, $"{p.Tip} bitiş: {P(p.VehicleId)}");
        foreach (var m in await db.InspectionRecords.AsNoTracking().Where(x => x.Bitis >= altSinir).ToListAsync(ct))
            if (AracKapsamda(m.VehicleId))
                AllDay($"muay-{m.Id}", m.Bitis, $"Muayene bitiş: {P(m.VehicleId)}");
        foreach (var t in await db.MtvRecords.AsNoTracking().Where(x => !x.Odendi && x.Vade >= altSinir).ToListAsync(ct))
            if (AracKapsamda(t.VehicleId))
                AllDay($"mtv-{t.Id}", t.Vade, $"MTV vade: {P(t.VehicleId)}");
        // Ceza vade (iptal/ödenmemiş olanlar). Şube kapsamında araçsız ceza gösterilmez.
        foreach (var c in await db.Penalties.AsNoTracking()
            .Where(x => x.Durum != CezaDurum.Iptal && x.Durum != CezaDurum.Odendi && x.VadeTarihi >= altSinir).ToListAsync(ct))
            if (c.VehicleId is Guid cvId ? AracKapsamda(cvId) : sube is null)
                AllDay($"ceza-{c.Id}", c.VadeTarihi, $"Ceza vade: {P(c.VehicleId ?? Guid.Empty)}");
        // Fatura ödeme vadesi — FİNANSAL: şube-kapsamlı Operatör feed'inde GÖSTERİLMEZ (O4: uygulamada da
        // Operatör finans tutarı görmez; feed yan kapı olmasın).
        if (sube is null)
            foreach (var f in await db.Invoices.AsNoTracking().Where(x => x.VadeTarihi != null && x.VadeTarihi >= altSinir).ToListAsync(ct))
                AllDay($"fat-{f.Id}", f.VadeTarihi!.Value, $"Fatura ödeme: {(f.GenelToplam * f.Kur).ToString("N0", CultureInfo.GetCultureInfo("tr-TR"))} TL"); // döviz faturada TL karşılığı (×Kur)

        sb.Append("END:VCALENDAR\r\n");
        return sb.ToString();
    }

    private static string Utc(DateTimeOffset d) => d.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
    private static string Esc(string s) =>
        s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r", "").Replace("\n", "\\n");
}
