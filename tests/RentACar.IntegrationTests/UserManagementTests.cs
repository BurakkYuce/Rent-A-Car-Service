using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Users;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

[Collection("postgres")]
public sealed class UserManagementTests(PostgresFixture fx)
{
    [Fact]
    public async Task Admin_creates_user_with_role_and_hashed_password()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant, Guid.NewGuid(), "admin", UserRole.Admin);
        var svc = scope.ServiceProvider.GetRequiredService<UserService>();

        var id = await svc.CreateAsync(new UserInput
        { UserName = "kasiyer", DisplayName = "Kasiyer A", Rol = UserRole.Muhasebe, Password = "gizli123" });

        var list = await svc.ListAsync();
        var created = Assert.Single(list, u => u.Id == id);
        Assert.Equal("kasiyer", created.UserName);
        Assert.Equal(UserRole.Muhasebe, created.Rol);
        Assert.True(created.IsActive);

        // Parola hash'lenmiş saklanır (düz metin değil).
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var row = await db.Users.AsNoTracking().FirstAsync(u => u.Id == id);
        Assert.NotEqual("gizli123", row.PasswordHash);
        Assert.True(scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Verify(row.PasswordHash, "gizli123"));
    }

    [Fact]
    public async Task NonAdmin_cannot_manage_users()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "op", UserRole.Operator);
        var svc = scope.ServiceProvider.GetRequiredService<UserService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.ListAsync());
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new UserInput
        { UserName = "x", Password = "gizli123", Rol = UserRole.Operator }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.SetActiveAsync(Guid.NewGuid(), false));
    }

    [Fact]
    public async Task Duplicate_username_and_weak_password_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "admin", UserRole.Admin);
        var svc = scope.ServiceProvider.GetRequiredService<UserService>();

        await svc.CreateAsync(new UserInput { UserName = "ayni", Password = "gizli123", Rol = UserRole.Operator });
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new UserInput
        { UserName = "ayni", Password = "gizli123", Rol = UserRole.Operator })); // dup
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new UserInput
        { UserName = "kisa", Password = "123", Rol = UserRole.Operator })); // zayıf parola
    }

    [Fact]
    public async Task Users_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1, Guid.NewGuid(), "a1", UserRole.Admin))
            await s1.ServiceProvider.GetRequiredService<UserService>()
                .CreateAsync(new UserInput { UserName = "t1user", Password = "gizli123", Rol = UserRole.Operator });

        // Aynı kullanıcı adı farklı tenant'ta serbest (tenant-kapsamlı benzersizlik).
        using var s2 = host.ScopeFor(t2, Guid.NewGuid(), "a2", UserRole.Admin);
        var svc2 = s2.ServiceProvider.GetRequiredService<UserService>();
        await svc2.CreateAsync(new UserInput { UserName = "t1user", Password = "gizli123", Rol = UserRole.Operator });

        // t2 yalnız kendi kullanıcısını görür.
        var list2 = await svc2.ListAsync();
        Assert.Single(list2);
        Assert.Equal("t1user", list2[0].UserName);
    }

    [Fact]
    public async Task Db_rls_blocks_authenticated_cross_tenant_user_read()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1, Guid.NewGuid(), "a1", UserRole.Admin))
            await s1.ServiceProvider.GetRequiredService<UserService>()
                .CreateAsync(new UserInput { UserName = "gizli", Password = "gizli123", Rol = UserRole.Admin });

        // t2 kimliğiyle, repo filtresini ATLAYIP raw SQL ile t1 kullanıcılarını okumayı dene.
        // RLS users_select (GUC=t2) yalnız t2 satırlarına izin verir → t1 satırı GÖRÜNMEZ.
        using var s2 = host.ScopeFor(t2, Guid.NewGuid(), "a2", UserRole.Admin);
        var factory = s2.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var foreignVisible = await db.Users
            .FromSqlRaw("SELECT * FROM \"Users\" WHERE \"UserName\" = {0}", "gizli")
            .IgnoreQueryFilters().AsNoTracking().AnyAsync();
        Assert.False(foreignVisible); // başka tenant'ın kullanıcısı DB seviyesinde görünmez
    }

    [Fact]
    public async Task Db_rls_blocks_cross_tenant_user_insert()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var foreignTenant = Guid.NewGuid();
        using var scope = host.ScopeFor(t1, Guid.NewGuid(), "admin", UserRole.Admin);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        // Repo'yu atlayıp DOĞRUDAN başka tenant için kullanıcı eklemeyi dene → RLS WITH CHECK engeller.
        db.Users.Add(new User
        {
            TenantId = foreignTenant, UserName = "sizma", DisplayName = "x",
            PasswordHash = "h", Rol = UserRole.Admin
        });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Admin_cannot_deactivate_self()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var adminId = Guid.NewGuid();
        using var scope = host.ScopeFor(Guid.NewGuid(), adminId, "admin", UserRole.Admin);
        var svc = scope.ServiceProvider.GetRequiredService<UserService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.SetActiveAsync(adminId, false));
    }

    [Fact]
    public async Task SetActive_and_reset_password_work()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "admin", UserRole.Admin);
        var svc = scope.ServiceProvider.GetRequiredService<UserService>();

        var id = await svc.CreateAsync(new UserInput { UserName = "u1", Password = "gizli123", Rol = UserRole.Operator });
        Assert.True(await svc.SetActiveAsync(id, false));
        Assert.False((await svc.ListAsync()).Single(u => u.Id == id).IsActive);

        Assert.True(await svc.ResetPasswordAsync(id, "yeniparola"));
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var row = await db.Users.AsNoTracking().FirstAsync(u => u.Id == id);
        Assert.True(scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Verify(row.PasswordHash, "yeniparola"));
    }
}
