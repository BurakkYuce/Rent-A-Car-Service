using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RentACar.Application.PlatformBelgeler;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Identity;
using RentACar.Infrastructure.Persistence;
using RentACar.Infrastructure.Persistence.Repositories;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Platform;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-B — Belge Merkezi (platformdan tenant'lara PDF dağıtımı).
///
/// <para><b>EN KRİTİK TEST:</b> <c>Hedefli_belge_hedef_olmayan_tenantta_ID_ile_de_INMEZ</c>.
/// <see cref="PlatformBelge"/> bir PLATFORM tablosudur — RLS YOK, merkezi query filter YOK →
/// izolasyon tamamen uygulama katmanında. Bu test kırmızıya dönerse bir tenant, belge ID'sini
/// deneyerek başka bir firmanın özel belgesini indirebiliyor demektir.</para>
///
/// Bağımsız oracle: hangi belgenin kime açık olduğu senaryodan kurulur, servisin kendi yükleminden
/// türetilmez.
///
/// <para><b>Test tasarım notu:</b> iddialar liste SAYISINA değil BELİRLİ BELGEYE bakar
/// (<c>Contains</c>/<c>DoesNotContain</c>). Sebep tasarımın kendisi: "global belge" tanımı gereği
/// tenant-ötesidir, dolayısıyla aynı veritabanını paylaşan başka bir testin global belgesi bu
/// testin tenant'ında da görünür. <c>Assert.Single</c>/<c>Empty</c> kullanmak testleri birbirine
/// bağımlı ve kırılgan yapardı.</para>
/// </summary>
[Collection("postgres")]
public sealed class BelgeMerkeziTests(PostgresFixture fx)
{
    /// <summary>Geçerli PDF imzalı bayt (`%PDF-`). İçerik decode edilmiyor, yalnız saklanıp veriliyor.</summary>
    private static byte[] Pdf(int padding = 100) => [0x25, 0x50, 0x44, 0x46, 0x2D, .. new byte[padding]];

    private PlatformAdminService Platform()
    {
        var appOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.AppConnectionString).Options;
        var factory = new ScopedAppDbContextFactory(appOptions, NullTenantContext.Instance, NullCurrentUser.Instance);
        var cache = new TenantStatusCache(new MemoryCache(new MemoryCacheOptions()), factory);
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Migrator"] = fx.OwnerConnectionString }).Build();
        return new PlatformAdminService(config, new AspNetPasswordHasher(), cache,
            NullLogger<PlatformAdminService>.Instance);
    }

    private async Task<Guid> TenantAsync(string prefix = "bm")
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options;
        await using var db = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance);
        var t = new Tenant { Code = prefix + Guid.NewGuid().ToString("N")[..10], Name = "BM", IsActive = true };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    private static PlatformDocumentService Documents(TestHost host, Guid tenantId, out IServiceScope scope,
        UserRole role = UserRole.Admin)
    {
        scope = host.ScopeFor(tenantId, role: role);
        return scope.ServiceProvider.GetRequiredService<PlatformDocumentService>();
    }

    // ---- Yaşam döngüsü ----

    [Fact]
    public async Task Yuklenen_belge_TASLAK_olur_tenant_GORMEZ()
    {
        var platform = Platform();
        var tenantId = await TenantAsync();
        await platform.UploadDocumentAsync("Kılavuz", null, "kilavuz.pdf", Pdf(), [], false, "op");

        using var host = new TestHost(fx.AppConnectionString);
        var documentId2 = (await Platform().ListDocumentsAsync()).First(b => b.Baslik == "Kılavuz").Id;
        var svc = Documents(host, tenantId, out var scope); using (scope)
        {
            // "Yükle → kontrol et → yayınla" akışı: yükleme anında canlıya ÇIKMAZ.
            Assert.DoesNotContain(await svc.ListAsync(), x => x.Id == documentId2);
            Assert.Null(await svc.DownloadAsync(documentId2));
        }
    }

    [Fact]
    public async Task Yayinlanan_GLOBAL_belge_her_tenantta_gorunur_ve_iner()
    {
        var platform = Platform();
        var t1 = await TenantAsync();
        var t2 = await TenantAsync();
        var documentId = await platform.UploadDocumentAsync("KVKK Metni", "Aydınlatma", "kvkk.pdf", Pdf(), [], false, "op");
        await platform.DocumentStatusAsync(documentId, PlatformBelgeDurum.Yayinda, "op");

        using var host = new TestHost(fx.AppConnectionString);
        foreach (var t in new[] { t1, t2 })
        {
            var svc = Documents(host, t, out var scope); using (scope)
            {
                var row = Assert.Single(await svc.ListAsync(), x => x.Id == documentId);
                Assert.Equal("KVKK Metni", row.Baslik);
                Assert.True(row.Yeni);                     // 14 gün içinde güncellendi
                Assert.NotNull(await svc.DownloadAsync(documentId));
            }
        }
    }

    [Fact]
    public async Task Arsivlenen_belge_listeden_duser_ama_kayit_DURUR()
    {
        var platform = Platform();
        var tenantId = await TenantAsync();
        var documentId = await platform.UploadDocumentAsync("Eski Fiyat Listesi", null, "f.pdf", Pdf(), [], false, "op");
        await platform.DocumentStatusAsync(documentId, PlatformBelgeDurum.Yayinda, "op");
        await platform.DocumentStatusAsync(documentId, PlatformBelgeDurum.Arsiv, "op");

        using var host = new TestHost(fx.AppConnectionString);
        var svc = Documents(host, tenantId, out var scope); using (scope)
        {
            Assert.DoesNotContain(await svc.ListAsync(), x => x.Id == documentId);
            Assert.Null(await svc.DownloadAsync(documentId));      // arşiv → indirilemez
        }
        // …ama platform tarafında kayıt ve dosya duruyor ("hangi sürümü indirdiler" cevaplanabilir).
        Assert.NotNull(await platform.DocumentContentAsync(documentId));
    }

    // ---- İZOLASYON (bu PR'ın en kritik davranışı) ----

    [Fact]
    public async Task Hedefli_belge_hedef_olmayan_tenantta_ID_ile_de_INMEZ()
    {
        var platform = Platform();
        var targetTenant = await TenantAsync("hedef");
        var foreignTenant = await TenantAsync("yabanci");

        var documentId = await platform.UploadDocumentAsync("Özel Fiyat Anlaşması", null, "ozel.pdf", Pdf(),
            [targetTenant], false, "op");
        await platform.DocumentStatusAsync(documentId, PlatformBelgeDurum.Yayinda, "op");

        using var host = new TestHost(fx.AppConnectionString);

        // Hedef tenant: görüyor VE indiriyor.
        var targetSvc = Documents(host, targetTenant, out var s1); using (s1)
        {
            Assert.Contains(await targetSvc.ListAsync(), x => x.Id == documentId);
            Assert.NotNull(await targetSvc.DownloadAsync(documentId));
        }

        // Yabancı tenant: listede YOK **ve** ID'yi bilse bile İNDİREMİYOR.
        // RLS burada korumuyor (platform tablosu) → tek savunma uygulama yüklemi.
        var foreignSvc = Documents(host, foreignTenant, out var s2); using (s2)
        {
            Assert.DoesNotContain(await foreignSvc.ListAsync(), x => x.Id == documentId);
            Assert.Null(await foreignSvc.DownloadAsync(documentId));
        }
    }

    [Fact]
    public async Task Yalniz_yoneticiler_belgesi_operatore_KAPALI()
    {
        var platform = Platform();
        var tenantId = await TenantAsync();
        var documentId = await platform.UploadDocumentAsync("Hassas Belge", null, "h.pdf", Pdf(),
            [], managersOnly: true, "op");
        await platform.DocumentStatusAsync(documentId, PlatformBelgeDurum.Yayinda, "op");

        using var host = new TestHost(fx.AppConnectionString);

        // Operatör: görmüyor ve indiremiyor.
        var opSvc = Documents(host, tenantId, out var s1, UserRole.Operator); using (s1)
        {
            Assert.DoesNotContain(await opSvc.ListAsync(), x => x.Id == documentId);
            Assert.Null(await opSvc.DownloadAsync(documentId));
        }

        // Yonetici (DİKKAT: ASCII, "Yönetici" DEĞİL — yanlış yazım kimseyi geçirmezdi): görüyor.
        var yonSvc = Documents(host, tenantId, out var s2, UserRole.Yonetici); using (s2)
        {
            Assert.Contains(await yonSvc.ListAsync(), x => x.Id == documentId);
            Assert.NotNull(await yonSvc.DownloadAsync(documentId));
        }
    }

    [Fact]
    public async Task Muhasebe_rolu_normal_belgeyi_GORUR()
    {
        var platform = Platform();
        var tenantId = await TenantAsync();
        var documentId = await platform.UploadDocumentAsync("KVKK", null, "k.pdf", Pdf(), [], false, "op");
        await platform.DocumentStatusAsync(documentId, PlatformBelgeDurum.Yayinda, "op");

        using var host = new TestHost(fx.AppConnectionString);
        // Yeni bir Permission değeri EKLENMEDİ; kapı "oturum açık" + belge bayrağı.
        var svc = Documents(host, tenantId, out var scope, UserRole.Muhasebe); using (scope)
            Assert.Contains(await svc.ListAsync(), x => x.Id == documentId);
    }

    // ---- Sürümleme ----

    [Fact]
    public async Task Yeni_surum_ayni_kaydi_guncellier_ID_DEGISMEZ()
    {
        var platform = Platform();
        var tenantId = await TenantAsync();
        var documentId = await platform.UploadDocumentAsync("Kılavuz", null, "v1.pdf", Pdf(100), [], false, "op");
        await platform.DocumentStatusAsync(documentId, PlatformBelgeDurum.Yayinda, "op");
        await platform.UpdateDocumentVersionAsync(documentId, "v2.pdf", Pdf(500), "op");

        using var host = new TestHost(fx.AppConnectionString);
        var svc = Documents(host, tenantId, out var scope); using (scope)
        {
            // TEK kayıt — yeni satır açılmadı (aynı Id, sürüm artmış).
            var row = Assert.Single(await svc.ListAsync(), x => x.Id == documentId);
            Assert.Equal(2, row.Surum);
            Assert.Equal(505, row.Boyut);                     // yeni dosyanın boyutu

            var content = await svc.DownloadAsync(documentId);          // ESKİ link çalışmaya devam ediyor
            Assert.NotNull(content);
            Assert.Equal(2, content!.Surum);                      // ETag'in sürüm bileşeni
        }
    }

    // ---- Doğrulama ----

    [Fact]
    public async Task PDF_olmayan_dosya_ve_bos_baslik_reddedilir()
    {
        var platform = Platform();
        await Assert.ThrowsAsync<Application.Common.ValidationException>(
            () => platform.UploadDocumentAsync("Kötü", null, "x.pdf", [0x4D, 0x5A, 0x90, 0x00], [], false, "op"));
        await Assert.ThrowsAsync<Application.Common.ValidationException>(
            () => platform.UploadDocumentAsync("  ", null, "x.pdf", Pdf(), [], false, "op"));
    }

    [Fact]
    public async Task Dosya_adi_ASCII_ye_indirgenir()
    {
        var platform = Platform();
        var tenantId = await TenantAsync();
        // Türkçe karakterli ad `Content-Disposition` başlığında bozulur → slug'lanır.
        var documentId = await platform.UploadDocumentAsync("Şöför Kılavuzu", null, "Şöför Kılavuzu.pdf", Pdf(), [], false, "op");
        await platform.DocumentStatusAsync(documentId, PlatformBelgeDurum.Yayinda, "op");

        using var host = new TestHost(fx.AppConnectionString);
        var svc = Documents(host, tenantId, out var scope); using (scope)
        {
            var name = (await svc.DownloadAsync(documentId))!.DosyaAdi;
            Assert.Equal("sofor-kilavuzu.pdf", name);
            Assert.All(name, c => Assert.True(char.IsAscii(c)));
        }
    }

    /// <summary>
    /// Liste sorgusu `Bytes` (bytea) kolonuna DOKUNMAZ. Blog-kapağı dersi: üstveri listesi dosya
    /// içeriğini çekerse 20 belgelik bir sayfa on-larca MB'ı DB'den uygulamaya taşır.
    /// Bağımsız oracle: iddia repository kodundan değil, PostgreSQL'e GİDEN SQL metninden okunuyor.
    /// </summary>
    [Fact]
    public async Task Liste_sorgusu_Bytes_kolonunu_SECMEZ()
    {
        var platform = Platform();
        var tenantId = await TenantAsync();
        var documentId = await platform.UploadDocumentAsync("Büyük Kılavuz", null, "b.pdf", Pdf(4000), [], false, "op");
        await platform.DocumentStatusAsync(documentId, PlatformBelgeDurum.Yayinda, "op");

        var sql = new List<string>();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fx.AppConnectionString)
            .LogTo(sql.Add, [RelationalEventId.CommandExecuted])
            .Options;
        var identity = new SystemTenantContext { TenantId = tenantId };
        var repo = new PlatformDocumentRepository(new ScopedAppDbContextFactory(options, identity, identity));

        var list = await repo.ListAsync(tenantId, isManager: true);
        Assert.Contains(list, x => x.Id == documentId);

        var sentSql = string.Join("\n", sql);
        Assert.DoesNotContain("\"Bytes\"", sentSql);   // dosya içeriği ÇEKİLMİYOR
        Assert.Contains("\"Boyut\"", sentSql);         // …ama üstveri çekiliyor (sorgu gerçekten koştu)
    }

    [Fact]
    public async Task Silinen_belgenin_hedefleri_de_duser()
    {
        var platform = Platform();
        var tenantId = await TenantAsync();
        var documentId = await platform.UploadDocumentAsync("Geçici", null, "g.pdf", Pdf(), [tenantId], false, "op");

        await platform.DeleteDocumentAsync(documentId, "op");

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options;
        await using var db = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance);
        Assert.Empty(await db.PlatformBelgeHedefler.Where(h => h.BelgeId == documentId).ToListAsync());
    }
}
