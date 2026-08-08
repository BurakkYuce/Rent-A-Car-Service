using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Customers;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-40 Bölüm A — cari alan derinliği (39 additive alan) + liste/filtre yüzeyleme.
///
/// <para><b>PORTAL ŞİFRESİ DÜZ SAKLANMAZ.</b> Test bunu DB'ye DOĞRUDAN bakarak kanıtlıyor —
/// servis dönüşüne değil, kolonun kendisine. Boş şifreyle güncelleme mevcut özeti KORUMALI;
/// aksi hâlde her düzenleme portal erişimini sessizce siler (TarifeGrubu dersi).</para>
///
/// <para><b>KVKK anonimleştirme bayrakları</b> kaydı SİLMEZ — alan bazında maskeleme TALEBİNİ
/// işaretler. Bu fazda yalnız bayrak taşınıyor; maskeleme davranışı ayrı bir iş.</para>
/// </summary>
[Collection("postgres")]
public sealed class CariDerinlikTests(PostgresFixture fx)
{
    private const string Sifre = "portalSifre1";

    private static CustomerInput Dolu(string unvan) => new()
    {
        Tip = CariType.Kurumsal, Unvan = unvan, VergiNo = "1234567890",
        Adres = " Bağdat Cad. 1 ", Il = "İstanbul", Ilce = "Kadıköy",
        TcDogrulama = true, Ulke = " Türkiye ", Tel2 = " 02161112233 ", OzelKod = " VIP ",
        EntegrasyonKodu = " ENT-1 ", Aciklama = " Kurumsal müşteri ", FaturaAdresFarkli = true,
        RiskIzin = " Orta ", BayiKomisyon = 0.15m, FaturaTekSatir = true, DogumGunuTakip = true,
        AnonimAd = true, AnonimTc = true, AnonimTelefon = true, AnonimMail = true,
        AnonimAdres = true, AnonimBelge = true,
        DogumYeri = " Ankara ", PasaportTarihi = new DateTimeOffset(2020, 1, 5, 0, 0, 0, TimeSpan.Zero),
        PasaportYeri = " İstanbul ", KurumsalNo = " K-99 ", Sifre = Sifre,
        UyariSerbest = " Ödeme gecikmesi var ", WebIndirim = 0.05m,
        KaraZamani = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
        IslemSubeId = Guid.NewGuid(), BakiyeGor = true, TevkifatKodu = " 601 ",
        AracVerilmez = true, YasEhliyetSerbest = true, FaturaKiralayanIsim = " ABC Ltd ",
        MerkezKurumsal = true, Broker = true, FindexZorunlu = true,
        IsAdresi = " Levent Plaza ", IsTelefonu = " 02123334455 ", FirmaId = Guid.NewGuid(),
        KayitliIl = " Ankara ", KayitliIlce = " Çankaya ", MahalleKoy = " Kızılay ",
        SeriNo = " A12 ", CiltNo = " 5 ", AileSira = " 7 ", SiraNo = " 9 "
    };

    [Fact]
    public async Task Tum_derinlik_alanlari_round_trip_ve_TRIM()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<CustomerService>();

        var id = await svc.CreateAsync(Dolu("Alfa A.Ş."));
        var c = await svc.GetAsync(id);

