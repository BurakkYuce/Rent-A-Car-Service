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
    private static MailKitEmailSender Gonderici() => new(NullLogger<MailKitEmailSender>.Instance);

    private static SmtpAyar GecerliAyar(string host = "smtp.ornek.com", int port = 587)
        => new(host, port, true, "kullanici", "sifre", "gonderen@ornek.com", "Gönderen");

    [Theory]
    [InlineData("", 587, "gonderen@ornek.com", "alici@ornek.com")]   // host yok
    [InlineData("smtp.ornek.com", 0, "gonderen@ornek.com", "alici@ornek.com")]     // port geçersiz
    [InlineData("smtp.ornek.com", 70000, "gonderen@ornek.com", "alici@ornek.com")] // port aralık dışı
    [InlineData("smtp.ornek.com", 587, "", "alici@ornek.com")]        // gönderen yok
    [InlineData("smtp.ornek.com", 587, "gonderen@ornek.com", "")]     // alıcı yok
    public async Task Eksik_alanlarda_aga_cikmadan_reddeder(string host, int port, string gonderen, string alici)
    {
        var ayar = new SmtpAyar(host, port, true, null, null, gonderen, null);
        var sonuc = await Gonderici().SendAsync(ayar, new EpostaMesaj(alici, "konu", "<p>gövde</p>"));

        Assert.False(sonuc.Ok);
        Assert.False(string.IsNullOrWhiteSpace(sonuc.Hata));
    }

    [Fact]
    public async Task Bozuk_alici_adresi_istisna_sizdirmadan_hata_dondurur()
    {
        // MimeKit adres ayrıştırma istisnası yukarı SIZMAMALI: gönderimi tetikleyen arka plan işi
        // tek bir bozuk müşteri adresi yüzünden çökerse sıradaki bildirimler de gitmez.
        var sonuc = await Gonderici().SendAsync(
            GecerliAyar(), new EpostaMesaj("bu bir adres değil", "konu", "<p>gövde</p>"));

        Assert.False(sonuc.Ok);
        Assert.Contains("geçersiz", sonuc.Hata, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("21211")]
    [InlineData("21408")]
    [InlineData("30007")]
    [InlineData("20003")]
    public void Sms_hata_kodlari_turkce_aciklamaya_cevrilir(string kod)
    {
        var aciklama = TwilioSmsService.HataAciklama(kod);
        Assert.False(string.IsNullOrWhiteSpace(aciklama));
        // Ham kodun kendisi cevap DEĞİLDİR: operatöre ne yapacağını söyleyen bir cümle beklenir.
        Assert.NotEqual(kod, aciklama);
    }

    [Fact]
    public void Sms_bilinmeyen_kod_sessiz_kalmaz_null_kod_bos_doner()
    {
        Assert.Contains("99999", TwilioSmsService.HataAciklama("99999"));
        Assert.Equal(string.Empty, TwilioSmsService.HataAciklama(null));
    }
}
