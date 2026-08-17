using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Integrations;
using RentACar.Application.Notifications;
using RentACar.Application.TenantSettings;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>Gönderim yapmadan sonucu kontrol edilebilir kılan sahte e-posta göndericisi.</summary>
public sealed class SahteEposta : IEmailSender
{
    public List<(SmtpAyar Ayar, EpostaMesaj Mesaj)> Gonderilenler { get; } = [];
    public bool Basarili { get; set; } = true;
    public string Hata { get; set; } = "sahte hata";

    public Task<EpostaSonuc> SendAsync(SmtpAyar ayar, EpostaMesaj mesaj, CancellationToken ct = default)
    {
        Gonderilenler.Add((ayar, mesaj));
        return Task.FromResult(Basarili ? new EpostaSonuc(true, null) : new EpostaSonuc(false, Hata));
    }
}

/// <summary>
/// Müşteri bildirimi — şablon çözümü, idempotency, izin kuralı, yeniden deneme.
///
/// <para>BAĞIMSIZ ORACLE: beklenen değerler elle kurulan senaryodan gelir ("aynı olay iki kez
/// tetiklenirse TEK kayıt olmalı", "şablon yoksa mesaj ÖLMEMELİ, kuyrukta beklemeli", "izin
/// kapalıysa hiç gönderilmemeli"). Gerçek SMTP'ye çıkılmaz — <see cref="SahteEposta"/> gönderimi
/// yakalar, böylece "ne gönderildi" doğrudan okunabilir.</para>
/// </summary>
[Collection("postgres")]
public sealed class MusteriBildirimTests(PostgresFixture fx)
{
    private static (TestHost Host, SahteEposta Posta) Kur(string cs)
    {
        var posta = new SahteEposta();
        var host = new TestHost(cs, s => s.AddSingleton<IEmailSender>(posta));
        return (host, posta);
    }

    private static async Task AyarYazAsync(TestHost host, Guid tenant)
    {
        using var scope = host.ScopeFor(tenant);
        await scope.ServiceProvider.GetRequiredService<TenantSettingsService>().SaveAsync(new TenantSettingsModel
        {
            FirmaUnvan = "Yüce Rent A.Ş.",
            SmtpHost = "smtp.test.local",
            SmtpPort = 587,
            SmtpGonderenAdres = "rezervasyon@yucerent.com",
        });
    }

    private static async Task SablonYazAsync(TestHost host, Guid tenant, MesajTuru tur, MesajKanal kanal,
        string konu, string govde, bool aktif = true)
    {
        using var scope = host.ScopeFor(tenant);
        await scope.ServiceProvider.GetRequiredService<MusteriBildirimService>()
            .SablonKaydetAsync(new MesajSablonInput { Tur = tur, Kanal = kanal, Konu = konu, Govde = govde, Aktif = aktif });
    }

    private static MesajIstegi Istek(string anahtar = "rez-onay:1") => new(
        MesajTuru.RezervasyonOnay, MesajKanal.Eposta, "musteri@ornek.com", anahtar,
        new Dictionary<string, string?> { ["MusteriAd"] = "Ahmet Yılmaz", ["Plaka"] = "34ABC123" },
        "Rezervasyon", Guid.NewGuid());

