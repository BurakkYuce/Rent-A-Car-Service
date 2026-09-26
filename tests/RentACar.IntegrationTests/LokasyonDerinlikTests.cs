using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Locations;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-22 Bölüm 1 — lokasyon (ofis) derinliği: 16 additive alan + JSONB haftalık çalışma saati.
///
/// <para><b>JSONB tuzağı:</b> koleksiyon kolonunda <c>ValueComparer</c> yoksa EF içeriği
/// "değişmedi" sayar ve düzenleme SESSİZCE kaydedilmez. Aşağıdaki güncelleme testi bunu
/// kalıcı olarak kilitliyor.</para>
///
/// <para>Bağımsız oracle: beklenen değerler elle kurulan girdiden yazılır.</para>
/// </summary>
[Collection("postgres")]
public sealed class LokasyonDerinlikTests(PostgresFixture fx)
{
    private static LocationInput Dolu(string kod, string ad) => new()
    {
        Kod = kod, Ad = ad,
        Adres = "Yeşilköy Mah.", Telefon = "02121112233", Eposta = "ist@ornek.com",
        CalismaSaatleri = "07:00-23:00", TeslimUcreti = 250m, Sube = null,
        IngilizceAd = " Istanbul Airport ", BulusmaNoktasi = " Havalimanı Karşılama ",
        Iata = " ist ", WebdeGizle = true, LokasyonTuru = " Havalimanı ", BinaNo = " B2 ",
        Tarif = " Dış hatlar geliş kapısı 14 ", Ulke = " Türkiye ", PostaKodu = " 34283 ",
        MapsKonumu = " 41.2753,28.7519 ", EkAciklama = " 7/24 vale mevcut ", WebSira = 3,
        DropKarsilamaTuru = " Vale ", DropCalismaSekli = " 7/24 ",
        OzelMail = " drop@ornek.com ", OzelTelefon = " 05551112233 ",
        HaftalikCalismaSaatleri =
        [
            new GunSaat { Gun = 1, Acilis = "08:00", Kapanis = "20:00" },
            new GunSaat { Gun = 6, Acilis = "09:00", Kapanis = "14:00" },
            new GunSaat { Gun = 7, Kapali = true }
        ]
    };

    [Fact]
    public async Task Tum_derinlik_alanlari_round_trip_ve_TRIM()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<LocationService>();

        var id = await svc.CreateAsync(Dolu("ist-hvl", "İstanbul Havalimanı"));
        var l = await svc.GetAsync(id);

        Assert.NotNull(l);
        Assert.Equal("IST-HVL", l!.Kod);                       // kod büyük harfe
        Assert.Equal("Istanbul Airport", l.IngilizceAd);       // trim
        Assert.Equal("Havalimanı Karşılama", l.BulusmaNoktasi);
        Assert.Equal("IST", l.Iata);                           // IATA büyük harfe
        Assert.True(l.WebdeGizle);
        Assert.Equal("Havalimanı", l.LokasyonTuru);
        Assert.Equal("B2", l.BinaNo);
        Assert.Equal("Dış hatlar geliş kapısı 14", l.Tarif);
        Assert.Equal("Türkiye", l.Ulke);
        Assert.Equal("34283", l.PostaKodu);
        Assert.Equal("41.2753,28.7519", l.MapsKonumu);
        Assert.Equal("7/24 vale mevcut", l.EkAciklama);
        Assert.Equal(3, l.WebSira);
        Assert.Equal("Vale", l.DropKarsilamaTuru);
        Assert.Equal("7/24", l.DropCalismaSekli);
        Assert.Equal("drop@ornek.com", l.OzelMail);
        Assert.Equal("05551112233", l.OzelTelefon);

