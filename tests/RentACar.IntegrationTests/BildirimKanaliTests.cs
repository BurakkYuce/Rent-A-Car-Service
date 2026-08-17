using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Integrations;
using RentACar.Application.TenantSettings;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Bildirim omurgası — tenant SMTP/SMS ayarının gönderime çözülmesi.
///
/// <para>BAĞIMSIZ ORACLE: beklenen değerler <see cref="BildirimKanaliService"/>'ten değil, elle kurulan
/// senaryodan gelir — "port yazılmadıysa 587", "gönderen adres yoksa kullanıcı adı e-posta biçimindeyse
/// o", "host yoksa hiç gönderme". Kurallar burada sabitlenir; servis onlara uymak zorundadır.</para>
///
/// <para>Ayrıca bir YETKİ regresyonunu kilitler: bu servis <see cref="TenantSettingsService"/> üzerinden
/// geçmez (o ManageUsers ister), çünkü bildirimi tetikleyen akışlar operatör yetkisiyle ya da hiç
/// kullanıcı bağlamı olmadan çalışır. Testler Operatör rolüyle koşar — servis admin izni isteseydi
/// burada patlardı.</para>
/// </summary>
[Collection("postgres")]
public sealed class BildirimKanaliTests(PostgresFixture fx)
{
    private const string SmtpSifre = "smtp-gizli-42";

    /// <summary>Ayarları admin olarak yazar (yazma yolu ManageUsers ister), sonra tenant id'yi döner.</summary>
    private static async Task AyarYazAsync(TestHost host, Guid tenant, TenantSettingsModel m)
    {
        using var scope = host.ScopeFor(tenant);
        await scope.ServiceProvider.GetRequiredService<TenantSettingsService>().SaveAsync(m);
    }

    [Fact]
    public async Task Smtp_ayari_yoksa_null_ve_gonderim_acik_hata_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();

        using var scope = host.ScopeFor(tenant, role: UserRole.Operator);
        var kanal = scope.ServiceProvider.GetRequiredService<BildirimKanaliService>();

        Assert.Null(await kanal.SmtpAyarAsync());

        var sonuc = await kanal.EpostaGonderAsync("musteri@ornek.com", "konu", "<p>gövde</p>");
        Assert.False(sonuc.Ok);
        Assert.False(string.IsNullOrWhiteSpace(sonuc.Hata)); // sessiz başarı YOK
    }

    [Fact]
    public async Task Smtp_ayari_cozulur_sifre_desifre_port_varsayilani_587()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        await AyarYazAsync(host, tenant, new TenantSettingsModel
        {
            FirmaUnvan = "Yüce Rent A.Ş.",
            SmtpHost = "mail.yucerent.com",
            SmtpPort = null,                       // yazılmadı → 587 beklenir
            SmtpKullanici = "no-reply@yucerent.com",
            SmtpSifre = SmtpSifre,
            SmtpSsl = true,
            SmtpGonderenAdres = "rezervasyon@yucerent.com",
            SmtpGonderenAd = "Yüce Rent Rezervasyon",
        });

        using var scope = host.ScopeFor(tenant, role: UserRole.Operator);
        var ayar = await scope.ServiceProvider.GetRequiredService<BildirimKanaliService>().SmtpAyarAsync();

        Assert.NotNull(ayar);
        Assert.Equal("mail.yucerent.com", ayar!.Host);
        Assert.Equal(587, ayar.Port);
        Assert.True(ayar.Ssl);
        Assert.Equal("no-reply@yucerent.com", ayar.Kullanici);
        Assert.Equal(SmtpSifre, ayar.Sifre); // at-rest şifreliydi, gönderim için çözüldü
        Assert.Equal("rezervasyon@yucerent.com", ayar.GonderenAdres);
        Assert.Equal("Yüce Rent Rezervasyon", ayar.GonderenAd);
    }

    [Fact]
    public async Task Gonderen_adres_yoksa_kullanici_eposta_bicimindeyse_ona_duser()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        await AyarYazAsync(host, tenant, new TenantSettingsModel
        {
            FirmaUnvan = "Demo Kiralama",
            SmtpHost = "smtp.ornek.com",
            SmtpPort = 465,
            SmtpKullanici = "bilgi@demo.com",   // e-posta biçiminde → gönderen olarak kullanılabilir
            SmtpGonderenAdres = null,
            SmtpGonderenAd = null,
        });

        using var scope = host.ScopeFor(tenant, role: UserRole.Operator);
        var ayar = await scope.ServiceProvider.GetRequiredService<BildirimKanaliService>().SmtpAyarAsync();

        Assert.NotNull(ayar);
        Assert.Equal("bilgi@demo.com", ayar!.GonderenAdres);
        Assert.Equal("Demo Kiralama", ayar.GonderenAd); // gönderen adı boşsa firma unvanı
        Assert.Equal(465, ayar.Port);
    }

    [Fact]
    public async Task Gonderen_adres_de_kullanici_da_eposta_degilse_gondermez()
    {
        // Uydurma bir "Kimden" ÜRETİLMEZ: SPF/DKIM uyumsuz gönderen sessizce spam'e düşer,
        // yani operatör "gönderdim" sanır, müşteri hiçbir şey almaz.
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        await AyarYazAsync(host, tenant, new TenantSettingsModel
        {
            SmtpHost = "smtp.ornek.com",
            SmtpKullanici = "kullanici1",   // e-posta DEĞİL
            SmtpGonderenAdres = null,
        });

        using var scope = host.ScopeFor(tenant, role: UserRole.Operator);
        Assert.Null(await scope.ServiceProvider.GetRequiredService<BildirimKanaliService>().SmtpAyarAsync());
    }

    [Fact]
    public async Task Sms_basligi_tenant_ayarindan_okunur_ve_bos_ise_null()
    {
        using var host = new TestHost(fx.AppConnectionString);

        var basliksiz = Guid.NewGuid();
        await AyarYazAsync(host, basliksiz, new TenantSettingsModel { FirmaUnvan = "Başlıksız" });
        using (var scope = host.ScopeFor(basliksiz, role: UserRole.Operator))
            Assert.Null(await scope.ServiceProvider.GetRequiredService<BildirimKanaliService>().SmsBaslikAsync());

        var baslikli = Guid.NewGuid();
        await AyarYazAsync(host, baslikli, new TenantSettingsModel { SmsBaslik = "YUCERENT" });
        using (var scope = host.ScopeFor(baslikli, role: UserRole.Operator))
            Assert.Equal("YUCERENT",
                await scope.ServiceProvider.GetRequiredService<BildirimKanaliService>().SmsBaslikAsync());
    }

    [Fact]
    public async Task Tenant_izolasyonu_baska_tenantin_smtp_ayarini_gormez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await AyarYazAsync(host, a, new TenantSettingsModel
        {
            SmtpHost = "mail.a-firma.com", SmtpGonderenAdres = "a@a-firma.com",
        });

        using var scope = host.ScopeFor(b, role: UserRole.Operator);
        Assert.Null(await scope.ServiceProvider.GetRequiredService<BildirimKanaliService>().SmtpAyarAsync());
    }
}