    [Fact]
    public async Task Sablon_ve_ayar_varsa_gonderilir_ve_yer_tutucular_dolar()
    {
        var (host, posta) = Kur(fx.AppConnectionString);
        using var _ = host;
        var tenant = Guid.NewGuid();
        await AyarYazAsync(host, tenant);
        await SablonYazAsync(host, tenant, MesajTuru.RezervasyonOnay, MesajKanal.Eposta,
            "Rezervasyonunuz alındı", "<p>Sayın {MusteriAd}, {Plaka} plakalı aracınız ayrıldı.</p>");

        using var scope = host.ScopeFor(tenant, role: UserRole.Operator);
        var sonuc = await scope.ServiceProvider.GetRequiredService<MusteriBildirimService>()
            .GonderAsync(Istek(), izinVar: true);

        Assert.True(sonuc.Gonderildi);
        var gonderilen = Assert.Single(posta.Gonderilenler);
        Assert.Equal("musteri@ornek.com", gonderilen.Mesaj.Alici);
        Assert.Equal("Rezervasyonunuz alındı", gonderilen.Mesaj.Konu);
        Assert.Contains("Ahmet Yılmaz", gonderilen.Mesaj.GovdeHtml);
        Assert.Contains("34ABC123", gonderilen.Mesaj.GovdeHtml);
        // Düz metin alternatifi de üretilmiş olmalı (çok parçalı e-posta).
        Assert.Contains("Ahmet Yılmaz", gonderilen.Mesaj.GovdeDuz);
        // Gönderen tenant ayarından çözülmüş olmalı.
        Assert.Equal("rezervasyon@yucerent.com", gonderilen.Ayar.GonderenAdres);
    }

    [Fact]
    public async Task Ayni_olay_iki_kez_tetiklenirse_tek_kayit_ve_tek_gonderim()
    {
        var (host, posta) = Kur(fx.AppConnectionString);
        using var _ = host;
        var tenant = Guid.NewGuid();
        await AyarYazAsync(host, tenant);
        await SablonYazAsync(host, tenant, MesajTuru.RezervasyonOnay, MesajKanal.Eposta, "Konu", "<p>Gövde</p>");

        using (var s1 = host.ScopeFor(tenant, role: UserRole.Operator))
            await s1.ServiceProvider.GetRequiredService<MusteriBildirimService>()
                .GonderAsync(Istek("rez-onay:tekrar"), izinVar: true);
        using (var s2 = host.ScopeFor(tenant, role: UserRole.Operator))
            await s2.ServiceProvider.GetRequiredService<MusteriBildirimService>()
                .GonderAsync(Istek("rez-onay:tekrar"), izinVar: true);

        Assert.Single(posta.Gonderilenler); // müşteri AYNI mesajı iki kez almadı

        using var scope = host.ScopeFor(tenant);
        await using var db = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal(1, await db.GidenMesajlar.CountAsync(x => x.Anahtar == "rez-onay:tekrar"));
    }

    [Fact]
    public async Task Izin_yoksa_hic_gonderilmez_ve_terminal_kayit_yazilir()
    {
        var (host, posta) = Kur(fx.AppConnectionString);
        using var _ = host;
        var tenant = Guid.NewGuid();
        await AyarYazAsync(host, tenant);
        await SablonYazAsync(host, tenant, MesajTuru.RezervasyonOnay, MesajKanal.Eposta, "Konu", "<p>Gövde</p>");

        using var scope = host.ScopeFor(tenant, role: UserRole.Operator);
        var svc = scope.ServiceProvider.GetRequiredService<MusteriBildirimService>();

        var ilk = await svc.GonderAsync(Istek("rez-onay:izinsiz"), izinVar: false);
        Assert.Equal(GidenMesajDurum.IzinYok, ilk.Durum);
        Assert.Empty(posta.Gonderilenler);

        // Terminal: izin sonradan verilse bile AYNI olay için tekrar denenmez (yeni olay yeni anahtar alır).
        var ikinci = await svc.GonderAsync(Istek("rez-onay:izinsiz"), izinVar: true);
        Assert.Equal(GidenMesajDurum.IzinYok, ikinci.Durum);
        Assert.Empty(posta.Gonderilenler);
    }

