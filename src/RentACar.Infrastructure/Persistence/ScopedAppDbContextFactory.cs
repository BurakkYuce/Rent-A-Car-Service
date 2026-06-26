using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Common;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Scope-bağlı context üreticisi: oluşturduğu her AppDbContext'e geçerli scope'un
/// ITenantContext/ICurrentUser'ını enjekte eder. Blazor Server'da circuit başına
/// tenant sabit → her işlemde taze, kısa-ömürlü, doğru-tenant'lı context.
/// (Standart AddDbContextFactory singleton olduğundan scoped servisleri yakalayamaz;
/// bu yüzden açıkça scoped bir factory yazıyoruz.)
/// </summary>
public sealed class ScopedAppDbContextFactory(
    DbContextOptions<AppDbContext> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(options, tenantContext, currentUser);
}