        // Eski alanlar KORUNDU (geriye uyum).
        Assert.Equal("07:00-23:00", l.CalismaSaatleri);
        Assert.Equal(250m, l.TeslimUcreti);
    }

    [Fact]
    public async Task Haftalik_saatler_DAIMA_7_satira_tamamlanir_ve_eksik_gun_KAPALI_olur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<LocationService>();

        // ELLE: girdide yalnız 3 gün var (1, 6, 7).
        var id = await svc.CreateAsync(Dolu("IST-2", "Ofis 2"));
        var l = await svc.GetAsync(id);

        Assert.Equal(7, l!.HaftalikCalismaSaatleri.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], l.HaftalikCalismaSaatleri.Select(x => x.Gun).ToArray());

        var pzt = l.HaftalikCalismaSaatleri.Single(x => x.Gun == 1);
        Assert.Equal("08:00", pzt.Acilis);
        Assert.Equal("20:00", pzt.Kapanis);
        Assert.False(pzt.Kapali);

        // ELLE: gönderilmeyen Salı → kapalı.
        Assert.True(l.HaftalikCalismaSaatleri.Single(x => x.Gun == 2).Kapali);
        // ELLE: Pazar açıkça kapalı işaretlendi.
        Assert.True(l.HaftalikCalismaSaatleri.Single(x => x.Gun == 7).Kapali);
        // ELLE: Cumartesi 09:00-14:00 açık.
        Assert.False(l.HaftalikCalismaSaatleri.Single(x => x.Gun == 6).Kapali);
    }

    [Fact]
    public async Task Haftalik_saat_GUNCELLEMESI_gercekten_kaydedilir_ValueComparer_kilidi()
    {
        // JSONB koleksiyonunda ValueComparer olmasaydı EF "değişmedi" deyip UPDATE üretmez,
        // kullanıcı kaydet der ve HİÇBİR ŞEY olmazdı. Bu test o sessiz kaybı yakalar.
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<LocationService>();
        var id = await svc.CreateAsync(Dolu("IST-3", "Ofis 3"));

        var girdi = Dolu("IST-3", "Ofis 3");
        girdi.HaftalikCalismaSaatleri =
        [
            new GunSaat { Gun = 1, Acilis = "10:00", Kapanis = "18:00" },
            new GunSaat { Gun = 2, Acilis = "10:00", Kapanis = "18:00" }
        ];
        Assert.True(await svc.UpdateAsync(id, girdi));

        var l = await svc.GetAsync(id);
        Assert.Equal(7, l!.HaftalikCalismaSaatleri.Count);
        Assert.Equal("10:00", l.HaftalikCalismaSaatleri.Single(x => x.Gun == 1).Acilis);
        Assert.Equal("18:00", l.HaftalikCalismaSaatleri.Single(x => x.Gun == 2).Kapanis);
        // ELLE: eskiden açık olan Cumartesi artık gönderilmedi → kapalı; eski satır BİRİKMEDİ.
        Assert.True(l.HaftalikCalismaSaatleri.Single(x => x.Gun == 6).Kapali);
        Assert.Equal(2, l.HaftalikCalismaSaatleri.Count(x => !x.Kapali));
    }

    [Fact]
    public async Task Saatsiz_gun_KAPALI_sayilir_ve_gecersiz_gun_numarasi_DUSER()
    {
        using var host = new TestHost(fx.AppConnectionString);

        // Saf fonksiyon — DB gerekmez.
        var n = LocationService.NormalizeWeek(
        [
            new GunSaat { Gun = 3, Acilis = "  ", Kapanis = null },   // saat yok → kapalı
            new GunSaat { Gun = 0, Acilis = "08:00" },                // geçersiz gün → düşer
            new GunSaat { Gun = 9, Acilis = "08:00" },                // geçersiz gün → düşer
            new GunSaat { Gun = 4, Acilis = "08:00", Kapanis = "17:00" }
        ]);

        Assert.Equal(7, n.Count);
        Assert.True(n.Single(x => x.Gun == 3).Kapali);
        Assert.False(n.Single(x => x.Gun == 4).Kapali);
        Assert.Equal(1, n.Count(x => !x.Kapali));
        Assert.DoesNotContain(n, x => x.Gun is < 1 or > 7);
        Assert.NotNull(host);
    }

    [Fact]
    public async Task Bos_haftalik_girdi_de_7_KAPALI_satir_uretir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<LocationService>();

        var id = await svc.CreateAsync(new LocationInput { Kod = "BOS", Ad = "Boş Ofis" });
        var l = await svc.GetAsync(id);
        Assert.Equal(7, l!.HaftalikCalismaSaatleri.Count);
        Assert.All(l.HaftalikCalismaSaatleri, x => Assert.True(x.Kapali));
        Assert.False(l.WebdeGizle);          // varsayılan: sitede GÖRÜNÜR
        Assert.Null(l.Iata);
    }

    [Fact]
    public async Task Derinlik_alanlari_yetki_ve_tenant_kapili()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        Guid id;
        using (var s1 = host.ScopeFor(t1))
            id = await s1.ServiceProvider.GetRequiredService<LocationService>()
                .CreateAsync(Dolu("GIZLI", "Gizli Ofis"));

        using (var s2 = host.ScopeFor(Guid.NewGuid()))
        {
            var svc2 = s2.ServiceProvider.GetRequiredService<LocationService>();
            Assert.Empty(await svc2.ListAsync());
            Assert.Null(await svc2.GetAsync(id));
            // Aynı kod başka tenant'ta serbest.
            Assert.NotEqual(Guid.Empty, await svc2.CreateAsync(Dolu("GIZLI", "Başka")));
        }

        using var muh = host.ScopeFor(t1, Guid.NewGuid(), "muh", UserRole.Muhasebe);
        await Assert.ThrowsAsync<NoPermissionException>(() =>
            muh.ServiceProvider.GetRequiredService<LocationService>().CreateAsync(Dolu("YENI", "Yetkisiz")));
    }
}