        Assert.NotNull(c);
        Assert.True(c!.TcDogrulama);
        Assert.Equal("Türkiye", c.Ulke);            // trim
        Assert.Equal("02161112233", c.Tel2);
        Assert.Equal("VIP", c.OzelKod);
        Assert.Equal("ENT-1", c.EntegrasyonKodu);
        Assert.Equal("Kurumsal müşteri", c.Aciklama);
        Assert.True(c.FaturaAdresFarkli);
        Assert.Equal("Orta", c.RiskIzin);
        Assert.Equal(0.15m, c.BayiKomisyon);
        Assert.True(c.FaturaTekSatir);
        Assert.True(c.DogumGunuTakip);
        Assert.True(c.AnonimAd && c.AnonimTc && c.AnonimTelefon && c.AnonimMail && c.AnonimAdres && c.AnonimBelge);
        Assert.Equal("Ankara", c.DogumYeri);
        Assert.Equal(new DateTimeOffset(2020, 1, 5, 0, 0, 0, TimeSpan.Zero), c.PasaportTarihi);
        Assert.Equal("İstanbul", c.PasaportYeri);
        Assert.Equal("K-99", c.KurumsalNo);
        Assert.Equal("Ödeme gecikmesi var", c.UyariSerbest);
        Assert.Equal(0.05m, c.WebIndirim);
        Assert.Equal(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), c.KaraZamani);
        Assert.NotNull(c.IslemSubeId);
        Assert.True(c.BakiyeGor);
        Assert.Equal("601", c.TevkifatKodu);
        Assert.True(c.AracVerilmez);
        Assert.True(c.YasEhliyetSerbest);
        Assert.Equal("ABC Ltd", c.FaturaKiralayanIsim);
        Assert.True(c.MerkezKurumsal && c.Broker && c.FindexZorunlu);
        Assert.Equal("Levent Plaza", c.IsAdresi);
        Assert.Equal("02123334455", c.IsTelefonu);
        Assert.NotNull(c.FirmaId);
        Assert.Equal("Ankara", c.KayitliIl);
        Assert.Equal("Çankaya", c.KayitliIlce);
        Assert.Equal("Kızılay", c.MahalleKoy);
        Assert.Equal("A12", c.SeriNo);
        Assert.Equal("5", c.CiltNo);
        Assert.Equal("7", c.AileSira);
        Assert.Equal("9", c.SiraNo);

        // FAZ-40: Adres entity'de vardı ama forma bağlı değildi — artık yazılabiliyor.
        Assert.Equal("Bağdat Cad. 1", c.Adres);
    }

    [Fact]
    public async Task Portal_sifresi_DUZ_SAKLANMAZ_ve_bos_birakilinca_KORUNUR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<CustomerService>();
        var id = await svc.CreateAsync(Dolu("Beta A.Ş."));

        // DB'ye DOĞRUDAN bak: ham şifre hiçbir kolonda geçmemeli.
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.Customers.AsNoTracking().SingleAsync(x => x.Id == id);
            Assert.False(string.IsNullOrWhiteSpace(row.SifreHash));
            Assert.DoesNotContain(Sifre, row.SifreHash!, StringComparison.Ordinal);
            Assert.NotEqual(Sifre, row.SifreHash);
        }

        var ilk = (await svc.GetAsync(id))!.SifreHash;

        // Şifre BOŞ bırakılarak güncelleme → mevcut özet KORUNMALI.
        var g = Dolu("Beta A.Ş. (yeni ad)");
        g.Sifre = null;
        await svc.UpdateAsync(id, g);
        var sonra = await svc.GetAsync(id);
        Assert.Equal("Beta A.Ş. (yeni ad)", sonra!.Unvan);
        Assert.Equal(ilk, sonra.SifreHash);          // portal erişimi kaybolmadı

        // Yeni şifre verilince özet DEĞİŞMELİ.
        var g2 = Dolu("Beta A.Ş.");
        g2.Sifre = "baskaSifre2";
        await svc.UpdateAsync(id, g2);
        Assert.NotEqual(ilk, (await svc.GetAsync(id))!.SifreHash);
    }

    [Fact]
    public async Task Guncellemede_derinlik_alanlari_DUSMUYOR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<CustomerService>();
        var id = await svc.CreateAsync(Dolu("Gama A.Ş."));

        var g = Dolu("Gama A.Ş.");
        g.OzelKod = "PLATIN";
        g.AracVerilmez = false;
        await svc.UpdateAsync(id, g);

        var c = await svc.GetAsync(id);
        Assert.Equal("PLATIN", c!.OzelKod);
        Assert.False(c.AracVerilmez);
        Assert.Equal("ENT-1", c.EntegrasyonKodu);   // dokunulmayan alan korundu
        Assert.True(c.Broker);
    }

    [Fact]
    public async Task Liste_yeni_kolonlari_tasiyor_ve_PASIF_filtresi_calisiyor()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<CustomerService>();

        // ELLE: 3 cari — 1 aktif+araç verilmez, 1 aktif, 1 pasif.
        await svc.CreateAsync(Dolu("Alfa A.Ş."));
        await svc.CreateAsync(new CustomerInput
        { Tip = CariType.Kurumsal, Unvan = "Beta A.Ş.", MusteriTemsilcisi = "Ali", VadeGun = 30,
          Gsm2 = "05551112233", Ilce = "Beşiktaş", Sinif = "A", UyariNedeni = "Gecikme" });
        await svc.CreateAsync(new CustomerInput { Tip = CariType.Kurumsal, Unvan = "Gama A.Ş.", Pasif = true });

        var hepsi = await svc.SearchRowsAsync(new CustomerFilter { PageSize = 50 });
        Assert.Equal(3, hepsi.Items.Count);

        // D3: entity'de olup projeksiyona girmeyen alanlar artık satırda.
        var beta = hepsi.Items.Single(x => x.DisplayName == "Beta A.Ş.");
        Assert.Equal("Ali", beta.MusteriTemsilcisi);
        Assert.Equal(30, beta.VadeGun);
        Assert.Equal("05551112233", beta.Gsm2);
        Assert.Equal("Beşiktaş", beta.Ilce);
        Assert.Equal("A", beta.Sinif);
        Assert.Equal("Gecikme", beta.UyariNedeni);

        var alfa = hepsi.Items.Single(x => x.DisplayName == "Alfa A.Ş.");
        Assert.Equal("ENT-1", alfa.EntegrasyonKodu);
        Assert.Equal("VIP", alfa.OzelKod);
        Assert.Equal("Türkiye", alfa.Ulke);
        Assert.True(alfa.AracVerilmez);
        Assert.Equal("Bağdat Cad. 1", alfa.Adres);

        // FAZ-40: Pasif filtresi YOKTU.
        Assert.Equal(2, (await svc.SearchRowsAsync(new CustomerFilter { Pasif = false, PageSize = 50 })).Items.Count);
        Assert.Single((await svc.SearchRowsAsync(new CustomerFilter { Pasif = true, PageSize = 50 })).Items);
        Assert.Single((await svc.SearchRowsAsync(new CustomerFilter { AracVerilmez = true, PageSize = 50 })).Items);
    }

    [Fact]
    public async Task Derinlik_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        Guid id;
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
            id = await s1.ServiceProvider.GetRequiredService<CustomerService>().CreateAsync(Dolu("Gizli A.Ş."));

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var svc = s2.ServiceProvider.GetRequiredService<CustomerService>();
        Assert.Null(await svc.GetAsync(id));
        Assert.Empty((await svc.SearchRowsAsync(new CustomerFilter { PageSize = 50 })).Items);
    }
}