    [Fact]
    public async Task Sablon_yoksa_mesaj_olmez_kuyrukta_bekler_ve_sablon_yazilinca_gonderilir()
    {
        // Bu senaryo kurulumun İLK GÜNÜDÜR: henüz hiçbir şablon tanımlı değil. Mesajların kalıcı
        // ölmemesi, DegerlerJson kolonunun var oluş sebebidir.
        var (host, posta) = Kur(fx.AppConnectionString);
        using var _ = host;
        var tenant = Guid.NewGuid();
        await AyarYazAsync(host, tenant);

        using (var scope = host.ScopeFor(tenant, role: UserRole.Operator))
        {
            var sonuc = await scope.ServiceProvider.GetRequiredService<MusteriBildirimService>()
                .GonderAsync(Istek("rez-onay:sablonsuz"), izinVar: true);
            Assert.Equal(GidenMesajDurum.Kuyrukta, sonuc.Durum);
            Assert.Contains("şablonu tanımlı değil", sonuc.Hata);
        }
        Assert.Empty(posta.Gonderilenler);

        // Firma şablonu şimdi yazıyor.
        await SablonYazAsync(host, tenant, MesajTuru.RezervasyonOnay, MesajKanal.Eposta,
            "Rezervasyon {Plaka}", "<p>Sayın {MusteriAd}, hazır.</p>");

        // Yeniden deneme: gövde DEĞERLERDEN yeniden üretilmeli — yer tutucular dolu gitmeli.
        using (var scope = host.ScopeFor(tenant, role: UserRole.Operator))
        {
            await using var db = await scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
            var kayit = await db.GidenMesajlar.FirstAsync(x => x.Anahtar == "rez-onay:sablonsuz");
            var sonuc = await scope.ServiceProvider.GetRequiredService<MusteriBildirimService>().DeneAsync(kayit);
            Assert.True(sonuc.Gonderildi);
        }

        var gonderilen = Assert.Single(posta.Gonderilenler);
        Assert.Equal("Rezervasyon 34ABC123", gonderilen.Mesaj.Konu);
        Assert.Contains("Ahmet Yılmaz", gonderilen.Mesaj.GovdeHtml);
    }

    [Fact]
    public async Task Gonderim_hatasi_kuyrukta_birakir_ve_deneme_sayaci_artar()
    {
        var (host, posta) = Kur(fx.AppConnectionString);
        using var _ = host;
        posta.Basarili = false;
        posta.Hata = "SMTP sunucusu yanıt vermedi.";
        var tenant = Guid.NewGuid();
        await AyarYazAsync(host, tenant);
        await SablonYazAsync(host, tenant, MesajTuru.RezervasyonOnay, MesajKanal.Eposta, "Konu", "<p>Gövde</p>");

        using var scope = host.ScopeFor(tenant, role: UserRole.Operator);
        var sonuc = await scope.ServiceProvider.GetRequiredService<MusteriBildirimService>()
            .GonderAsync(Istek("rez-onay:hatali"), izinVar: true);

        Assert.Equal(GidenMesajDurum.Kuyrukta, sonuc.Durum);
        Assert.Equal("SMTP sunucusu yanıt vermedi.", sonuc.Hata);

        await using var db = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var kayit = await db.GidenMesajlar.AsNoTracking().FirstAsync(x => x.Anahtar == "rez-onay:hatali");
        Assert.Equal(1, kayit.DenemeSayisi);
        Assert.Null(kayit.GonderimUtc);
    }

    [Fact]
    public async Task Deneme_hakki_bitince_kalici_basarisiz_olur()
    {
        var (host, posta) = Kur(fx.AppConnectionString);
        using var _ = host;
        posta.Basarili = false;
        var tenant = Guid.NewGuid();
        await AyarYazAsync(host, tenant);
        await SablonYazAsync(host, tenant, MesajTuru.RezervasyonOnay, MesajKanal.Eposta, "Konu", "<p>Gövde</p>");

        using var scope = host.ScopeFor(tenant, role: UserRole.Operator);
        var svc = scope.ServiceProvider.GetRequiredService<MusteriBildirimService>();

        MesajSonuc son = null!;
        for (var i = 0; i < MusteriBildirimService.MaxDeneme; i++)
            son = await svc.GonderAsync(Istek("rez-onay:hep-hatali"), izinVar: true);

        Assert.Equal(GidenMesajDurum.Basarisiz, son.Durum);
        Assert.Equal(MusteriBildirimService.MaxDeneme, posta.Gonderilenler.Count);

        // Bir kez daha çağrılırsa artık DENENMEZ (sonsuz yeniden deneme yok).
        await svc.GonderAsync(Istek("rez-onay:hep-hatali"), izinVar: true);
        Assert.Equal(MusteriBildirimService.MaxDeneme, posta.Gonderilenler.Count);
    }

