using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RentACar.Application.Observability;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Infrastructure.Identity;

/// <summary>EkIzinler/YasakIzinler: kullanıcı-bazlı istisna ADLARI (login'de tek doğruluk
/// kaynağından yüklenir; hem cookie hem JWT üreticisi buradan claim yazar — iki host ayrışamaz).</summary>
public sealed record LoginResult(
    Tenant Tenant, User User,
    IReadOnlyList<string> EkIzinler, IReadOnlyList<string> YasakIzinler);

/// <summary>
/// İki aşamalı login doğrulaması: firma kodu → tenant, sonra (tenant + kullanıcı + şifre) →
/// kullanıcı. Tenants/Users PLATFORM tablolarıdır (RLS yok) → anonim context ile okunur.
/// Şifre ASP.NET Core PasswordHasher ile doğrulanır. Web (cookie) ve API (JWT) hostları
/// ORTAK kullanır → tek doğruluk kaynağı (güvenlik-kritik, çoğaltılmaz).
/// </summary>
public sealed class LoginService(
    IDbContextFactory<AppDbContext> factory,
    IPasswordHasher<User> passwordHasher,
    ILogger<LoginService> logger)
{
    public async Task<LoginResult?> ValidateAsync(
        string companyCode, string userName, string password, CancellationToken ct = default)
    {
        companyCode = (companyCode ?? string.Empty).Trim();
        userName = (userName ?? string.Empty).Trim();

        await using var db = await factory.CreateDbContextAsync(ct);

        // KapanisTarihi KEMERİ (defense-in-depth): Kapalı firma IsActive elle true yapılsa bile giremez —
        // CloseAsync ikisini birlikte set eder ama tek bayrağa güvenmeyiz (DB anomalisi/elle müdahale).
        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Code == companyCode && t.IsActive && t.KapanisTarihiUtc == null, ct);
        if (tenant is null) { RacarMetrics.LoginFail(); return null; }

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.UserName == userName && u.IsActive, ct);
        if (user is null) { RacarMetrics.LoginFail(); return null; }

        var verify = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verify == PasswordVerificationResult.Failed) { RacarMetrics.LoginFail(); return null; }
        RacarMetrics.LoginSuccess();

        // Son giriş metriği (platform konsolu) — korumalı: metrik yazımı login'i ASLA düşürmez.
        // Users RLS'i KOMUT-BAZLI: users_select GUC-boşken açık (login bu yüzden çalışır) ama
        // users_update `TenantId = GUC` ister → GUC'suz UPDATE sessiz 0-satır olur. Çözüm: aynı
        // bağlantıda tx-yerel set_config + UPDATE (is_local=true → GUC commit'te buharlaşır,
        // havuza dönen bağlantıya tenant sızmaz).
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlAsync(
                $"SELECT set_config('app.tenant_id', {tenant.Id.ToString()}, true)", ct);
            await db.Users.Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastLoginAtUtc, DateTimeOffset.UtcNow), ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "LastLoginAtUtc yazılamadı (login etkilenmedi)."); }

        // Kullanıcı-bazlı izin istisnaları (2026-08-17). Login-bootstrap yolunda GUC yok →
        // istisna_select politikası GUC-boşken açık (Users deseni); sorgu (TenantId, UserId) ile
        // DAR okur. Buradan dönen adlar claim'e yazılır — değişiklik SONRAKİ girişte etkinleşir.
        var istisnalar = await db.KullaniciIzinIstisnalari.AsNoTracking()
            .Where(i => i.TenantId == tenant.Id && i.UserId == user.Id)
            .Select(i => new { i.Izin, i.Ver })
            .ToListAsync(ct);

        return new LoginResult(tenant, user,
            EkIzinler: istisnalar.Where(i => i.Ver).Select(i => i.Izin).ToList(),
            YasakIzinler: istisnalar.Where(i => !i.Ver).Select(i => i.Izin).ToList());
    }
}
