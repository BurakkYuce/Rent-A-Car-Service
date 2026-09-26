using Microsoft.Extensions.Logging.Abstractions;
using RentACar.Application.Integrations;
using RentACar.Infrastructure.Integrations;
using RentACar.Web.Integrations;

namespace RentACar.IntegrationTests;

/// <summary>
/// Gerçek göndericilerin AĞA ÇIKMADAN doğrulanabilen davranışları: girdi reddi ve hata sözlüğü.
///
/// <para>Gönderim yolunun kendisi (SMTP el sıkışması, Twilio HTTP çağrısı) burada test EDİLMEZ —
/// dış servise bağımlı olurdu. Onun yerine gönderici, ağa hiç çıkmadan reddetmesi gereken durumlarda
/// reddediyor mu ve reddi operatörün okuyabileceği bir cümleyle mi bildiriyor, o kilitlenir.</para>
/// </summary>
public sealed class GondericiTests
{
    private static MailKitEmailSender Sender() => new(NullLogger<MailKitEmailSender>.Instance);

    private static SmtpAyar ValidSetting(string host = "smtp.ornek.com", int port = 587)
        => new(host, port, true, "kullanici", "sifre", "gonderen@ornek.com", "Gönderen");

    [Theory]
    [InlineData("", 587, "gonderen@ornek.com", "alici@ornek.com")]   // host yok
    [InlineData("smtp.ornek.com", 0, "gonderen@ornek.com", "alici@ornek.com")]     // port geçersiz
    [InlineData("smtp.ornek.com", 70000, "gonderen@ornek.com", "alici@ornek.com")] // port aralık dışı
    [InlineData("smtp.ornek.com", 587, "", "alici@ornek.com")]        // gönderen yok
    [InlineData("smtp.ornek.com", 587, "gonderen@ornek.com", "")]     // alıcı yok
    public async Task Eksik_alanlarda_aga_cikmadan_reddeder(string host, int port, string sender, string recipient)
    {
        var setting = new SmtpAyar(host, port, true, null, null, sender, null);
        var result = await Sender().SendAsync(setting, new EpostaMesaj(recipient, "konu", "<p>gövde</p>"));

        Assert.False(result.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result.Hata));
    }

    [Fact]
    public async Task Bozuk_alici_adresi_istisna_sizdirmadan_hata_dondurur()
    {
        // MimeKit adres ayrıştırma istisnası yukarı SIZMAMALI: gönderimi tetikleyen arka plan işi
        // tek bir bozuk müşteri adresi yüzünden çökerse sıradaki bildirimler de gitmez.
        var result = await Sender().SendAsync(
            ValidSetting(), new EpostaMesaj("bu bir adres değil", "konu", "<p>gövde</p>"));

        Assert.False(result.Ok);
        Assert.Contains("geçersiz", result.Hata, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("21211")]
    [InlineData("21408")]
    [InlineData("30007")]
    [InlineData("20003")]
    public void Sms_hata_kodlari_turkce_aciklamaya_cevrilir(string code)
    {
        var description = TwilioSmsService.ErrorDescription(code);
        Assert.False(string.IsNullOrWhiteSpace(description));
        // Ham kodun kendisi cevap DEĞİLDİR: operatöre ne yapacağını söyleyen bir cümle beklenir.
        Assert.NotEqual(code, description);
    }

    [Fact]
    public void Sms_bilinmeyen_kod_sessiz_kalmaz_null_kod_bos_doner()
    {
        Assert.Contains("99999", TwilioSmsService.ErrorDescription("99999"));
        Assert.Equal(string.Empty, TwilioSmsService.ErrorDescription(null));
    }
}
