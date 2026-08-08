using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Crm;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-44 — assistans (yol yardım) talebi dikeyi.
///
/// <para><b>SNAPSHOT sözleşmesi:</b> plaka/ad/telefon sözleşmeden KOPYALANIR (şikayet dikeyinde
/// canlı çözülüyordu — burada tersi bilinçli). Kayıt bir OLAY TUTANAĞIDIR: araç değişse bile
/// "o gece hangi araç arandı" sabit kalmalı. Test bunu kanıtlıyor.</para>
///
/// <para><b>Kullanıcı değeri önceliklidir:</b> elle yazılan telefon sözleşmedekiyle EZİLMEZ —
/// çağrıyı yapan kişi müşteri olmayabilir.</para>
/// </summary>
[Collection("postgres")]
public sealed class AssistansTalepTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset T0 = TestZaman.Simdi().AddDays(-3);

    private static async Task<(Guid kira, Guid arac, Guid cari)> KiraAsync(
        IServiceProvider sp, string plaka, string ad, string? tel)
    {
        var cari = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CariType.Kurumsal, Unvan = ad, CepTel = tel });
        var arac = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        var kira = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = cari, VehicleId = arac, BasTar = T0, BitTar = T0.AddDays(5), GunlukUcret = 1000m });
        return (kira, arac, cari);
    }

    [Fact]
    public async Task Sozlesmeden_SNAPSHOT_dolduruluyor_ve_arac_degisse_de_SABIT_kaliyor()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var (kira, arac, _) = await KiraAsync(sp, "34 AS 01", "Alfa A.Ş.", "05551112233");
        var svc = sp.GetRequiredService<AssistansTalepService>();

        var id = await svc.CreateAsync(new AssistansInput
        {
            RentalId = kira, Mesaj = "  Lastik patladı, yolda kaldık  ",
            Sebep = " Lastik ", YedekLastikMi = true, AracHareketMi = false, Zaman = T0.AddDays(1)
        });

        var t = await svc.GetAsync(id);
        Assert.Equal("34AS01", t!.Plaka);            // sözleşmeden + normalize
        Assert.Equal("Alfa A.Ş.", t.AdSoyad);
        Assert.Equal("05551112233", t.CepTel);
        Assert.Equal("Lastik patladı, yolda kaldık", t.Mesaj);   // trim
        Assert.Equal("Lastik", t.Sebep);
        Assert.True(t.YedekLastikMi);
        Assert.False(t.AracHareketMi);
        Assert.False(t.Kapandi);

        // OLAY TUTANAĞI: aracın plakası sonradan değişse bile kayıt DEĞİŞMEZ (şikayetin tersi).
        Assert.True(await sp.GetRequiredService<VehicleService>()
            .UpdateAsync(arac, new VehicleInput { Plaka = "06 YN 99" }));
        Assert.Equal("34AS01", (await svc.GetAsync(id))!.Plaka);
    }

    [Fact]
    public async Task ELLE_yazilan_deger_sozlesmeden_geleni_EZMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var (kira, _, _) = await KiraAsync(sp, "34 AS 02", "Beta A.Ş.", "05551112233");
        var svc = sp.GetRequiredService<AssistansTalepService>();

        // Çağrıyı ikinci sürücü yapıyor: telefon FARKLI, ad farklı. Otomatik doldurma bunu ezmemeli.
        var id = await svc.CreateAsync(new AssistansInput
        { RentalId = kira, Mesaj = "Akü bitti", AdSoyad = "İkinci Sürücü", CepTel = "05559998877" });

        var t = await svc.GetAsync(id);
        Assert.Equal("İkinci Sürücü", t!.AdSoyad);
        Assert.Equal("05559998877", t.CepTel);
        Assert.Equal("34AS02", t.Plaka);      // yalnız BOŞ alan sözleşmeden dolduruldu
    }

    [Fact]
    public async Task Sozlesmesiz_cagri_kaydedilebilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<AssistansTalepService>();

        var id = await svc.CreateAsync(new AssistansInput
        { Mesaj = "Sözleşmesi bulunamadı, plaka 34 XX 99", Plaka = "34 xx 99", CepTel = "05551110000" });

        var t = await svc.GetAsync(id);
        Assert.Null(t!.RentalId);
        Assert.Equal("34XX99", t.Plaka);      // elle girilen plaka da normalize
        Assert.Equal("05551110000", t.CepTel);
    }

    [Fact]
    public async Task Gecersiz_girdi_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<AssistansTalepService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new AssistansInput { Mesaj = "   " }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new AssistansInput { Mesaj = "x", RentalId = Guid.NewGuid() }));
        // Gelecek tarihli olay tutanağı olmaz.
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new AssistansInput { Mesaj = "x", Zaman = DateTimeOffset.UtcNow.AddYears(5) }));
        Assert.Contains("gelecekte olamaz", ex.Message);

        Assert.Empty(await svc.SearchAsync());
    }

    [Fact]
    public async Task Filtreler_ELLE_beklenen_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<AssistansTalepService>();

        // ELLE: 3 talep.
        await svc.CreateAsync(new AssistansInput
        { Mesaj = "Lastik patladı", Plaka = "34 FL 01", Zaman = T0, YedekLastikMi = true, AracHareketMi = false });
        await svc.CreateAsync(new AssistansInput
        { Mesaj = "Akü bitti", Plaka = "34 FL 02", Zaman = T0.AddDays(1), AracHareketMi = true, CepTel = "05551234567" });
        await svc.CreateAsync(new AssistansInput
        { Mesaj = "Anahtar içeride kaldı", Plaka = "06 XY 03", Zaman = T0.AddDays(2), Kapandi = true, AracHareketMi = true });

        Assert.Equal(3, (await svc.SearchAsync()).Count);

        // Plaka: kullanıcı BOŞLUKLU yazsa da bulunur (DB'de normalize saklanıyor).
        Assert.Equal(2, (await svc.SearchAsync(new AssistansFilter { Plaka = "34 FL" })).Count);
        Assert.Single(await svc.SearchAsync(new AssistansFilter { Plaka = "34fl01" }));
        Assert.Empty(await svc.SearchAsync(new AssistansFilter { Plaka = "99 ZZ" }));

        // Tarih aralığı (kapsayıcı).
        Assert.Equal(2, (await svc.SearchAsync(new AssistansFilter { TarihMin = T0.AddDays(1) })).Count);
        Assert.Equal(2, (await svc.SearchAsync(new AssistansFilter { TarihMax = T0.AddDays(1) })).Count);

        Assert.Equal(2, (await svc.SearchAsync(new AssistansFilter { Kapandi = false })).Count);
        Assert.Single(await svc.SearchAsync(new AssistansFilter { Kapandi = true }));
        Assert.Single(await svc.SearchAsync(new AssistansFilter { YedekLastikMi = true }));
        // "Çekici gerekenler" = hareket EDEMEYENLER.
        Assert.Single(await svc.SearchAsync(new AssistansFilter { HareketEdemiyor = true }));
        Assert.Equal(2, (await svc.SearchAsync(new AssistansFilter { HareketEdemiyor = false })).Count);

        // Metin araması mesaj/telefon içinde.
        Assert.Equal("Akü bitti", Assert.Single(await svc.SearchAsync(new AssistansFilter { Ara = "akü" })).Mesaj);
        Assert.Single(await svc.SearchAsync(new AssistansFilter { Ara = "0555123" }));

        // Liste en yeniden eskiye.
        var hepsi = await svc.SearchAsync();
        Assert.Equal("Anahtar içeride kaldı", hepsi[0].Mesaj);
    }

    [Fact]
    public async Task Guncelleme_ve_silme_calisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<AssistansTalepService>();
        var id = await svc.CreateAsync(new AssistansInput { Mesaj = "Yolda kaldık", Plaka = "34 GU 01" });

        Assert.True(await svc.UpdateAsync(id, new AssistansInput
        { Mesaj = "Yolda kaldık", Plaka = "34 GU 01", Kapandi = true, Cozum = "Çekici gönderildi", AracHareketMi = true }));

        var t = await svc.GetAsync(id);
        Assert.True(t!.Kapandi);
        Assert.Equal("Çekici gönderildi", t.Cozum);
        Assert.True(t.AracHareketMi);

        Assert.True(await svc.DeleteAsync(id));
        Assert.Null(await svc.GetAsync(id));
    }

    [Fact]
    public async Task Assistans_tenant_izolasyonlu_ve_yetki_kapili()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        Guid id;
        using (var s1 = host.ScopeFor(t1))
            id = await s1.ServiceProvider.GetRequiredService<AssistansTalepService>()
                .CreateAsync(new AssistansInput { Mesaj = "Gizli çağrı", Plaka = "34 GZ 01" });

        using (var s2 = host.ScopeFor(Guid.NewGuid()))
        {
            var svc2 = s2.ServiceProvider.GetRequiredService<AssistansTalepService>();
            Assert.Empty(await svc2.SearchAsync());
            Assert.Empty(await svc2.SearchAsync(new AssistansFilter { Plaka = "34GZ01" }));
            Assert.Null(await svc2.GetAsync(id));
            Assert.False(await svc2.DeleteAsync(id));
        }

        // Muhasebe rolünde OperationsWrite yok → yazma kapalı.
        using var muh = host.ScopeFor(t1, Guid.NewGuid(), "muh", UserRole.Muhasebe);
        await Assert.ThrowsAsync<ValidationException>(() =>
            muh.ServiceProvider.GetRequiredService<AssistansTalepService>()
                .CreateAsync(new AssistansInput { Mesaj = "Yetkisiz" }));

        // Kaynak tenant'ta kayıt duruyor.
        using var s1b = host.ScopeFor(t1);
        Assert.NotNull(await s1b.ServiceProvider.GetRequiredService<AssistansTalepService>().GetAsync(id));
    }
}
