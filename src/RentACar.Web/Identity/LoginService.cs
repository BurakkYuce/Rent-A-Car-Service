using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Identity;

public sealed record LoginResult(Tenant Tenant, User User);

/// <summary>
/// İki aşamalı login'in doğrulaması: firma kodu → tenant, sonra (tenant + kullanıcı +
/// şifre) → kullanıcı. Tenants/Users PLATFORM tablolarıdır (RLS yok) → anonim context
/// ile okunabilir. Şifre ASP.NET Core PasswordHasher ile doğrulanır.
/// </summary>
public sealed class LoginService(
    IDbContextFactory<AppDbContext> factory,
    IPasswordHasher<User> passwordHasher)
{
    public async Task<LoginResult?> ValidateAsync(
        string companyCode, string userName, string password, CancellationToken ct = default)
    {
        companyCode = (companyCode ?? string.Empty).Trim();
        userName = (userName ?? string.Empty).Trim();

        await using var db = await factory.CreateDbContextAsync(ct);

        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Code == companyCode && t.IsActive, ct);
        if (tenant is null) return null;

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.UserName == userName && u.IsActive, ct);
        if (user is null) return null;

        var verify = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verify == PasswordVerificationResult.Failed) return null;

        return new LoginResult(tenant, user);
    }
}
