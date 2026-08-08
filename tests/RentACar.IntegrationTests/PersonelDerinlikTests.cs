using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Personnel;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-40 Bölüm B — personel alan derinliği (23 additive alan) + liste araması.
///
/// <para><b>GorevTanimi SERBEST METİNDİR ve YETKİ VERMEZ</b> — sistem rolü kullanıcı hesabından
/// gelir. Test bunu açıkça kilitliyor: "Yönetici" görev tanımlı personel yaratmak hiçbir yetki
/// değiştirmez.</para>
///
/// <para><b>PII korunuyor:</b> yeni alanlar şifrelenmez ama mevcut TcKimlik/Maaş şifreli akışı
/// bozulmamalı — boş bırakılan PII güncellemede korunur (regresyon).</para>
/// </summary>
[Collection("postgres")]
public sealed class PersonelDerinlikTests(PostgresFixture fx)
{
    private static PersonelInput Dolu(string kod) => new()
    {
        Kod = kod, Ad = "Ali", Soyad = "Yılmaz", TcKimlik = "11111111110", Maas = 50000m,
        Sube = "Merkez", SurucuBelgeNo = "SB-1",
        GorevTanimi = " Operasyon ", Adres = " Bağdat Cad. 1 ", EvTelefonu = " 02161112233 ",
        IsTelefonu = " 02161112244 ", CepTel = " 05551112233 ", MailAdresi = " ali@ornek.com ",
        Referans = " Mehmet Bey ", Aciklama = " Vardiya sorumlusu ",
        SSinifi = " B ", SVerilisTarihi = new DateTimeOffset(2015, 5, 1, 0, 0, 0, TimeSpan.Zero),
        SVerilisYeri = " İstanbul ", DogumTarihi = new DateTimeOffset(1990, 3, 20, 0, 0, 0, TimeSpan.Zero),
        DogumYeri = " Ankara ", BabaAdi = " Hasan ", AnaAdi = " Ayşe ",
        Il = " İstanbul ", Ilce = " Kadıköy ", Mahalle = " Caferağa ",
        CiltNo = " 12 ", AileSiraNo = " 34 ", SiraNo = " 56 ",
        KanGrubu = " 0 Rh+ ", RacTabletNo = " TB-007 "
    };

    [Fact]
    public async Task Yeni_23_alan_round_trip_ve_TRIM()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<PersonelService>();

        var id = await svc.CreateAsync(Dolu("p1"));
        var d = await svc.GetDetailAsync(id);
        var h = d!.Ham!;

        Assert.Equal("P1", d.Kod);                    // kod büyük harfe (mevcut davranış)
        Assert.Equal("Operasyon", h.GorevTanimi);     // trim
        Assert.Equal("Bağdat Cad. 1", h.Adres);
        Assert.Equal("02161112233", h.EvTelefonu);
        Assert.Equal("02161112244", h.IsTelefonu);
        Assert.Equal("05551112233", h.CepTel);
        Assert.Equal("ali@ornek.com", h.MailAdresi);
        Assert.Equal("Mehmet Bey", h.Referans);
        Assert.Equal("Vardiya sorumlusu", h.Aciklama);
        Assert.Equal("B", h.SSinifi);
        Assert.Equal(new DateTimeOffset(2015, 5, 1, 0, 0, 0, TimeSpan.Zero), h.SVerilisTarihi);
        Assert.Equal("İstanbul", h.SVerilisYeri);
        Assert.Equal(new DateTimeOffset(1990, 3, 20, 0, 0, 0, TimeSpan.Zero), h.DogumTarihi);
        Assert.Equal("Ankara", h.DogumYeri);
        Assert.Equal("Hasan", h.BabaAdi);
        Assert.Equal("Ayşe", h.AnaAdi);
        Assert.Equal("İstanbul", h.Il);
        Assert.Equal("Kadıköy", h.Ilce);
        Assert.Equal("Caferağa", h.Mahalle);
        Assert.Equal("12", h.CiltNo);
        Assert.Equal("34", h.AileSiraNo);
        Assert.Equal("56", h.SiraNo);
        Assert.Equal("0 Rh+", h.KanGrubu);
        Assert.Equal("TB-007", h.RacTabletNo);

