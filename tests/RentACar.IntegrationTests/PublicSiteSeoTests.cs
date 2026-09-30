using System.Net;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RentACar.Application.Blog;
using RentACar.Application.SiteIcerik;
using RentACar.Application.Vehicles;
using RentACar.Application.WebSite;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Halka açık sitenin SEO teknik katmanı — GERÇEK PublicSite boru hattı üzerinden (PublicSiteFactory).
/// Denetim bulguları: H-1 (arama dışı yazı sitemap/llms.txt'te), M-9 (lastmod), M-3 (robots +
/// noindex), M-4 (429), M-5 (HEAD 404), L-5 (410). BAĞIMSIZ ORACLE: beklenen metinler ve zamanlar
/// testte ELLE kurulur (zaman damgaları SQL ile sabitlenir), üretim kodundan türetilmez.
/// </summary>
[Collection("postgres")]
public sealed partial class PublicSiteSeoTests(PostgresFixture fx)
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAICAIAAABPmPnhAAAAFElEQVR4nGM8YWTEgBsw4ZEb0tIAKaUBPDvSacQAAAAASUVORK5CYII=");

    private AppDbContext Owner() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options,
        NullTenantContext.Instance, NullCurrentUser.Instance);

    /// <summary>Tenant + aktif subdomain (kanonik host). Dönen host sitemap/robots'taki köktür.</summary>
    private async Task<(Guid TenantId, string Host)> SeedTenantAsync()
    {
        var host = $"seo-{Guid.NewGuid():N}.example.com";
        await using var db = Owner();
        var t = new Tenant { Code = "seo" + Guid.NewGuid().ToString("N")[..10], Name = "SEO", IsActive = true };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        db.TenantDomains.Add(new TenantDomain
        {
            TenantId = t.Id, Host = host, Kind = TenantDomainKind.Subdomain, Status = TenantDomainStatus.Active,
        });
        await db.SaveChangesAsync();
        return (t.Id, host);
    }

    /// <summary>Zaman damgasını SQL ile sabitler (uygulamanın denetim yakalayıcısı "şimdi" yazar;
    /// beklenen lastmod'u senaryodan kurmak için elle üzerine yazılır). RLS: aynı işlemde tenant GUC'u.</summary>
    private async Task SetTimeAsync(Guid tenantId, string table, Guid id, string column, DateTimeOffset value)
    {
        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var g = new NpgsqlCommand("SELECT set_config('app.tenant_id', @t, true)", conn, tx))
        {
            g.Parameters.AddWithValue("t", tenantId.ToString());
            await g.ExecuteScalarAsync();
        }
        await using (var u = new NpgsqlCommand($"UPDATE \"{table}\" SET \"{column}\" = @v WHERE \"Id\" = @id", conn, tx))
        {
            u.Parameters.AddWithValue("v", value);
            u.Parameters.AddWithValue("id", id);
            Assert.Equal(1, await u.ExecuteNonQueryAsync());
        }
        await tx.CommitAsync();
    }

    private static string W3c(DateTimeOffset t) => $"{t.UtcDateTime:yyyy-MM-dd}T{t.UtcDateTime:HH:mm:ss}Z";

    private static XNamespace Ns => "http://www.sitemaps.org/schemas/sitemap/0.9";

    private static Dictionary<string, string?> ParseSitemap(string xml)
        => XDocument.Parse(xml).Root!.Elements(Ns + "url").ToDictionary(
            u => u.Element(Ns + "loc")!.Value, u => u.Element(Ns + "lastmod")?.Value);

    /// <summary>Yayında bir ilan kurar (araç + foto + sihirbaz), VitrinIlanTests deseni.</summary>
    private static async Task<(Guid IlanId, string Slug)> SeedListingAsync(TestHost host, Guid t)
    {
        using var s = host.ScopeFor(t);
        var vehicles = s.ServiceProvider.GetRequiredService<VehicleService>();
        var photos = s.ServiceProvider.GetRequiredService<VehiclePhotoService>();
        var listings = s.ServiceProvider.GetRequiredService<WebListingService>();
        var id = await vehicles.CreateAsync(new VehicleInput
        {
            Plaka = "34SE" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
            Marka = "Fiat", Tip = "Egea", Vites = Transmission.Manuel, Yakit = FuelType.Dizel,
            ModelYili = 2023, Durum = VehicleStatus.Musait, GrupBilincliBos = true,
        });
        await photos.AddAsync(id, TinyPng);
        var signature = (await listings.PoolAsync()).Single(k => k.Araclar.Count == 1).Imza;
        var listingId = await listings.StepOneSignatureAsync([signature]);
        await listings.StepTwoAsync(listingId, 1500m, null, null, true);
        await listings.StepThreeAsync(listingId, [new OzellikSatiri("Marka", "Fiat")]);
        return (listingId, (await listings.GetAsync(listingId))!.Ilan.Slug);
    }

    private static async Task<Guid> SeedPostAsync(TestHost host, Guid t, string title, bool outOfSearch)
    {
        using var s = host.ScopeFor(t);
        var input = new BlogInput { Baslik = title, Icerik = "Gövde.", Durum = BlogPostDurum.Yayinda, AramaDisi = outOfSearch };
        return await s.ServiceProvider.GetRequiredService<BlogService>().CreateAsync(input);
    }
}
