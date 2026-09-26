using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Blog;
using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-6: halka açık site blog. BAĞIMSIZ ORACLE — beklenen değerler elle kurulmuş senaryodan.
/// Ziyaretçi bağlamı `Role: null` (PublicTenantContext'in gerçek şekli, guard'sız public okuma).
/// </summary>
[Collection("postgres")]
public sealed class BlogServiceTests(PostgresFixture fx)
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAICAIAAABPmPnhAAAAFElEQVR4nGM8YWTEgBsw4ZEb0tIAKaUBPDvSacQAAAAASUVORK5CYII=");
    private static readonly byte[] NotAnImage = "bu bir görsel değil"u8.ToArray();

    private static async Task<Guid> CreateAsync(TestHost host, Guid tenantId, string title,
        BlogPostDurum status = BlogPostDurum.Taslak, string content = "İçerik metni.")
    {
        using var scope = host.ScopeFor(tenantId);
        var svc = scope.ServiceProvider.GetRequiredService<BlogService>();
        return await svc.CreateAsync(new BlogInput { Baslik = title, Icerik = content, Durum = status });
    }

    [Fact]
    public async Task Taslak_public_listede_slugda_ve_kapak_ucunda_gorunmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var draftId = await CreateAsync(host, tenantId, "Taslak Yazı");
        await CreateAsync(host, tenantId, "Yayında Yazı", BlogPostDurum.Yayinda);

        // Taslağa kapak yükle — public kapak ucu YİNE görmemeli (RLS taslak/yayında ayrımını BİLMEZ,
        // bu app-seviyesi bir durum; postId'yi bilen biri taslak kapağını çekememeli).
        using (var staff = host.ScopeFor(tenantId))
            await staff.ServiceProvider.GetRequiredService<BlogService>().SetCoverAsync(draftId, TinyPng);

        using var pub = host.ScopeFor(tenantId, role: null);
        var svc = pub.ServiceProvider.GetRequiredService<BlogService>();

        var list = await svc.ListPublishedAsync();
        Assert.Single(list);
        Assert.Equal("Yayında Yazı", list[0].Baslik);

        Assert.Null(await svc.GetPublishedBySlugAsync("taslak-yazi"));
        Assert.Null(await svc.GetPublishedCoverAsync(draftId)); // KAPAK UCU da taslağı reddeder
    }

    [Fact]
    public async Task Slug_turkce_dogru_uretilir_ve_tenant_icinde_benzersizdir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var id = await CreateAsync(host, tenantId, "Şirket Haberleri: İZMİR Şubemiz Açıldı!");

        using var scope = host.ScopeFor(tenantId);
        var svc = scope.ServiceProvider.GetRequiredService<BlogService>();
        var post = await svc.GetAsync(id);

        // BAĞIMSIZ ORACLE: ş→s, İ→i, Z→z... elle: "sirket-haberleri-izmir-subemiz-acildi"
        Assert.Equal("sirket-haberleri-izmir-subemiz-acildi", post!.Slug);

        // Aynı başlık ikinci kez → aynı slug → REDDEDİLİR.
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new BlogInput { Baslik = "Şirket Haberleri: İZMİR Şubemiz Açıldı!", Icerik = "x" }));
    }

    [Fact]
    public async Task Slug_ilk_yayindan_sonra_baslik_degisse_bile_donar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var id = await CreateAsync(host, tenantId, "Ilk Baslik", BlogPostDurum.Yayinda);

        using var scope = host.ScopeFor(tenantId);
        var svc = scope.ServiceProvider.GetRequiredService<BlogService>();
        var firstSlug = (await svc.GetAsync(id))!.Slug;
        Assert.Equal("ilk-baslik", firstSlug);

        // Başlığı VE slug'ı değiştirmeye çalış — yayınlandığı için slug DEĞİŞMEMELİ.
        await svc.UpdateAsync(id, new BlogInput
        { Baslik = "Tamamen Farkli Baslik", Slug = "yeni-adres", Icerik = "y", Durum = BlogPostDurum.Yayinda });

        var after = await svc.GetAsync(id);
        Assert.Equal("Tamamen Farkli Baslik", after!.Baslik); // başlık değişti
        Assert.Equal(firstSlug, after.Slug);                     // adres DONDU
    }

    [Fact]
    public async Task Yayinlanmamis_yazinin_slugu_degistirilebilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var id = await CreateAsync(host, tenantId, "Taslak Baslik"); // Taslak → YayinTarihi null

        using var scope = host.ScopeFor(tenantId);
        var svc = scope.ServiceProvider.GetRequiredService<BlogService>();
        await svc.UpdateAsync(id, new BlogInput { Baslik = "Yeni Baslik", Icerik = "z", Durum = BlogPostDurum.Taslak });

        Assert.Equal("yeni-baslik", (await svc.GetAsync(id))!.Slug);
    }

    [Fact]
    public async Task Kapak_paylasilan_ImageValidation_ile_dogrulanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var id = await CreateAsync(host, tenantId, "Kapakli Yazi", BlogPostDurum.Yayinda);

        using var scope = host.ScopeFor(tenantId);
        var svc = scope.ServiceProvider.GetRequiredService<BlogService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.SetCoverAsync(id, NotAnImage));

        await svc.SetCoverAsync(id, TinyPng);
        var cover = await svc.GetPublishedCoverAsync(id);
        Assert.NotNull(cover);
        Assert.Equal("image/png", cover!.ContentType);
    }

    [Fact]
    public async Task Liste_yayin_tarihine_gore_azalan_siralidir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        await CreateAsync(host, tenantId, "Once Yayinlanan", BlogPostDurum.Yayinda);
        await Task.Delay(20); // YayinTarihi UtcNow — ayırt edilebilir olsun
        await CreateAsync(host, tenantId, "Sonra Yayinlanan", BlogPostDurum.Yayinda);

        using var pub = host.ScopeFor(tenantId, role: null);
        var list = await pub.ServiceProvider.GetRequiredService<BlogService>().ListPublishedAsync();

        Assert.Equal(2, list.Count);
        Assert.Equal("Sonra Yayinlanan", list[0].Baslik); // en yeni ÖNCE
    }

    [Fact]
    public async Task Bilinmeyen_slug_null_doner()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var pub = host.ScopeFor(Guid.NewGuid(), role: null);
        Assert.Null(await pub.ServiceProvider.GetRequiredService<BlogService>().GetPublishedBySlugAsync("yok-boyle-bir-yazi"));
    }

    [Fact]
    public async Task OperationsWrite_olmayan_rol_yazamaz_ama_public_okuma_serbest()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        await CreateAsync(host, tenantId, "Muhasebe Testi", BlogPostDurum.Yayinda);

        using var accounting = host.ScopeFor(tenantId, role: UserRole.Muhasebe); // OperationsWrite YOK
        var svc = accounting.ServiceProvider.GetRequiredService<BlogService>();

        await Assert.ThrowsAsync<NoPermissionException>(
            () => svc.CreateAsync(new BlogInput { Baslik = "Olmaz", Icerik = "x" }));
        await Assert.ThrowsAsync<NoPermissionException>(() => svc.ListAllAsync());

        Assert.Single(await svc.ListPublishedAsync()); // public okuma GUARD'SIZ
    }

    [Fact]
    public async Task Bloglar_tenant_izoledir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        await CreateAsync(host, t1, "Tenant Bir Yazisi", BlogPostDurum.Yayinda);

        using var pub2 = host.ScopeFor(t2, role: null);
        var svc2 = pub2.ServiceProvider.GetRequiredService<BlogService>();
        Assert.Empty(await svc2.ListPublishedAsync());
        Assert.Null(await svc2.GetPublishedBySlugAsync("tenant-bir-yazisi"));

        // Aynı slug farklı tenant'ta SERBEST (benzersizlik tenant-içi).
        await CreateAsync(host, t2, "Tenant Bir Yazisi", BlogPostDurum.Yayinda);
        Assert.Single(await svc2.ListPublishedAsync());
    }

    [Fact]
    public async Task Bos_baslik_veya_icerik_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BlogService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new BlogInput { Baslik = "  ", Icerik = "x" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new BlogInput { Baslik = "Baslik", Icerik = " " }));
    }
}