    [Fact]
    public async Task Sablon_yonetimi_ManageUsers_ister_gonderim_istemez()
    {
        var (host, posta) = Kur(fx.AppConnectionString);
        using var _ = host;
        var tenant = Guid.NewGuid();
        await AyarYazAsync(host, tenant);
        await SablonYazAsync(host, tenant, MesajTuru.RezervasyonOnay, MesajKanal.Eposta, "Konu", "<p>Gövde</p>");

        using var scope = host.ScopeFor(tenant, role: UserRole.Operator);
        var svc = scope.ServiceProvider.GetRequiredService<MusteriBildirimService>();

        // Operatör şablon YAZAMAZ...
        await Assert.ThrowsAsync<ValidationException>(() => svc.SablonKaydetAsync(
            new MesajSablonInput { Tur = MesajTuru.RezervasyonOnay, Kanal = MesajKanal.Sms, Govde = "x" }));

        // ...ama bildirim GÖNDEREBİLİR (gönderim bir operasyon yan etkisidir, ayrı izin kapısı yok).
        var sonuc = await svc.GonderAsync(Istek("rez-onay:operator"), izinVar: true);
        Assert.True(sonuc.Gonderildi);
    }

    [Fact]
    public async Task Eposta_sablonunda_konu_zorunlu_sms_de_degil()
    {
        var (host, _) = Kur(fx.AppConnectionString);
        using var __ = host;
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var svc = scope.ServiceProvider.GetRequiredService<MusteriBildirimService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.SablonKaydetAsync(
            new MesajSablonInput { Tur = MesajTuru.TalepAlindi, Kanal = MesajKanal.Eposta, Govde = "gövde" }));

        await svc.SablonKaydetAsync(
            new MesajSablonInput { Tur = MesajTuru.TalepAlindi, Kanal = MesajKanal.Sms, Govde = "kısa mesaj" });

        var sablonlar = await svc.SablonListAsync();
        Assert.Single(sablonlar);
    }

    [Fact]
    public async Task Tenant_izolasyonu_baska_tenantin_sablonunu_ve_mesajini_gormez()
    {
        var (host, _) = Kur(fx.AppConnectionString);
        using var __ = host;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await AyarYazAsync(host, a);
        await SablonYazAsync(host, a, MesajTuru.RezervasyonOnay, MesajKanal.Eposta, "A konusu", "<p>A gövdesi</p>");
        using (var scope = host.ScopeFor(a, role: UserRole.Operator))
            await scope.ServiceProvider.GetRequiredService<MusteriBildirimService>()
                .GonderAsync(Istek("rez-onay:izolasyon"), izinVar: true);

        using var bScope = host.ScopeFor(b);
        var bSvc = bScope.ServiceProvider.GetRequiredService<MusteriBildirimService>();
        Assert.Empty(await bSvc.SablonListAsync());
        Assert.Empty(await bSvc.GidenListAsync());

        // B tenant'ı A'nın anahtarını kullanabilir — anahtar TENANT İÇİNDE benzersizdir.
        await AyarYazAsync(host, b);
        await SablonYazAsync(host, b, MesajTuru.RezervasyonOnay, MesajKanal.Eposta, "B konusu", "<p>B gövdesi</p>");
        using var bScope2 = host.ScopeFor(b, role: UserRole.Operator);
        var sonuc = await bScope2.ServiceProvider.GetRequiredService<MusteriBildirimService>()
            .GonderAsync(Istek("rez-onay:izolasyon"), izinVar: true);
        Assert.True(sonuc.Gonderildi);
    }
}
