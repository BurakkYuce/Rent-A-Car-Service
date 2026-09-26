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
public sealed class FakeEmail : IEmailSender
{
    public List<(SmtpAyar Ayar, EpostaMesaj Mesaj)> Sent { get; } = [];
    public bool Basarili { get; set; } = true;
    public string Hata { get; set; } = "sahte hata";

    public Task<EpostaSonuc> SendAsync(SmtpAyar setting, EpostaMesaj message, CancellationToken ct = default)
    {
        Sent.Add((setting, message));
        return Task.FromResult(Basarili ? new EpostaSonuc(true, null) : new EpostaSonuc(false, Hata));
    }
}

/// <summary>
/// Müşteri bildirimi — şablon çözümü, idempotency, izin kuralı, yeniden deneme.
///
/// <para>BAĞIMSIZ ORACLE: beklenen değerler elle kurulan senaryodan gelir ("aynı olay iki kez
/// tetiklenirse TEK kayıt olmalı", "şablon yoksa mesaj ÖLMEMELİ, kuyrukta beklemeli", "izin
/// kapalıysa hiç gönderilmemeli"). Gerçek SMTP'ye çıkılmaz — <see cref="FakeEmail"/> gönderimi
/// yakalar, böylece "ne gönderildi" doğrudan okunabilir.</para>
/// </summary>
[Collection("postgres")]
public sealed class MusteriBildirimTests(PostgresFixture fx)
{
    private static (TestHost Host, FakeEmail Posta) Setup(string cs)
    {
        var mail = new FakeEmail();
        var host = new TestHost(cs, s => s.AddSingleton<IEmailSender>(mail));
        return (host, mail);
    }

    private static async Task WriteSettingAsync(TestHost host, Guid tenant)
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

    private static async Task WriteTemplateAsync(TestHost host, Guid tenant, MessageType type, MessageChannel channel,
        string subject, string body, bool active = true)
    {
        using var scope = host.ScopeFor(tenant);
        await scope.ServiceProvider.GetRequiredService<CustomerNotificationService>()
            .SaveTemplateAsync(new MesajSablonInput { Tur = type, Kanal = channel, Konu = subject, Govde = body, Aktif = active });
    }

    private static MesajIstegi Request(string key = "rez-onay:1") => new(
        MessageType.RezervasyonOnay, MessageChannel.Eposta, "musteri@ornek.com", key,
        new Dictionary<string, string?> { ["MusteriAd"] = "Ahmet Yılmaz", ["Plaka"] = "34ABC123" },
        "Rezervasyon", Guid.NewGuid());

    [Fact]
    public async Task Sablon_ve_ayar_varsa_gonderilir_ve_yer_tutucular_dolar()
    {
        var (host, mail) = Setup(fx.AppConnectionString);
        using var _ = host;
        var tenant = Guid.NewGuid();
        await WriteSettingAsync(host, tenant);
        await WriteTemplateAsync(host, tenant, MessageType.RezervasyonOnay, MessageChannel.Eposta,
            "Rezervasyonunuz alındı", "<p>Sayın {MusteriAd}, {Plaka} plakalı aracınız ayrıldı.</p>");

        using var scope = host.ScopeFor(tenant, role: UserRole.Operator);
        var result = await scope.ServiceProvider.GetRequiredService<CustomerNotificationService>()
            .GonderAsync(Request(), hasPermission: true);

        Assert.True(result.Gonderildi);
        var sent = Assert.Single(mail.Sent);
        Assert.Equal("musteri@ornek.com", sent.Mesaj.Alici);
        Assert.Equal("Rezervasyonunuz alındı", sent.Mesaj.Konu);
        Assert.Contains("Ahmet Yılmaz", sent.Mesaj.GovdeHtml);
        Assert.Contains("34ABC123", sent.Mesaj.GovdeHtml);
        // Düz metin alternatifi de üretilmiş olmalı (çok parçalı e-posta).
        Assert.Contains("Ahmet Yılmaz", sent.Mesaj.GovdeDuz);
        // Gönderen tenant ayarından çözülmüş olmalı.
        Assert.Equal("rezervasyon@yucerent.com", sent.Ayar.GonderenAdres);
    }

    [Fact]
    public async Task Ayni_olay_iki_kez_tetiklenirse_tek_kayit_ve_tek_gonderim()
    {
        var (host, mail) = Setup(fx.AppConnectionString);
        using var _ = host;
        var tenant = Guid.NewGuid();
        await WriteSettingAsync(host, tenant);
        await WriteTemplateAsync(host, tenant, MessageType.RezervasyonOnay, MessageChannel.Eposta, "Konu", "<p>Gövde</p>");

        using (var s1 = host.ScopeFor(tenant, role: UserRole.Operator))
            await s1.ServiceProvider.GetRequiredService<CustomerNotificationService>()
                .GonderAsync(Request("rez-onay:tekrar"), hasPermission: true);
        using (var s2 = host.ScopeFor(tenant, role: UserRole.Operator))
            await s2.ServiceProvider.GetRequiredService<CustomerNotificationService>()
                .GonderAsync(Request("rez-onay:tekrar"), hasPermission: true);

        Assert.Single(mail.Sent); // müşteri AYNI mesajı iki kez almadı

        using var scope = host.ScopeFor(tenant);
        await using var db = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal(1, await db.GidenMesajlar.CountAsync(x => x.Anahtar == "rez-onay:tekrar"));
    }

