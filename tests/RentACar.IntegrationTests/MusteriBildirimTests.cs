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

    private static async Task SablonYazAsync(TestHost host, Guid tenant, MessageType tur, MessageChannel kanal,
        string konu, string govde, bool aktif = true)
    {
        using var scope = host.ScopeFor(tenant);
        await scope.ServiceProvider.GetRequiredService<CustomerNotificationService>()
            .SaveTemplateAsync(new MesajSablonInput { Tur = tur, Kanal = kanal, Konu = konu, Govde = govde, Aktif = aktif });
    }

    private static MesajIstegi Istek(string anahtar = "rez-onay:1") => new(
        MessageType.RezervasyonOnay, MessageChannel.Eposta, "musteri@ornek.com", anahtar,
        new Dictionary<string, string?> { ["MusteriAd"] = "Ahmet Yılmaz", ["Plaka"] = "34ABC123" },
        "Rezervasyon", Guid.NewGuid());

    [Fact]
    public async Task Sablon_ve_ayar_varsa_gonderilir_ve_yer_tutucular_dolar()
    {
        var (host, posta) = Kur(fx.AppConnectionString);
        using var _ = host;
        var tenant = Guid.NewGuid();
        await AyarYazAsync(host, tenant);
        await SablonYazAsync(host, tenant, MessageType.RezervasyonOnay, MessageChannel.Eposta,
            "Rezervasyonunuz alındı", "<p>Sayın {MusteriAd}, {Plaka} plakalı aracınız ayrıldı.</p>");

        using var scope = host.ScopeFor(tenant, role: UserRole.Operator);
        var sonuc = await scope.ServiceProvider.GetRequiredService<CustomerNotificationService>()
            .GonderAsync(Istek(), hasPermission: true);

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
        await SablonYazAsync(host, tenant, MessageType.RezervasyonOnay, MessageChannel.Eposta, "Konu", "<p>Gövde</p>");

        using (var s1 = host.ScopeFor(tenant, role: UserRole.Operator))
            await s1.ServiceProvider.GetRequiredService<CustomerNotificationService>()
                .GonderAsync(Istek("rez-onay:tekrar"), hasPermission: true);
        using (var s2 = host.ScopeFor(tenant, role: UserRole.Operator))
            await s2.ServiceProvider.GetRequiredService<CustomerNotificationService>()
                .GonderAsync(Istek("rez-onay:tekrar"), hasPermission: true);

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
        await SablonYazAsync(host, tenant, MessageType.RezervasyonOnay, MessageChannel.Eposta, "Konu", "<p>Gövde</p>");

        using var scope = host.ScopeFor(tenant, role: UserRole.Operator);
        var svc = scope.ServiceProvider.GetRequiredService<CustomerNotificationService>();

        var ilk = await svc.GonderAsync(Istek("rez-onay:izinsiz"), hasPermission: false);
        Assert.Equal(OutgoingMessageStatus.IzinYok, ilk.Durum);
        Assert.Empty(posta.Gonderilenler);

        // Terminal: izin sonradan verilse bile AYNI olay için tekrar denenmez (yeni olay yeni anahtar alır).
        var ikinci = await svc.GonderAsync(Istek("rez-onay:izinsiz"), hasPermission: true);
        Assert.Equal(OutgoingMessageStatus.IzinYok, ikinci.Durum);
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
            var sonuc = await scope.ServiceProvider.GetRequiredService<CustomerNotificationService>()
                .GonderAsync(Istek("rez-onay:sablonsuz"), hasPermission: true);
            Assert.Equal(OutgoingMessageStatus.Kuyrukta, sonuc.Durum);
            Assert.Contains("şablonu tanımlı değil", sonuc.Hata);
        }
        Assert.Empty(posta.Gonderilenler);

        // Firma şablonu şimdi yazıyor.
        await SablonYazAsync(host, tenant, MessageType.RezervasyonOnay, MessageChannel.Eposta,
            "Rezervasyon {Plaka}", "<p>Sayın {MusteriAd}, hazır.</p>");

        // Yeniden deneme: gövde DEĞERLERDEN yeniden üretilmeli — yer tutucular dolu gitmeli.
        using (var scope = host.ScopeFor(tenant, role: UserRole.Operator))
        {
            await using var db = await scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
            var kayit = await db.GidenMesajlar.FirstAsync(x => x.Anahtar == "rez-onay:sablonsuz");
            var sonuc = await scope.ServiceProvider.GetRequiredService<CustomerNotificationService>().TryAsync(kayit);
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
        await SablonYazAsync(host, tenant, MessageType.RezervasyonOnay, MessageChannel.Eposta, "Konu", "<p>Gövde</p>");

        using var scope = host.ScopeFor(tenant, role: UserRole.Operator);
        var sonuc = await scope.ServiceProvider.GetRequiredService<CustomerNotificationService>()
            .GonderAsync(Istek("rez-onay:hatali"), hasPermission: true);

        Assert.Equal(OutgoingMessageStatus.Kuyrukta, sonuc.Durum);
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
        await SablonYazAsync(host, tenant, MessageType.RezervasyonOnay, MessageChannel.Eposta, "Konu", "<p>Gövde</p>");

        using var scope = host.ScopeFor(tenant, role: UserRole.Operator);
        var svc = scope.ServiceProvider.GetRequiredService<CustomerNotificationService>();

        MesajSonuc son = null!;
        for (var i = 0; i < CustomerNotificationService.MaxAttempts; i++)
            son = await svc.GonderAsync(Istek("rez-onay:hep-hatali"), hasPermission: true);

        Assert.Equal(OutgoingMessageStatus.Basarisiz, son.Durum);
        Assert.Equal(CustomerNotificationService.MaxAttempts, posta.Gonderilenler.Count);

        // Bir kez daha çağrılırsa artık DENENMEZ (sonsuz yeniden deneme yok).
        await svc.GonderAsync(Istek("rez-onay:hep-hatali"), hasPermission: true);
        Assert.Equal(CustomerNotificationService.MaxAttempts, posta.Gonderilenler.Count);
    }

    [Fact]
    public async Task Sablon_yonetimi_ManageUsers_ister_gonderim_istemez()
    {
        var (host, posta) = Kur(fx.AppConnectionString);
        using var _ = host;
        var tenant = Guid.NewGuid();
        await AyarYazAsync(host, tenant);
        await SablonYazAsync(host, tenant, MessageType.RezervasyonOnay, MessageChannel.Eposta, "Konu", "<p>Gövde</p>");

        using var scope = host.ScopeFor(tenant, role: UserRole.Operator);
        var svc = scope.ServiceProvider.GetRequiredService<CustomerNotificationService>();

        // Operatör şablon YAZAMAZ...
        await Assert.ThrowsAsync<NoPermissionException>(() => svc.SaveTemplateAsync(
            new MesajSablonInput { Tur = MessageType.RezervasyonOnay, Kanal = MessageChannel.Sms, Govde = "x" }));

        // ...ama bildirim GÖNDEREBİLİR (gönderim bir operasyon yan etkisidir, ayrı izin kapısı yok).
        var sonuc = await svc.GonderAsync(Istek("rez-onay:operator"), hasPermission: true);
        Assert.True(sonuc.Gonderildi);
    }

    [Fact]
    public async Task Eposta_sablonunda_konu_zorunlu_sms_de_degil()
    {
        var (host, _) = Kur(fx.AppConnectionString);
        using var __ = host;
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var svc = scope.ServiceProvider.GetRequiredService<CustomerNotificationService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.SaveTemplateAsync(
            new MesajSablonInput { Tur = MessageType.TalepAlindi, Kanal = MessageChannel.Eposta, Govde = "gövde" }));

        await svc.SaveTemplateAsync(
            new MesajSablonInput { Tur = MessageType.TalepAlindi, Kanal = MessageChannel.Sms, Govde = "kısa mesaj" });

        var sablonlar = await svc.ListTemplatesAsync();
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
        await SablonYazAsync(host, a, MessageType.RezervasyonOnay, MessageChannel.Eposta, "A konusu", "<p>A gövdesi</p>");
        using (var scope = host.ScopeFor(a, role: UserRole.Operator))
            await scope.ServiceProvider.GetRequiredService<CustomerNotificationService>()
                .GonderAsync(Istek("rez-onay:izolasyon"), hasPermission: true);

        using var bScope = host.ScopeFor(b);
        var bSvc = bScope.ServiceProvider.GetRequiredService<CustomerNotificationService>();
        Assert.Empty(await bSvc.ListTemplatesAsync());
        Assert.Empty(await bSvc.OutgoingListAsync());

        // B tenant'ı A'nın anahtarını kullanabilir — anahtar TENANT İÇİNDE benzersizdir.
        await AyarYazAsync(host, b);
        await SablonYazAsync(host, b, MessageType.RezervasyonOnay, MessageChannel.Eposta, "B konusu", "<p>B gövdesi</p>");
        using var bScope2 = host.ScopeFor(b, role: UserRole.Operator);
        var sonuc = await bScope2.ServiceProvider.GetRequiredService<CustomerNotificationService>()
            .GonderAsync(Istek("rez-onay:izolasyon"), hasPermission: true);
        Assert.True(sonuc.Gonderildi);
    }
}
