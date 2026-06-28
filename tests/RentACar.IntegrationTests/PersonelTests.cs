using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Personnel;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap C1 — Personel (PII şifreli). BAĞIMSIZ ORACLE: CRUD; PII (TC/Maaş) cipher-at-rest (ham kolon ≠
/// düz metin) + GetDetail çözer; Kod benzersizliği; güncellemede boş PII korunur; tenant izolasyon;
/// yetki (yalnız Admin = ManageUsers).
/// </summary>
[Collection("postgres")]
public sealed class PersonelTests(PostgresFixture fx)
{
    private const string Tc = "12345678901";
    private static PersonelInput Input(string kod) => new()
    {
        Kod = kod, Ad = "Ali", Soyad = "Veli", TcKimlik = Tc, Maas = 25000m, Sube = "Merkez",
        IseGiris = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
    };

    [Fact]
    public async Task Crud_with_pii_cipher_at_rest()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<PersonelService>();

        var id = await svc.CreateAsync(Input("P001"));

        var d = await svc.GetDetailAsync(id);
        Assert.NotNull(d);
        Assert.Equal("Ali", d!.Ad);
        Assert.Equal(Tc, d.TcKimlik);   // çözüldü
        Assert.Equal(25000m, d.Maas);

        // Ham kolonlar ŞİFRELİ: düz metin değil, içinde geçmiyor; protector ile çözülür.
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var raw = await db.Personeller.AsNoTracking().FirstAsync();
        Assert.NotNull(raw.TcKimlikEnc);
        Assert.NotEqual(Tc, raw.TcKimlikEnc);
        Assert.DoesNotContain(Tc, raw.TcKimlikEnc!);
        Assert.NotEqual("25000", raw.MaasEnc);
        Assert.Equal(Tc, sp.GetRequiredService<ISecretProtector>().Unprotect(raw.TcKimlikEnc));

        Assert.Single(await svc.ListAsync());
    }

    [Fact]
    public async Task Duplicate_kod_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PersonelService>();

        await svc.CreateAsync(Input("P001"));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Input("p001"))); // normalize→aynı
    }

    [Fact]
    public async Task Update_blank_pii_keeps_existing()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PersonelService>();

        var id = await svc.CreateAsync(Input("P001"));
        await svc.UpdateAsync(id, new PersonelInput { Kod = "P001", Ad = "Ali", Soyad = "Yılmaz", TcKimlik = null, Maas = null });

        var d = await svc.GetDetailAsync(id);
        Assert.Equal("Yılmaz", d!.Soyad);  // güncellendi
        Assert.Equal(Tc, d.TcKimlik);      // PII korundu
        Assert.Equal(25000m, d.Maas);
    }

    [Fact]
    public async Task Tenant_isolation()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<PersonelService>().CreateAsync(Input("P001"));

        using var s2 = host.ScopeFor(t2);
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<PersonelService>().ListAsync());
    }

    [Fact]
    public async Task Non_admin_denied()
    {
        using var host = new TestHost(fx.AppConnectionString);

        using (var op = host.ScopeFor(Guid.NewGuid(), role: UserRole.Operator))
            await Assert.ThrowsAsync<ValidationException>(
                () => op.ServiceProvider.GetRequiredService<PersonelService>().CreateAsync(Input("P001")));

        // Yönetici de ManageUsers'a sahip değil → liste bile reddedilir.
        using var yon = host.ScopeFor(Guid.NewGuid(), role: UserRole.Yonetici);
        await Assert.ThrowsAsync<ValidationException>(
            () => yon.ServiceProvider.GetRequiredService<PersonelService>().ListAsync());
    }
}
