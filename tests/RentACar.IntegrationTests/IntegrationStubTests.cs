using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Integrations;
using RentACar.Infrastructure.Integrations;

namespace RentACar.IntegrationTests;

/// <summary>
/// v1 entegrasyon stub'ları kayıtlı + çağrılabilir (DB gerektirmez).
///
/// <para><b>DÜRÜST STUB KURALI (kalıcı kilit):</b> yapılandırma yokken hiçbir stub "başarılı"
/// dönmez. Sahte başarı, çağıranın kalıcı kayda yanlış yazmasına yol açar — e-Fatura'da sahte
/// ETTN (M2), POS'ta var olmayan işlem referansı, KABİS'te yapılmamış yasal bildirim, SMS'te
/// gitmemiş müşteri mesajı. Bu testler o kuralı her port için ayrı ayrı sabitler; bir stub
/// yeniden "true" dönmeye başlarsa suite kırmızıya döner.</para>
/// </summary>
public sealed class IntegrationStubTests
{
    private static ServiceProvider Build()
        => new ServiceCollection().AddIntegrationStubs().BuildServiceProvider();

    [Fact]
    public void All_ports_resolve()
    {
        using var sp = Build();
        Assert.NotNull(sp.GetService<ISmsService>());
        Assert.NotNull(sp.GetService<IEmailSender>());
        Assert.NotNull(sp.GetService<IWhatsAppService>());
        Assert.NotNull(sp.GetService<IGoogleCalendarService>());
        Assert.NotNull(sp.GetService<IEInvoiceService>());
        Assert.NotNull(sp.GetService<IPosService>());
        Assert.NotNull(sp.GetService<IKabisService>());
        Assert.NotNull(sp.GetService<IHgsService>());
    }

    [Fact]
    public async Task Einvoice_stub_gondermez_ettn_yok()
    {
        // Adversarial M2: stub GERÇEKTEN göndermez → Success=false, ETTN yok (fatura sahte "gönderildi"
        // işaretlenmesin). Gerçek adapter takılınca Success=true döner.
        using var sp = Build();
        var result = await sp.GetRequiredService<IEInvoiceService>()
            .SendAsync(new EInvoiceRequest("1234567890", "ACME", 100m, 20m, "TRY"));
        Assert.False(result.Success);
        Assert.Null(result.Ettn);
    }

    [Fact]
    public async Task Pos_stub_sahte_islem_referansi_uretmez()
    {
        // Yapılandırma yokken hiçbir kart bloke EDİLMEZ ve hiçbir ödeme sayfası açılmaz. Eskiden
        // "STUBTX-…" referansıyla true dönüyordu; o referans provizyon kaydına yazılsaydı sistemde
        // geçerli bir işlem varmış gibi görünürdü.
        using var sp = Build();
        var pos = sp.GetRequiredService<IPosService>();

        var baslat = await pos.BaslatAsync(new PosOdemeIstegi(
            500m, "TRY", "RZ-1", "https://ornek/donus",
            new PosAlici("M1", "Ahmet", "Yılmaz", "a@b.c", "+905000000000", "11111111110",
                "Adres", "İstanbul", "Turkey", "1.2.3.4"),
            "Depozito", Provizyon: true));
        Assert.False(baslat.Ok);
        Assert.Null(baslat.Token);
        Assert.Null(baslat.OdemeSayfasiUrl);
        Assert.False(string.IsNullOrWhiteSpace(baslat.Hata));

        var durum = await pos.SonucAsync("herhangi-token");
        Assert.False(durum.Ok);
        Assert.Null(durum.OdemeId);

        foreach (var sonuc in new[]
        {
            await pos.KapatAsync("1", 10m, "1.2.3.4"),
            await pos.IptalAsync("1", "1.2.3.4"),
            await pos.IadeAsync("1", 10m, "1.2.3.4"),
        })
        {
            Assert.False(sonuc.Success);
            Assert.Null(sonuc.TxRef);
        }
    }

    [Fact]
    public async Task Sms_stub_gondermez()
    {
        // Gitmeyen müşteri mesajı "gitti" sayılmamalı — çağıran bunu bildirim kaydına yazacak.
        using var sp = Build();
        Assert.False(await sp.GetRequiredService<ISmsService>().SendAsync("+905321112233", "deneme"));
    }

    [Fact]
    public async Task Kabis_stub_bildirim_yapmaz()
    {
        // KABİS yasal yükümlülük: bildirilmemiş kiralamayı "bildirildi" göstermek cezayı gizler.
        using var sp = Build();
        var ok = await sp.GetRequiredService<IKabisService>().BildirAsync(
            new KabisBildirim("RZ-000001", "34ABC123", "11111111110",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(3)));
        Assert.False(ok);
    }

    [Fact]
    public async Task Eposta_noop_gonderici_hata_dondurur()
    {
        using var sp = Build();
        var sonuc = await sp.GetRequiredService<IEmailSender>().SendAsync(
            new SmtpAyar("mail.ornek.com", 587, true, null, null, "a@ornek.com", null),
            new EpostaMesaj("b@ornek.com", "konu", "<p>gövde</p>"));
        Assert.False(sonuc.Ok);
        Assert.False(string.IsNullOrWhiteSpace(sonuc.Hata));
    }

    [Fact]
    public async Task Hgs_stub_bos_liste_dondurur()
    {
        // Boş liste DÜRÜSTTÜR: "veri yok" ≠ "başarı". Yansıtma mantığı no-op çalışır, yanlış kayıt yazmaz.
        using var sp = Build();
        var gecisler = await sp.GetRequiredService<IHgsService>().GetCrossingsAsync(
            "34ABC123", DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow);
        Assert.Empty(gecisler);
    }
}