    [Fact]
    public async Task Izin_yoksa_hic_gonderilmez_ve_terminal_kayit_yazilir()
    {
        var (host, mail) = Setup(fx.AppConnectionString);
        using var _ = host;
        var tenant = Guid.NewGuid();
        await WriteSettingAsync(host, tenant);
        await WriteTemplateAsync(host, tenant, MessageType.RezervasyonOnay, MessageChannel.Eposta, "Konu", "<p>Gövde</p>");

        using var scope = host.ScopeFor(tenant, role: UserRole.Operator);
        var svc = scope.ServiceProvider.GetRequiredService<CustomerNotificationService>();

        var first = await svc.GonderAsync(Request("rez-onay:izinsiz"), hasPermission: false);
        Assert.Equal(OutgoingMessageStatus.IzinYok, first.Durum);
        Assert.Empty(mail.Sent);

        // Terminal: izin sonradan verilse bile AYNI olay için tekrar denenmez (yeni olay yeni anahtar alır).
        var second = await svc.GonderAsync(Request("rez-onay:izinsiz"), hasPermission: true);
        Assert.Equal(OutgoingMessageStatus.IzinYok, second.Durum);
        Assert.Empty(mail.Sent);
    }

    [Fact]
    public async Task Sablon_yoksa_mesaj_olmez_kuyrukta_bekler_ve_sablon_yazilinca_gonderilir()
    {
        // Bu senaryo kurulumun İLK GÜNÜDÜR: henüz hiçbir şablon tanımlı değil. Mesajların kalıcı
        // ölmemesi, DegerlerJson kolonunun var oluş sebebidir.
        var (host, mail) = Setup(fx.AppConnectionString);
        using var _ = host;
        var tenant = Guid.NewGuid();
        await WriteSettingAsync(host, tenant);

        using (var scope = host.ScopeFor(tenant, role: UserRole.Operator))
        {
            var result = await scope.ServiceProvider.GetRequiredService<CustomerNotificationService>()
                .GonderAsync(Request("rez-onay:sablonsuz"), hasPermission: true);
            Assert.Equal(OutgoingMessageStatus.Kuyrukta, result.Durum);
            Assert.Contains("şablonu tanımlı değil", result.Hata);
        }
        Assert.Empty(mail.Sent);

        // Firma şablonu şimdi yazıyor.
        await WriteTemplateAsync(host, tenant, MessageType.RezervasyonOnay, MessageChannel.Eposta,
            "Rezervasyon {Plaka}", "<p>Sayın {MusteriAd}, hazır.</p>");

        // Yeniden deneme: gövde DEĞERLERDEN yeniden üretilmeli — yer tutucular dolu gitmeli.
        using (var scope = host.ScopeFor(tenant, role: UserRole.Operator))
        {
            await using var db = await scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
            var record = await db.GidenMesajlar.FirstAsync(x => x.Anahtar == "rez-onay:sablonsuz");
            var result = await scope.ServiceProvider.GetRequiredService<CustomerNotificationService>().TryAsync(record);
            Assert.True(result.Gonderildi);
        }

        var sent = Assert.Single(mail.Sent);
        Assert.Equal("Rezervasyon 34ABC123", sent.Mesaj.Konu);
        Assert.Contains("Ahmet Yılmaz", sent.Mesaj.GovdeHtml);
    }

    [Fact]
    public async Task Gonderim_hatasi_kuyrukta_birakir_ve_deneme_sayaci_artar()
    {
        var (host, mail) = Setup(fx.AppConnectionString);
        using var _ = host;
        mail.Basarili = false;
        mail.Hata = "SMTP sunucusu yanıt vermedi.";
        var tenant = Guid.NewGuid();
        await WriteSettingAsync(host, tenant);
        await WriteTemplateAsync(host, tenant, MessageType.RezervasyonOnay, MessageChannel.Eposta, "Konu", "<p>Gövde</p>");

        using var scope = host.ScopeFor(tenant, role: UserRole.Operator);
        var result = await scope.ServiceProvider.GetRequiredService<CustomerNotificationService>()
            .GonderAsync(Request("rez-onay:hatali"), hasPermission: true);

        Assert.Equal(OutgoingMessageStatus.Kuyrukta, result.Durum);
        Assert.Equal("SMTP sunucusu yanıt vermedi.", result.Hata);

        await using var db = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var record = await db.GidenMesajlar.AsNoTracking().FirstAsync(x => x.Anahtar == "rez-onay:hatali");
        Assert.Equal(1, record.DenemeSayisi);
        Assert.Null(record.GonderimUtc);
    }

