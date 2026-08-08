using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Customers;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Cari CRM/kimlik zenginleştirme — bağımsız oracle. Yeni additive alanların roundtrip'i
/// (Gsm2/Kaynak/temsilci/İYS/uyarı/ehliyet/risk mesajı/HGS yansıtma türü).
/// </summary>
[Collection("postgres")]
public sealed class CustomerEnrichmentTests(PostgresFixture fx)
{
    [Fact]
    public async Task Parite_crm_fields_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CustomerService>();

        // Canlı musteri_kayit/musteri_crm parite alanları (docs/parite/03). Beklenenler senaryodan.
        var dogum = new DateTimeOffset(1985, 7, 20, 0, 0, 0, TimeSpan.Zero);
        var id = await svc.CreateAsync(new CustomerInput
        {
            Tip = CariType.Kurumsal, Unvan = "Yüce Turizm A.Ş.", VergiNo = "1234567890",
            Sinif = "VIP", MailIzin = true, SmsIzin = false, TelefonIzin = true,
            DogumTarihi = dogum, BabaAdi = "Ahmet", AnaAdi = "Fatma", PasaportNo = "U1234567",
            FaturaDonemi = "Aylık", TevkifatOrani = 20.00m,
            Kisiler =
            [
                new CustomerContactInput { AdSoyad = "Ali Veli", Telefon = "5551112233", Mail = "ali@yuce.com" },
                new CustomerContactInput { AdSoyad = "Veli Ali", Telefon = "5324445566", Mail = "veli@yuce.com" },
                new CustomerContactInput { AdSoyad = "Can Can", Telefon = "5061112233", Mail = "can@yuce.com" }
            ]
        });

