using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Locations;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Ofis/Lokasyon master — bağımsız oracle. CRUD + kod normalize/benzersizlik + aktif filtre
/// (dropdown kaynağı) + yetki + tenant izolasyon.
/// </summary>
[Collection("postgres")]
public sealed class LocationTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_normalizes_kod_and_roundtrips()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<LocationService>();

        var id = await svc.CreateAsync(new LocationInput { Kod = "ist-hvl", Ad = "İstanbul Havalimanı", Sube = "Merkez" });
        var got = await svc.GetAsync(id);
        Assert.Equal("IST-HVL", got!.Kod);
        Assert.Equal("İstanbul Havalimanı", got.Ad);
        Assert.True(got.Aktif);
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<LocationService>();

        await svc.CreateAsync(new LocationInput { Kod = "MERKEZ", Ad = "Merkez Ofis" });
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new LocationInput { Kod = "merkez", Ad = "Başka" }));
    }

    [Fact]
    public async Task ListActive_excludes_passive_but_list_keeps_all()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<LocationService>();

        var a = await svc.CreateAsync(new LocationInput { Kod = "A", Ad = "A Ofis" });
        await svc.CreateAsync(new LocationInput { Kod = "B", Ad = "B Ofis" });
        await svc.UpdateAsync(a, new LocationInput { Kod = "A", Ad = "A Ofis", Aktif = false });

        var active = await svc.ListActiveAsync();
        Assert.Single(active);
        Assert.Equal("B", active[0].Kod);
        Assert.Equal(2, (await svc.ListAsync()).Count); // pasif korunur
    }

    [Fact]
    public async Task Validation_requires_kod_and_ad()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<LocationService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new LocationInput { Kod = "", Ad = "Ad" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new LocationInput { Kod = "X", Ad = "  " }));
    }

    [Fact]
    public async Task Delete_removes_location()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<LocationService>();

        var id = await svc.CreateAsync(new LocationInput { Kod = "DEL", Ad = "Silinecek" });
        Assert.True(await svc.DeleteAsync(id));
        Assert.Null(await svc.GetAsync(id));
    }

    [Fact]
    public async Task NonOperations_user_cannot_manage()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<LocationService>();
        await Assert.ThrowsAsync<NoPermissionException>(
            () => svc.CreateAsync(new LocationInput { Kod = "X", Ad = "Yetkisiz" }));
    }

    [Fact]
    public async Task Locations_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<LocationService>()
                .CreateAsync(new LocationInput { Kod = "T1", Ad = "Tenant1" });

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<LocationService>();
        Assert.Empty(await svc2.ListAsync());
        // Aynı kod farklı tenant'ta serbest.
        await svc2.CreateAsync(new LocationInput { Kod = "T1", Ad = "Tenant2" });
        Assert.Single(await svc2.ListAsync());
    }

    // Ofis adı anahtarı (trim + ToLowerInvariant, OfficeNameKey) kiracı içinde tekil. Beklenen anahtarlar elle:
    // .NET invariant küçültme Türkçe 'İ'/'ı'yı DEĞİŞTİRMEZ, ASCII 'I' → 'i'. "İzmir Merkez" → "İzmir merkez";
    // aşağıdakilerin hepsi aynı anahtara iner.
    [Theory]
    [InlineData("  İzmir Merkez  ")]
    [InlineData("İzmir MERKEZ")]
    [InlineData("İZMIR MERKEZ")]
    [InlineData("İzmir merkez")]
    public async Task Duplicate_office_name_variant_is_rejected_on_create_with_field_ad(string variant)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<LocationService>();

        await svc.CreateAsync(new LocationInput { Kod = "IZM", Ad = "İzmir Merkez" });
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new LocationInput { Kod = "IZM2", Ad = variant }));
        Assert.Equal("ad", ex.Alan);
        Assert.Single(await svc.ListAsync());
    }

    [Fact]
    public async Task Renaming_into_existing_office_name_is_rejected_on_both_update_paths()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<LocationService>();

        await svc.CreateAsync(new LocationInput { Kod = "IZM", Ad = "İzmir Merkez" });
        var other = await svc.CreateAsync(new LocationInput { Kod = "ANK", Ad = "Ankara" });

        var plain = await Assert.ThrowsAsync<ValidationException>(
            () => svc.UpdateAsync(other, new LocationInput { Kod = "ANK", Ad = " İzmir MERKEZ" }));
        Assert.Equal("ad", plain.Alan);
        var version = await svc.RowVersionAsync(other);
        var versioned = await Assert.ThrowsAsync<ValidationException>(
            () => svc.UpdateAsync(other, new LocationInput { Kod = "ANK", Ad = "İZMIR MERKEZ" }, version));
        Assert.Equal("ad", versioned.Alan);
        Assert.Equal("Ankara", (await svc.GetAsync(other))!.Ad);
    }

    [Fact]
    public async Task Resaving_own_office_with_name_variant_is_allowed()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<LocationService>();

        var id = await svc.CreateAsync(new LocationInput { Kod = "IZM", Ad = "İzmir Merkez" });
        Assert.True(await svc.UpdateAsync(id, new LocationInput { Kod = "IZM", Ad = "İzmir Merkez" }));
        Assert.True(await svc.UpdateAsync(id, new LocationInput { Kod = "IZM", Ad = " İZMIR merkez " }, await svc.RowVersionAsync(id)));
        Assert.Equal("İZMIR merkez", (await svc.GetAsync(id))!.Ad);
    }

    [Fact]
    public async Task Same_office_name_in_another_tenant_is_allowed()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
            await s1.ServiceProvider.GetRequiredService<LocationService>()
                .CreateAsync(new LocationInput { Kod = "IZM", Ad = "İzmir Merkez" });

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var svc2 = s2.ServiceProvider.GetRequiredService<LocationService>();
        await svc2.CreateAsync(new LocationInput { Kod = "IZM2", Ad = " İzmir MERKEZ " });
        Assert.Single(await svc2.ListAsync());
    }

    // Tekillik interceptor'la AYNI anahtarı kullanır, Türkçe kültür kuralı DEĞİL: "İzmir Merkez" → "İzmir merkez",
    // "izmir merkez" → "izmir merkez", "İZMİR MERKEZ" → "İzmİr merkez" (elle). Üç farklı anahtar → üçü de kabul;
    // ofis→şube çözümü (FindByNameAsync, interceptor'la tek kural) her adı YALNIZ kendi kaydına çözer — karışma yok.
    [Fact]
    public async Task Turkish_dotted_i_variants_have_distinct_keys_and_resolve_to_their_own_office()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<LocationService>();
        var repo = scope.ServiceProvider.GetRequiredService<ILocationRepository>();

        var a = await svc.CreateAsync(new LocationInput { Kod = "I1", Ad = "İzmir Merkez" });
        var b = await svc.CreateAsync(new LocationInput { Kod = "I2", Ad = "izmir merkez" });
        var c = await svc.CreateAsync(new LocationInput { Kod = "I3", Ad = "İZMİR MERKEZ" });

        Assert.Equal(a, (await repo.FindByNameAsync(" İzmir MERKEZ"))!.Id);
        Assert.Equal(b, (await repo.FindByNameAsync("IZMIR MERKEZ"))!.Id);
        Assert.Equal(c, (await repo.FindByNameAsync("İzmİr Merkez"))!.Id);
    }
}