    [Fact]
    public async Task Deneme_hakki_bitince_kalici_basarisiz_olur()
    {
        var (host, mail) = Setup(fx.AppConnectionString);
        using var _ = host;
        mail.Basarili = false;
        var tenant = Guid.NewGuid();
        await WriteSettingAsync(host, tenant);
        await WriteTemplateAsync(host, tenant, MessageType.RezervasyonOnay, MessageChannel.Eposta, "Konu", "<p>Gövde</p>");

        using var scope = host.ScopeFor(tenant, role: UserRole.Operator);
        var svc = scope.ServiceProvider.GetRequiredService<CustomerNotificationService>();

        MesajSonuc last = null!;
        for (var i = 0; i < CustomerNotificationService.MaxAttempts; i++)
            last = await svc.GonderAsync(Request("rez-onay:hep-hatali"), hasPermission: true);

        Assert.Equal(OutgoingMessageStatus.Basarisiz, last.Durum);
        Assert.Equal(CustomerNotificationService.MaxAttempts, mail.Sent.Count);

        // Bir kez daha çağrılırsa artık DENENMEZ (sonsuz yeniden deneme yok).
        await svc.GonderAsync(Request("rez-onay:hep-hatali"), hasPermission: true);
        Assert.Equal(CustomerNotificationService.MaxAttempts, mail.Sent.Count);
    }

    [Fact]
    public async Task Sablon_yonetimi_ManageUsers_ister_gonderim_istemez()
    {
        var (host, mail) = Setup(fx.AppConnectionString);
        using var _ = host;
        var tenant = Guid.NewGuid();
        await WriteSettingAsync(host, tenant);
        await WriteTemplateAsync(host, tenant, MessageType.RezervasyonOnay, MessageChannel.Eposta, "Konu", "<p>Gövde</p>");

        using var scope = host.ScopeFor(tenant, role: UserRole.Operator);
        var svc = scope.ServiceProvider.GetRequiredService<CustomerNotificationService>();

        // Operatör şablon YAZAMAZ...
        await Assert.ThrowsAsync<NoPermissionException>(() => svc.SaveTemplateAsync(
            new MesajSablonInput { Tur = MessageType.RezervasyonOnay, Kanal = MessageChannel.Sms, Govde = "x" }));

        // ...ama bildirim GÖNDEREBİLİR (gönderim bir operasyon yan etkisidir, ayrı izin kapısı yok).
        var result = await svc.GonderAsync(Request("rez-onay:operator"), hasPermission: true);
        Assert.True(result.Gonderildi);
    }

    [Fact]
    public async Task Eposta_sablonunda_konu_zorunlu_sms_de_degil()
    {
        var (host, _) = Setup(fx.AppConnectionString);
        using var __ = host;
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var svc = scope.ServiceProvider.GetRequiredService<CustomerNotificationService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.SaveTemplateAsync(
            new MesajSablonInput { Tur = MessageType.TalepAlindi, Kanal = MessageChannel.Eposta, Govde = "gövde" }));

        await svc.SaveTemplateAsync(
            new MesajSablonInput { Tur = MessageType.TalepAlindi, Kanal = MessageChannel.Sms, Govde = "kısa mesaj" });

        var templates = await svc.ListTemplatesAsync();
        Assert.Single(templates);
    }

    [Fact]
    public async Task Tenant_izolasyonu_baska_tenantin_sablonunu_ve_mesajini_gormez()
    {
        var (host, _) = Setup(fx.AppConnectionString);
        using var __ = host;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await WriteSettingAsync(host, a);
        await WriteTemplateAsync(host, a, MessageType.RezervasyonOnay, MessageChannel.Eposta, "A konusu", "<p>A gövdesi</p>");
        using (var scope = host.ScopeFor(a, role: UserRole.Operator))
            await scope.ServiceProvider.GetRequiredService<CustomerNotificationService>()
                .GonderAsync(Request("rez-onay:izolasyon"), hasPermission: true);

        using var bScope = host.ScopeFor(b);
        var bSvc = bScope.ServiceProvider.GetRequiredService<CustomerNotificationService>();
        Assert.Empty(await bSvc.ListTemplatesAsync());
        Assert.Empty(await bSvc.OutgoingListAsync());

        // B tenant'ı A'nın anahtarını kullanabilir — anahtar TENANT İÇİNDE benzersizdir.
        await WriteSettingAsync(host, b);
        await WriteTemplateAsync(host, b, MessageType.RezervasyonOnay, MessageChannel.Eposta, "B konusu", "<p>B gövdesi</p>");
        using var bScope2 = host.ScopeFor(b, role: UserRole.Operator);
        var result = await bScope2.ServiceProvider.GetRequiredService<CustomerNotificationService>()
            .GonderAsync(Request("rez-onay:izolasyon"), hasPermission: true);
        Assert.True(result.Gonderildi);
    }
}