        var c = await svc.GetAsync(id);
        Assert.NotNull(c);
        Assert.Equal("VIP", c!.Sinif);
        Assert.True(c.MailIzin);
        Assert.False(c.SmsIzin);
        Assert.True(c.TelefonIzin);
        Assert.Equal(dogum, c.DogumTarihi);
        Assert.Equal("Ahmet", c.BabaAdi);
        Assert.Equal("Fatma", c.AnaAdi);
        Assert.Equal("U1234567", c.PasaportNo);
        Assert.Equal("Aylık", c.FaturaDonemi);
        Assert.Equal(20.00m, c.TevkifatOrani);
        Assert.Equal(3, c.Kisiler.Count);
        Assert.Equal("Ali Veli", c.Kisiler[0].AdSoyad);
        Assert.Equal("veli@yuce.com", c.Kisiler[1].Mail);
        Assert.Equal("5061112233", c.Kisiler[2].Telefon);
    }

    [Fact]
    public async Task Kisiler_bos_satir_atlanir_ve_update_temizleyip_yeniden_yazar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CustomerService>();

        var id = await svc.CreateAsync(new CustomerInput
        {
            Tip = CariType.Kurumsal, Unvan = "Kişi A.Ş.",
            Kisiler =
            [
                new CustomerContactInput { AdSoyad = "Boş Satır Test", Gorev = "Müdür" },
                new CustomerContactInput { AdSoyad = "   " } // boşluk-only AdSoyad — atlanmalı
            ]
        });

        var c1 = await svc.GetAsync(id);
        Assert.Single(c1!.Kisiler);
        Assert.Equal("Boş Satır Test", c1.Kisiler[0].AdSoyad);
        Assert.Equal("Müdür", c1.Kisiler[0].Gorev);

        await svc.UpdateAsync(id, new CustomerInput
        {
            Tip = CariType.Kurumsal, Unvan = "Kişi A.Ş.",
            Kisiler = [new CustomerContactInput { AdSoyad = "Yeni Kişi", Telefon = "5001112233" }]
        });

        var c2 = await svc.GetAsync(id);
        Assert.Single(c2!.Kisiler);
        Assert.Equal("Yeni Kişi", c2.Kisiler[0].AdSoyad);
        Assert.Equal("5001112233", c2.Kisiler[0].Telefon);
    }

    [Fact]
    public async Task Tevkifat_orani_out_of_range_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CustomerService>();

        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => svc.CreateAsync(new CustomerInput
            { Tip = CariType.Kurumsal, Unvan = "X A.Ş.", TevkifatOrani = 120m }));
    }

    [Fact]
    public async Task New_crm_fields_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CustomerService>();

        var ehliyetTar = new DateTimeOffset(2015, 3, 10, 0, 0, 0, TimeSpan.Zero);
        var riskTar = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);

        var id = await svc.CreateAsync(new CustomerInput
        {
            Tip = CariType.Bireysel, Ad = "Ayşe", Soyad = "Yıldız",
            CepTel = "5551112233", Gsm2 = "5324445566", Kaynak = "Web",
            MusteriTemsilcisi = "Mehmet", IysIzinli = true, Uyari = true, UyariNedeni = "Geç ödeme",
            EhliyetNo = "ABC123", EhliyetSinifi = "B", EhliyetTarihi = ehliyetTar, EhliyetYeri = "İstanbul",
            RiskMesaji = "Dikkat", RiskTarihi = riskTar, HgsYansitmaTuru = "Faturalı"
        });

        var c = await svc.GetAsync(id);
        Assert.NotNull(c);
        Assert.Equal("5324445566", c!.Gsm2);
        Assert.Equal("Web", c.Kaynak);
        Assert.Equal("Mehmet", c.MusteriTemsilcisi);
        Assert.True(c.IysIzinli);
        Assert.True(c.Uyari);
        Assert.Equal("Geç ödeme", c.UyariNedeni);
        Assert.Equal("ABC123", c.EhliyetNo);
        Assert.Equal("B", c.EhliyetSinifi);
        Assert.Equal(ehliyetTar, c.EhliyetTarihi);
        Assert.Equal("İstanbul", c.EhliyetYeri);
        Assert.Equal("Dikkat", c.RiskMesaji);
        Assert.Equal(riskTar, c.RiskTarihi);
        Assert.Equal("Faturalı", c.HgsYansitmaTuru);
    }

    [Fact]
    public async Task New_fields_default_null_or_false()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CustomerService>();

        var id = await svc.CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Boş" });
        var c = await svc.GetAsync(id);
        Assert.Null(c!.Gsm2);
        Assert.Null(c.Kaynak);
        Assert.Null(c.EhliyetNo);
        Assert.Null(c.EhliyetTarihi);
        Assert.Null(c.RiskTarihi);
        Assert.False(c.IysIzinli);
        Assert.False(c.Uyari);
    }

    [Fact]
    public async Task Update_clears_and_sets_crm_fields()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CustomerService>();

        var id = await svc.CreateAsync(new CustomerInput
        { Tip = CariType.Bireysel, Ad = "Can", Uyari = true, UyariNedeni = "x", Kaynak = "Telefon" });

        await svc.UpdateAsync(id, new CustomerInput
        { Tip = CariType.Bireysel, Ad = "Can", Uyari = false, UyariNedeni = null, Kaynak = "Bayi" });

        var c = await svc.GetAsync(id);
        Assert.False(c!.Uyari);
        Assert.Null(c.UyariNedeni);
        Assert.Equal("Bayi", c.Kaynak);
    }

    [Fact]
    public async Task Turev_parite_fields_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CustomerService>();

        var id = await svc.CreateAsync(new CustomerInput
        {
            Tip = CariType.Bireysel, Ad = "Kaan", Soyad = "Demir",
            OzelCariTip = "Grup İçi", MusteriTipi = "Türk-Yabancı Ehliyetli",
            EhliyetUlke = "ALMANYA", Dil = "EN", Doviz = "EURO", TevkifatDurum = "Sadece Tevkifatlı",
        });

        var c = await svc.GetAsync(id);
        Assert.NotNull(c);
        Assert.Equal("Grup İçi", c!.OzelCariTip);              // Türkçe imlâ korunur
        Assert.Equal("Türk-Yabancı Ehliyetli", c.MusteriTipi);
        Assert.Equal("ALMANYA", c.EhliyetUlke);
        Assert.Equal("EN", c.Dil);
        Assert.Equal("EURO", c.Doviz);
        Assert.Equal("Sadece Tevkifatlı", c.TevkifatDurum);
    }

    /// <summary>
    /// FAZ-13 — dropdown seçim listesi: görünen ad ListAsync ile BİREBİR aynı, ama PII taşımaz.
    /// (ListAsync her carinin TC/ehliyet/pasaport cipher'ını çözer; açılır liste için gereksiz.
    /// Canlı duman testinde bayat anahtar-halkası yüzünden sayfa başına yüzlerce
    /// "Cipher çözülemedi" uyarısı ürettiği ölçüldü.)
    /// </summary>
    [Fact]
    public async Task Secim_listesi_PIIsiz_ve_ad_ListAsync_ile_ayni()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var svc = scope.ServiceProvider.GetRequiredService<CustomerService>();

        await svc.CreateAsync(new CustomerInput
        { Tip = CariType.Bireysel, Ad = "Selim", Soyad = "Kaya", TcKimlik = "10000000146" });
        await svc.CreateAsync(new CustomerInput { Tip = CariType.Kurumsal, Unvan = "Kaya Filo A.Ş." });

        var secim = await svc.ListSecimAsync();
        var tam = await svc.ListAsync();
        Assert.Equal(tam.Select(x => x.DisplayName).OrderBy(x => x),
                     secim.Select(x => x.Ad).OrderBy(x => x));
        Assert.Contains(secim, x => x.Ad == "Selim Kaya");
        Assert.Contains(secim, x => x.Ad == "Kaya Filo A.Ş.");

        // Tenant izolasyonu (racar_app + RLS): başka tenant hiçbir ad görmez.
        using var digeri = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await digeri.ServiceProvider.GetRequiredService<CustomerService>().ListSecimAsync());
    }
}
