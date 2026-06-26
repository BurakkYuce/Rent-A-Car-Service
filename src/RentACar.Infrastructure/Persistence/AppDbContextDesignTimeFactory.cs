using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// `dotnet ef migrations add` / `database update` için context üreticisi.
/// Bağlantı, RACAR_MIGRATOR_CONN env'inden (owner/migrator rolü) okunur; yoksa
/// yerel geliştirme varsayılanına düşer. migrations add bağlantıyı kullanmaz,
/// database update ise owner rolüyle bağlanır.
/// </summary>
public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("RACAR_MIGRATOR_CONN")
            ?? "Host=127.0.0.1;Port=5432;Database=racar;Username=racar_owner;Password=racar_owner_pw";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(conn)
            .Options;

        return new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance);
    }
}
