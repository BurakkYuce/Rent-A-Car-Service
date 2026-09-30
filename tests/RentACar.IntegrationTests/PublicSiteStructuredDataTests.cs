using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Blog;
using RentACar.Application.Branches;
using RentACar.Application.SiteIcerik;
using RentACar.Application.TenantSettings;
using RentACar.Application.Vehicles;
using RentACar.Application.WebSite;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Halka açık site yapısal verisi ve kanonik adresler — GERÇEK PublicSite boru hattı (PublicSiteFactory).
/// SEO denetimi: H-3 (og:url/JSON-LD istek host'undan), H-5 (AutoRental zayıf), M-7 (Car/Offer),
/// M-9 (BlogPosting dateModified), L-4 (ana sayfa FAQPage görünenden fazla).
///
/// <para>BAĞIMSIZ ORACLE: firma adı, şube il/ilçe/koordinat/saat, ilan fiyatı ve SSS sayısı testte ELLE
/// kurulur; beklenen değerler o senaryodan yazılır. Sayfa HTML'inden JSON-LD blokları ayrıştırılıp alan alan
/// karşılaştırılır. İstek TestServer host'u (localhost) ile gelir, kanonik host AYRI bir alan adıdır — adreslerin
/// istek host'undan değil kanonikten geldiği böyle ayırt edilir.</para>
/// </summary>
[Collection("postgres")]
public sealed partial class PublicSiteStructuredDataTests(PostgresFixture fx)
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAICAIAAABPmPnhAAAAFElEQVR4nGM8YWTEgBsw4ZEb0tIAKaUBPDvSacQAAAAASUVORK5CYII=");

    private const string Brand = "Kiyi Oto Yapisal";

    private async Task<(Guid Tenant, string Root)> SeedTenantAsync()
    {
        var host = $"yv-{Guid.NewGuid():N}.example.com";
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options,
            NullTenantContext.Instance, NullCurrentUser.Instance);
        var t = new Tenant { Code = "yv" + Guid.NewGuid().ToString("N")[..10], Name = Brand, IsActive = true };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        db.TenantDomains.Add(new TenantDomain
        {
            TenantId = t.Id, Host = host, Kind = TenantDomainKind.Subdomain, Status = TenantDomainStatus.Active,
        });
        await db.SaveChangesAsync();
        return (t.Id, "https://" + host);
    }

    private static async Task SeedBranchAsync(TestHost host, Guid t, string hours)
    {
        using var s = host.ScopeFor(t);
        await s.ServiceProvider.GetRequiredService<BranchService>().CreateAsync(new BranchInput
        {
            Kod = "IZM", Ad = "İzmir Merkez", Il = "İzmir", Ilce = "Konak",
            Enlem = 38.4192m, Boylam = 27.1287m, CalismaSaatleri = hours, Aktif = true,
        });
    }

    /// <summary>Yayında bir ilan: Fiat Egea 2023, günlük 1500 TL (KDV dahil), özellik "Marka: Fiat" + "Vites: Manuel".</summary>
    private static async Task<string> SeedListingAsync(TestHost host, Guid t)
    {
        using var s = host.ScopeFor(t);
        var vehicles = s.ServiceProvider.GetRequiredService<VehicleService>();
        var photos = s.ServiceProvider.GetRequiredService<VehiclePhotoService>();
        var listings = s.ServiceProvider.GetRequiredService<WebListingService>();
        var id = await vehicles.CreateAsync(new VehicleInput
        {
            Plaka = "35YV" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
            Marka = "Fiat", Tip = "Egea", Vites = Transmission.Manuel, Yakit = FuelType.Dizel,
            ModelYili = 2023, Durum = VehicleStatus.Musait, GrupBilincliBos = true,
        });
        await photos.AddAsync(id, TinyPng);
        var signature = (await listings.PoolAsync()).Single(k => k.Araclar.Count == 1).Imza;
        var listingId = await listings.StepOneSignatureAsync([signature]);
        await listings.StepTwoAsync(listingId, 1500m, null, null, true);
        await listings.StepThreeAsync(listingId,
            [new OzellikSatiri("Marka", "Fiat"), new OzellikSatiri("Vites", "Manuel")]);
        return (await listings.GetAsync(listingId))!.Ilan.Slug;
    }

    private static async Task SeedFirmAsync(TestHost host, Guid t)
    {
        using var s = host.ScopeFor(t);
        var svc = s.ServiceProvider.GetRequiredService<TenantSettingsService>();
        var m = await svc.GetAsync();
        m.FirmaMarka = Brand;
        m.FirmaTel = "0232 000 00 00";
        await svc.SaveAsync(m);
        await svc.SetLogoAsync(LogoPng());
    }

    /// <summary>Logo kurallarını geçen gerçek PNG (600×200).</summary>
    private static byte[] LogoPng()
    {
        using var bmp = new SkiaSharp.SKBitmap(600, 200);
        using (var c = new SkiaSharp.SKCanvas(bmp)) c.Clear(SkiaSharp.SKColors.SteelBlue);
        using var img = SkiaSharp.SKImage.FromBitmap(bmp);
        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [GeneratedRegex(@"<script type=""application/ld(?:\+|&#x2B;)json"">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex LdBlock();

    /// <summary>Sayfadaki tüm JSON-LD düğümleri (dizi blokları açılır).</summary>
    private static List<JsonElement> Nodes(string html)
    {
        var list = new List<JsonElement>();
        foreach (Match m in LdBlock().Matches(html))
        {
            var root = JsonDocument.Parse(m.Groups[1].Value).RootElement;
            if (root.ValueKind == JsonValueKind.Array) list.AddRange(root.EnumerateArray());
            else list.Add(root);
        }
        return list;
    }

    private static JsonElement Node(string html, string type)
        => Nodes(html).Single(n => n.GetProperty("@type").GetString() == type);

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Attr(string html, string pattern)
        => WebUtility.HtmlDecode(Regex.Match(html, pattern).Groups[1].Value);
}
