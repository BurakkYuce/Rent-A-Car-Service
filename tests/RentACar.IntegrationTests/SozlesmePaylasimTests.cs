using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Bookings;
using RentACar.Web.Reports;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-C — sözleşme paylaşım linki (anlık görüntü + anonim adres + iptal + yeni sürüm + erişim sayacı).
///
/// <para><b>EN KRİTİK TEST:</b> <c>Anonim_uc_RLS_ardindaki_PDFi_TOKEN_ile_verir</c>. Bu PR'ın tüm
/// tasarımı tek bir zorunluluktan doğuyor: link kimliksiz açılıyor, yani istekte
/// <c>app.tenant_id</c> GUC'u YOK. Bu yüzden token RLS'siz bir platform tablosunda
/// (<see cref="PaylasimLink"/>), PDF ise RLS'li tenant tablosunda (<see cref="SozlesmePdf"/>) ve
/// arasını iki-fazlı çözüm bağlıyor. Bu test kırmızıya dönerse müşteriye verilen her link ölür
/// (sessizce 0 satır — hata bile vermez).</para>
///
/// <para>Bağımsız oracle: beklenen davranış senaryodan kuruluyor ("iptal ettim → adres artık
/// çalışmamalı"), servisin kendi mantığından türetilmiyor. PDF baytı testin ELİYLE kurulan
/// bir imzadan geliyor; yalnız bir testte gerçek <see cref="PdfExportService"/> zinciri koşuyor.</para>
/// </summary>
[Collection("postgres")]
public sealed class SozlesmePaylasimTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-3).AddHours(9);

    /// <summary>Sahte ama GEÇERLİ imzalı PDF — servis içeriği decode etmiyor, yalnız saklayıp veriyor.</summary>
    private static byte[] Pdf(byte imza = 0x41) => [0x25, 0x50, 0x44, 0x46, 0x2D, .. Enumerable.Repeat(imza, 200)];

    /// <summary>
    /// Gerçek <c>Tenants</c> satırı — <see cref="PaylasimLink"/> platform tablosu olduğu için
    /// <c>TenantId</c>'de FK var; uydurma Guid'le insert patlar (doğru davranış).
    /// </summary>
    private async Task<Guid> TenantAsync(bool aktif = true)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options;
        await using var db = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance);
        var t = new Tenant { Code = "pc" + Guid.NewGuid().ToString("N")[..10], Name = "PC", IsActive = aktif };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    /// <summary>Anonim görüntüleme servisi — cookie/tenant context YOK, tıpkı müşterinin isteği gibi.</summary>
    private SozlesmeGoruntuleService Anonim()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Default"] = fx.AppConnectionString }).Build();
        return new SozlesmeGoruntuleService(config, NullLogger<SozlesmeGoruntuleService>.Instance);
    }

    private static async Task<Guid> KiraAsync(IServiceProvider sp, string plaka)
    {
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Paylas", Soyad = "Test" });
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        return await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = cari, VehicleId = veh, BasTar = Bas, BitTar = Bas.AddDays(2), GunlukUcret = 100m,
        });
    }

    // ---- EN KRİTİK: iki-fazlı çözüm gerçekten çalışıyor mu ----

    [Fact]
    public async Task Anonim_uc_RLS_ardindaki_PDFi_TOKEN_ile_verir()
    {
        var tenantId = await TenantAsync();
        using var host = new TestHost(fx.AppConnectionString);
        string token;
        using (var scope = host.ScopeFor(tenantId))
        {
            var rental = await KiraAsync(scope.ServiceProvider, "34 PC 01");
            var durum = await scope.ServiceProvider.GetRequiredService<ContractShareService>()
                .ShareAsync(rental, "RZ-PC-01", Pdf());
            token = durum.Token;
            Assert.False(durum.Bayat);              // yeni üretilen görüntü bayat olamaz
            Assert.Equal(0, durum.ErisimSayisi);
            Assert.Null(durum.SonErisimUtc);
        }

        // Buradan sonrası MÜŞTERİNİN isteği: oturum yok, tenant context yok, GUC yok.
        var sonuc = await Anonim().GoruntuleAsync(token);
        Assert.NotNull(sonuc);
        Assert.Equal(Pdf(), sonuc!.Pdf);
        Assert.Equal("Sozlesme-RZ-PC-01.pdf", sonuc.DosyaAdi);
        Assert.All(sonuc.DosyaAdi, c => Assert.True(char.IsAscii(c)));   // Content-Disposition ASCII bekler
    }

    [Fact]
    public async Task Gecersiz_ve_bos_token_null_doner()
    {
        var svc = Anonim();
        Assert.Null(await svc.GoruntuleAsync("yok-boyle-bir-token"));
        Assert.Null(await svc.GoruntuleAsync(""));
        Assert.Null(await svc.GoruntuleAsync(new string('x', 200)));      // uzunluk çiti
    }

    // ---- İZOLASYON ----

    [Fact]
    public async Task Yabanci_tenant_anlik_goruntuyu_RLS_yuzunden_GOREMEZ()
    {
        var sahip = await TenantAsync();
        var yabanci = await TenantAsync();
        using var host = new TestHost(fx.AppConnectionString);

        Guid rental;
        using (var s1 = host.ScopeFor(sahip))
        {
            rental = await KiraAsync(s1.ServiceProvider, "34 PC 02");
            await s1.ServiceProvider.GetRequiredService<ContractShareService>()
                .ShareAsync(rental, "RZ-PC-02", Pdf());
        }

        // Yabancı tenant, kira ID'sini BİLSE bile ne durumu ne PDF'i görebilir.
        using (var s2 = host.ScopeFor(yabanci))
        {
            Assert.Null(await s2.ServiceProvider.GetRequiredService<ContractShareService>()
                .StatusAsync(rental));
            var db = s2.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var ctx = await db.CreateDbContextAsync();
            // racar_app + FORCE RLS: filtre kaldırılsa DA satır gelmez (asıl savunma DB'de).
            Assert.Empty(await ctx.SozlesmePdfler.IgnoreQueryFilters().ToListAsync());
        }
    }

    [Fact]
    public async Task Pasif_tenantin_linki_CALISMAZ()
    {
        var tenantId = await TenantAsync();
        using var host = new TestHost(fx.AppConnectionString);
        string token;
        using (var scope = host.ScopeFor(tenantId))
        {
            var rental = await KiraAsync(scope.ServiceProvider, "34 PC 03");
            token = (await scope.ServiceProvider.GetRequiredService<ContractShareService>()
                .ShareAsync(rental, "RZ-PC-03", Pdf())).Token;
        }
        Assert.NotNull(await Anonim().GoruntuleAsync(token));   // önce çalışıyor

        // Firma kapatıldı → müşterinin elindeki link de durur (CalendarFeedService ile aynı kural).
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            var t = await db.Tenants.FirstAsync(x => x.Id == tenantId);
            t.IsActive = false;
            await db.SaveChangesAsync();
        }
        Assert.Null(await Anonim().GoruntuleAsync(token));
    }

    // ---- YAŞAM DÖNGÜSÜ ----

    [Fact]
    public async Task Tekrar_paylas_AYNI_tokeni_dondurur_yeni_goruntu_URETMEZ()
    {
        var tenantId = await TenantAsync();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        var svc = scope.ServiceProvider.GetRequiredService<ContractShareService>();
        var rental = await KiraAsync(scope.ServiceProvider, "34 PC 04");

        var bir = await svc.ShareAsync(rental, "RZ-PC-04", Pdf(0x41));
        var iki = await svc.ShareAsync(rental, "RZ-PC-04", Pdf(0x42));   // FARKLI bayt gönderildi

        Assert.Equal(bir.Token, iki.Token);        // müşterinin elindeki adres bozulmaz
        // …ve ikinci baytlar YAZILMADI: anlık görüntü ilk halini koruyor (aksi halde link sabit
        // ama belge değişken olurdu — paylaşımın anlamı biterdi).
        Assert.Equal(Pdf(0x41), (await Anonim().GoruntuleAsync(bir.Token))!.Pdf);
    }

    [Fact]
    public async Task Yeni_surum_ESKI_tokeni_oldurur_ve_goruntuyu_tazeler()
    {
        var tenantId = await TenantAsync();
        using var host = new TestHost(fx.AppConnectionString);
        string eski, yeni;
        using (var scope = host.ScopeFor(tenantId))
        {
            var svc = scope.ServiceProvider.GetRequiredService<ContractShareService>();
            var rental = await KiraAsync(scope.ServiceProvider, "34 PC 05");
            eski = (await svc.ShareAsync(rental, "RZ-PC-05", Pdf(0x41))).Token;
            yeni = (await svc.NewVersionAsync(rental, "RZ-PC-05", Pdf(0x42))).Token;
        }

        Assert.NotEqual(eski, yeni);
        Assert.Null(await Anonim().GoruntuleAsync(eski));                       // eski adres 404
        Assert.Equal(Pdf(0x42), (await Anonim().GoruntuleAsync(yeni))!.Pdf);    // yeni adres taze belge
    }

    [Fact]
    public async Task Iptal_adresi_olturur_PDFi_SILER_kaydi_BIRAKIR()
    {
        var tenantId = await TenantAsync();
        using var host = new TestHost(fx.AppConnectionString);
        string token;
        Guid rental;
        using (var scope = host.ScopeFor(tenantId))
        {
            var svc = scope.ServiceProvider.GetRequiredService<ContractShareService>();
            rental = await KiraAsync(scope.ServiceProvider, "34 PC 06");
            token = (await svc.ShareAsync(rental, "RZ-PC-06", Pdf())).Token;

            Assert.True(await svc.CancelAsync(rental));
            Assert.False(await svc.CancelAsync(rental));   // ikinci kez: aktif link yok
            Assert.Null(await svc.StatusAsync(rental));      // panel "link yok" gösterir

            // Anlık görüntü SİLİNDİ — iptal edilmiş paylaşım bir PDF'i süresiz taşımasın.
            var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var ctx = await f.CreateDbContextAsync();
            Assert.Empty(await ctx.SozlesmePdfler.Where(x => x.RentalId == rental).ToListAsync());
        }

        Assert.Null(await Anonim().GoruntuleAsync(token));   // adres 404

        // …ama link KAYDI duruyor (erişim geçmişi kanıt).
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options;
        await using var owner = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance);
        var link = await owner.PaylasimLinkler.AsNoTracking().FirstAsync(x => x.Token == token);
        Assert.True(link.Iptal);
    }

    [Fact]
    public async Task Iptalden_sonra_tekrar_paylasilabilir_YENI_token()
    {
        var tenantId = await TenantAsync();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        var svc = scope.ServiceProvider.GetRequiredService<ContractShareService>();
        var rental = await KiraAsync(scope.ServiceProvider, "34 PC 07");

        var bir = await svc.ShareAsync(rental, "RZ-PC-07", Pdf());
        await svc.CancelAsync(rental);
        var iki = await svc.ShareAsync(rental, "RZ-PC-07", Pdf());

        // Kısmi unique index `NOT "Iptal"` üzerinde: iptal edilen satır yeni linke yer açar.
        Assert.NotEqual(bir.Token, iki.Token);
        Assert.NotNull(await Anonim().GoruntuleAsync(iki.Token));
    }

    // ---- ERİŞİM KAYDI ----

    [Fact]
    public async Task Erisim_sayaci_her_acilista_artar_ve_IP_TUTULMAZ()
    {
        var tenantId = await TenantAsync();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        var svc = scope.ServiceProvider.GetRequiredService<ContractShareService>();
        var rental = await KiraAsync(scope.ServiceProvider, "34 PC 08");
        var token = (await svc.ShareAsync(rental, "RZ-PC-08", Pdf())).Token;

        var anonim = Anonim();
        await anonim.GoruntuleAsync(token);
        await anonim.GoruntuleAsync(token);
        await anonim.GoruntuleAsync(token);

        var durum = await svc.StatusAsync(rental);
        Assert.Equal(3, durum!.ErisimSayisi);
        Assert.NotNull(durum.SonErisimUtc);

        // KVKK: bu tabloda IP / user-agent / müşteri kimliği gibi KİŞİSEL VERİ olmamalı. Kolon kümesi
        // TAM LİSTE olarak kilitli — "hızlıca IP de tutalım" diyen bir değişiklik bu testi kırar.
        // (Alt-dize araması yapmak yanlıştı: `Iptal` içinde "ip" geçiyor.)
        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var ctx = await f.CreateDbContextAsync();
        var kolonlar = ctx.Model.FindEntityType(typeof(PaylasimLink))!.GetProperties()
            .Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(
            ["AnlikGoruntuUtc", "ErisimSayisi", "Id", "Iptal", "OlusturmaUtc", "RentalId",
             "SonErisimUtc", "SozlesmeNo", "TenantId", "Token"],
            kolonlar);
    }

    [Fact]
    public async Task Bayat_bayragi_kira_guncellenince_YANAR()
    {
        var tenantId = await TenantAsync();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<ContractShareService>();
        var rental = await KiraAsync(sp, "34 PC 09");

        Assert.False((await svc.ShareAsync(rental, "RZ-PC-09", Pdf())).Bayat);

        // Kira değişti (ör. açıklama güncellendi) → müşterinin elindeki nüsha artık ESKİ.
        await sp.GetRequiredService<RentalService>()
            .UpdateOpenAsync(rental, new RentalUpdateInput { Aciklama = "uzatildi" });

        var durum = await svc.StatusAsync(rental);
        Assert.True(durum!.Bayat);

        // Yeni sürüm bayrağı söndürür (görüntü tazelendi).
        Assert.False((await svc.NewVersionAsync(rental, "RZ-PC-09", Pdf(0x42))).Bayat);
    }

    // ---- YETKİ + DOĞRULAMA ----

    [Fact]
    public async Task Operasyon_yetkisi_olmayan_rol_PAYLASAMAZ()
    {
        var tenantId = await TenantAsync();
        using var host = new TestHost(fx.AppConnectionString);
        Guid rental;
        using (var admin = host.ScopeFor(tenantId))
            rental = await KiraAsync(admin.ServiceProvider, "34 PC 10");

        // Muhasebe rolü OperationsWrite taşımaz → hem paylaşamaz hem iptal edemez.
        using var scope = host.ScopeFor(tenantId, role: UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<ContractShareService>();
        await Assert.ThrowsAsync<Application.Common.NoPermissionException>(
            () => svc.ShareAsync(rental, "RZ-PC-10", Pdf()));
        await Assert.ThrowsAsync<Application.Common.NoPermissionException>(
            () => svc.CancelAsync(rental));
    }

    [Fact]
    public async Task Bos_PDF_ve_bos_sozlesme_no_reddedilir()
    {
        var tenantId = await TenantAsync();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        var svc = scope.ServiceProvider.GetRequiredService<ContractShareService>();
        var rental = await KiraAsync(scope.ServiceProvider, "34 PC 11");

        // PDF üretimi sessizce başarısız olursa müşteriye bozuk dosya gitmesin.
        await Assert.ThrowsAsync<Application.Common.ValidationException>(
            () => svc.ShareAsync(rental, "RZ-PC-11", []));
        await Assert.ThrowsAsync<Application.Common.ValidationException>(
            () => svc.ShareAsync(rental, "RZ-PC-11", [0x4D, 0x5A, 0x90, 0x00, 0x00]));  // PDF değil
        await Assert.ThrowsAsync<Application.Common.ValidationException>(
            () => svc.ShareAsync(rental, "  ", Pdf()));
    }

    /// <summary>
    /// Denetim izi "kim ne zaman paylaştı/iptal etti"yi taşır (KVKK: kişisel veri dışarı verildi),
    /// AMA token'ı <b>maskeler</b>. Token bir sırdır; denetim izine düz yazılsa sırrın ikinci bir
    /// kopyası daha uzun ömürlü bir tabloda yaşamaya başlardı.
    /// </summary>
    [Fact]
    public async Task Denetim_izi_paylasimi_kaydeder_ama_TOKENI_MASKELER()
    {
        var tenantId = await TenantAsync();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<ContractShareService>();
        var rental = await KiraAsync(sp, "34 PC 13");
        var token = (await svc.ShareAsync(rental, "RZ-PC-13", Pdf())).Token;
        await svc.CancelAsync(rental);

        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var ctx = await f.CreateDbContextAsync();
        var kayitlar = await ctx.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == "PaylasimLinkler")
            .Select(a => new { a.Action, Yeni = a.NewValues })
            .ToListAsync();

        var olusturma = Assert.Single(kayitlar, k => k.Action == AuditAction.Create);
        Assert.Contains("\"Token\": \"***\"", olusturma.Yeni);
        Assert.DoesNotContain(token, olusturma.Yeni);        // sır denetim izinde YOK
        Assert.Contains("RZ-PC-13", olusturma.Yeni);         // …ama olay izlenebilir

        // İptal de iz bırakıyor (adres kapandı bilgisi).
        Assert.Contains(kayitlar, k => k.Action == AuditAction.Update && k.Yeni!.Contains("\"Iptal\": true"));

        // PDF baytı denetim izine ASLA girmez — SozlesmePdf bilinçli olarak IAuditable değil.
        Assert.Empty(await ctx.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == "SozlesmePdfler").ToListAsync());
    }

    // ---- GERÇEK ZİNCİR (uçtaki akışın birebir aynısı) ----

    [Fact]
    public async Task Gercek_sozlesme_PDFi_paylasilir_ve_anonim_uctan_ayni_bayt_iner()
    {
        var tenantId = await TenantAsync();
        using var host = new TestHost(fx.AppConnectionString);
        string token;
        byte[] beklenen;
        using (var scope = host.ScopeFor(tenantId))
        {
            var sp = scope.ServiceProvider;
            var rental = await KiraAsync(sp, "34 PC 12");

            // Web ucundaki zincirin birebir aynısı: SozlesmeService → PdfExportService → PaylasAsync.
            var s = await sp.GetRequiredService<ContractService>().GetAsync(rental);
            Assert.NotNull(s);
            beklenen = new PdfExportService().Contract(s!);
            Assert.True(beklenen.Length > 1000);   // gerçek bir PDF üretildi

            token = (await sp.GetRequiredService<ContractShareService>()
                .ShareAsync(rental, s!.SozlesmeNo, beklenen)).Token;
        }

        var sonuc = await Anonim().GoruntuleAsync(token);
        Assert.NotNull(sonuc);
        Assert.Equal(beklenen, sonuc!.Pdf);        // müşteriye personelin bastığı NÜSHANIN AYNISI gider
        Assert.StartsWith("Sozlesme-", sonuc.DosyaAdi);
    }
}