        // Mevcut PII akışı bozulmadı.
        Assert.Equal("11111111110", d.TcKimlik);
        Assert.Equal(50000m, d.Maas);
    }

    [Fact]
    public async Task Guncellemede_yeni_alanlar_DUSMUYOR_ve_bos_PII_KORUNUYOR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<PersonelService>();
        var id = await svc.CreateAsync(Dolu("P2"));

        // PII BOŞ bırakılıyor (mevcut korunmalı), derinlik alanları değişiyor.
        var g = Dolu("P2");
        g.TcKimlik = null; g.Maas = null;
        g.GorevTanimi = "Yönetici"; g.RacTabletNo = "TB-999"; g.KanGrubu = "A Rh-";
        Assert.True(await svc.UpdateAsync(id, g));

        var d = await svc.GetDetailAsync(id);
        Assert.Equal("Yönetici", d!.Ham!.GorevTanimi);
        Assert.Equal("TB-999", d.Ham.RacTabletNo);
        Assert.Equal("A Rh-", d.Ham.KanGrubu);
        Assert.Equal("Kadıköy", d.Ham.Ilce);      // dokunulmayan alan da korundu
        Assert.Equal("11111111110", d.TcKimlik);  // PII KORUNDU (regresyon)
        Assert.Equal(50000m, d.Maas);
    }

    [Fact]
    public async Task GOREV_TANIMI_YETKI_VERMEZ()
    {
        // "Yönetici" görev tanımı yalnız bir etikettir; yetki UserRole'den gelir.
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        using (var admin = host.ScopeFor(t1))
            await admin.ServiceProvider.GetRequiredService<PersonelService>()
                .CreateAsync(new PersonelInput { Kod = "YON", Ad = "Veli", Soyad = "Yön", GorevTanimi = "Yönetici" });

        // Operatör rolü personel listesini HÂLÂ göremez (ManageUsers gerekiyor) — kayıttaki
        // "Yönetici" etiketi hiçbir kapı açmaz.
        using var op = host.ScopeFor(t1, Guid.NewGuid(), "op", UserRole.Operator);
        await Assert.ThrowsAsync<ValidationException>(() =>
            op.ServiceProvider.GetRequiredService<PersonelService>().ListAsync());
        await Assert.ThrowsAsync<ValidationException>(() =>
            op.ServiceProvider.GetRequiredService<PersonelService>().SearchAsync());
    }

    [Fact]
    public async Task Gelecek_dogum_tarihi_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<PersonelService>();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new PersonelInput
        { Kod = "GT", Ad = "A", Soyad = "B", DogumTarihi = DateTimeOffset.UtcNow.AddYears(1) }));
        Assert.Contains("Doğum tarihi gelecekte olamaz", ex.Message);
        Assert.Empty(await svc.ListAsync());
    }

    [Fact]
    public async Task Filtreler_ELLE_beklenen_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<PersonelService>();

        // ELLE: 3 personel.
        await svc.CreateAsync(new PersonelInput
        { Kod = "F1", Ad = "Ali", Soyad = "Yılmaz", Sube = "Merkez", GorevTanimi = "Operasyon",
          CepTel = "05551112233", MailAdresi = "ali@ornek.com" });
        await svc.CreateAsync(new PersonelInput
        { Kod = "F2", Ad = "Ayşe", Soyad = "Kaya", Sube = "Şube 2", GorevTanimi = "Yönetici",
          CepTel = "05559998877" });
        await svc.CreateAsync(new PersonelInput
        { Kod = "F3", Ad = "Mehmet", Soyad = "Demir", Sube = "Merkez", GorevTanimi = "Operasyon", Aktif = false });

        Assert.Equal(3, (await svc.SearchAsync()).Count);
        Assert.Equal(2, (await svc.SearchAsync(new PersonelFilter { Sube = "merkez" })).Count);       // harf duyarsız
        Assert.Equal(2, (await svc.SearchAsync(new PersonelFilter { GorevTanimi = "OPERASYON" })).Count);
        Assert.Equal(2, (await svc.SearchAsync(new PersonelFilter { Aktif = true })).Count);
        Assert.Single(await svc.SearchAsync(new PersonelFilter { Aktif = false }));

        // Metin araması: sicil / ad / soyad / tam ad / cep / mail
        Assert.Single(await svc.SearchAsync(new PersonelFilter { Ara = "F2" }));
        Assert.Single(await svc.SearchAsync(new PersonelFilter { Ara = "Demir" }));
        Assert.Single(await svc.SearchAsync(new PersonelFilter { Ara = "Ayşe Kaya" }));
        Assert.Single(await svc.SearchAsync(new PersonelFilter { Ara = "0555999" }));
        Assert.Single(await svc.SearchAsync(new PersonelFilter { Ara = "ali@ornek" }));
        Assert.Empty(await svc.SearchAsync(new PersonelFilter { Ara = "yok-boyle" }));

        // Birleşik: Merkez + aktif → 1
        Assert.Single(await svc.SearchAsync(new PersonelFilter { Sube = "Merkez", Aktif = true }));

        // Sıralama ada göre.
        Assert.Equal(["Ali", "Ayşe", "Mehmet"], (await svc.SearchAsync()).Select(p => p.Ad).ToArray());
    }

    [Fact]
    public async Task Derinlik_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
            await s1.ServiceProvider.GetRequiredService<PersonelService>().CreateAsync(Dolu("GIZLI"));

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var svc = s2.ServiceProvider.GetRequiredService<PersonelService>();
        Assert.Empty(await svc.SearchAsync());
        Assert.Empty(await svc.SearchAsync(new PersonelFilter { Ara = "Ali" }));
        // Aynı sicil başka tenant'ta serbest.
        Assert.NotEqual(Guid.Empty, await svc.CreateAsync(Dolu("GIZLI")));
    }
}
