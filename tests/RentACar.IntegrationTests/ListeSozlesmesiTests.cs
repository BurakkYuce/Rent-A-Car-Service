using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// F1.3 liste sözleşmesi — saf kısım: <see cref="ListeIstegi"/> normalizasyonu,
/// <see cref="Sayfa{T}"/> hesapları ve <see cref="SortFieldMap{T}"/> beyaz listesi.
/// Beklenen değerler ELLE yazılmış tablolardır (üretim kodundan türetilmez).
/// </summary>
public sealed class ListeSozlesmesiTests
{
    // ---- ListeIstegi normalizasyonu ----

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(200, 200)]
    [InlineData(201, 201)]      // sayfa numarasının üst sınırı yok
    [InlineData(10000, 10000)]
    public void Sayfa_numarasi_birden_kucukse_bire_cekilir(int verilen, int beklenen)
        => Assert.Equal(beklenen, new ListeIstegi(Sayfa: verilen).Sayfa);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(200, 200)]
    [InlineData(201, 200)]
    [InlineData(10000, 200)]
    public void Boyut_bir_ile_iki_yuz_arasina_kirpilir(int verilen, int beklenen)
        => Assert.Equal(beklenen, new ListeIstegi(Boyut: verilen).Boyut);

    [Fact]
    public void Varsayilanlar_sayfa_bir_boyut_elli_siralama_yok()
    {
        var i = new ListeIstegi();
        Assert.Equal(1, i.Sayfa);
        Assert.Equal(50, i.Boyut);
        Assert.Null(i.Sirala);
        Assert.Null(i.ParseSort());
    }

    [Fact]
    public void With_ifadesi_normalizasyonu_atlatamaz()
    {
        var i = new ListeIstegi() with { Sayfa = -3, Boyut = 10000, Sirala = "   " };
        Assert.Equal(1, i.Sayfa);
        Assert.Equal(200, i.Boyut);
        Assert.Null(i.Sirala);
    }

    [Theory]
    [InlineData("plaka", "plaka", false)]
    [InlineData("-plaka", "plaka", true)]
    [InlineData("  -plaka  ", "plaka", true)]
    [InlineData("- plaka", "plaka", true)]
    [InlineData("-", "", true)] // alan adı boş → beyaz liste reddeder
    public void Sirala_alan_ve_yon_olarak_cozulur(string verilen, string alan, bool azalan)
    {
        var coz = new ListeIstegi(Sirala: verilen).ParseSort();
        Assert.NotNull(coz);
        Assert.Equal(alan, coz!.Value.Alan);
        Assert.Equal(azalan, coz.Value.Azalan);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Bos_sirala_null_olur(string? verilen)
    {
        var i = new ListeIstegi(Sirala: verilen);
        Assert.Null(i.Sirala);
        Assert.Null(i.ParseSort());
    }

    [Fact]
    public void Atla_long_hesaplanir_int_tasmaz()
    {
        Assert.Equal(0L, new ListeIstegi(1, 50).Atla);
        Assert.Equal(40L, new ListeIstegi(3, 20).Atla);
        // (2147483647 - 1) × 200 = 429 496 729 200 — int'e sığmaz; elle hesaplandı.
        Assert.Equal(429_496_729_200L, new ListeIstegi(int.MaxValue, 200).Atla);
    }

    // ---- Sayfa<T> ----

    [Theory]
    [InlineData(0, 50, 0)]
    [InlineData(1, 50, 1)]
    [InlineData(50, 50, 1)]
    [InlineData(51, 50, 2)]
    [InlineData(5, 2, 3)]
    [InlineData(int.MaxValue, 200, 10_737_419)] // 200 × 10 737 418 = 2 147 483 600, kalan 47 → +1
    public void ToplamSayfa_yukari_yuvarlanir(int toplam, int boyut, int beklenen)
        => Assert.Equal(beklenen, new Sayfa<int>([], toplam, 1, boyut).ToplamSayfa);

    [Fact]
    public void PagedResult_kopru_alanlari_birebir_tasir()
    {
        var eski = new PagedResult<string>(["a", "b"], 7, 2, 2);
        var yeni = eski.ToPage();
        Assert.Equal(["a", "b"], yeni.Kayitlar);
        Assert.Equal(7, yeni.Toplam);
        Assert.Equal(2, yeni.SayfaNo);
        Assert.Equal(2, yeni.Boyut);
        Assert.Equal(4, yeni.ToplamSayfa);
    }

    [Fact]
    public void Donustur_kayitlari_esler_sayfa_bilgisini_korur()
    {
        var s = new Sayfa<int>([1, 2], 9, 3, 2).Convert(x => x * 10);
        Assert.Equal([10, 20], s.Kayitlar);
        Assert.Equal((9, 3, 2), (s.Toplam, s.SayfaNo, s.Boyut));
    }

    // ---- SiralamaHaritasi<T> (bellek içi IQueryable) ----

    private sealed record Satir(int Id, string Ad, int Puan);

    // Bilinçli karışık sırada; Puan=10 üç satırda eşit → eşitlik bozucu (Id) belirleyici.
    private static readonly Satir[] Veri =
    [
        new(3, "Bora", 10),
        new(1, "Cem", 10),
        new(4, "Deniz", 10),
        new(2, "Ali", 20),
    ];

    private static SortFieldMap<Satir> Harita() => SortFieldMap<Satir>
        .Create(s => s.Id)
        .Alan("ad", s => s.Ad)
        .Alan("puan", s => s.Puan);

    private static int[] Idler(SortFieldMap<Satir> h, string? sirala)
        => h.Apply(Veri.AsQueryable(), sirala).Select(s => s.Id).ToArray();

    [Theory]
    [InlineData("ad", new[] { 2, 3, 1, 4 })]      // Ali, Bora, Cem, Deniz
    [InlineData("-ad", new[] { 4, 1, 3, 2 })]
    [InlineData("puan", new[] { 1, 3, 4, 2 })]    // 10'lar Id artan, sonra 20
    [InlineData("-puan", new[] { 2, 4, 3, 1 })]   // 20, sonra 10'lar Id AZALAN (tam ters)
    [InlineData("PUAN", new[] { 1, 3, 4, 2 })]    // alan adı büyük/küçük harf duyarsız
    [InlineData(" -Ad ", new[] { 4, 1, 3, 2 })]
    public void Beyaz_listedeki_alan_ve_esitlik_bozucuyla_siralar(string sirala, int[] beklenen)
        => Assert.Equal(beklenen, Idler(Harita(), sirala));

    [Fact]
    public void Bos_sirala_varsayilani_kullanir()
    {
        var h = Harita().Default("-puan");
        Assert.Equal([2, 4, 3, 1], Idler(h, null));
        Assert.Equal([2, 4, 3, 1], Idler(h, "  "));
    }

    [Fact]
    public void Varsayilan_yoksa_yalniz_esitlik_bozucu_artan()
        => Assert.Equal([1, 2, 3, 4], Idler(Harita(), null));

    [Theory]
    [InlineData("sifre")]
    [InlineData("-sifre")]
    [InlineData("-")]
    [InlineData("Id")]                          // entity'de var ama beyaz listede YOK
    [InlineData("ad; DROP TABLE Markalar")]
    [InlineData("ad,puan")]
    public void Beyaz_listede_olmayan_alan_reddedilir(string sirala)
    {
        var ex = Assert.Throws<ValidationException>(() => Idler(Harita(), sirala));
        Assert.StartsWith("Geçersiz sıralama alanı:", ex.Message);
        Assert.Contains("İzin verilenler: ad, puan.", ex.Message);
    }

    [Fact]
    public void Red_mesaji_uzun_girdiyi_kirpar()
    {
        var ex = Assert.Throws<ValidationException>(() => Idler(Harita(), new string('x', 5000)));
        Assert.Contains(new string('x', 64) + "…'", ex.Message);
        Assert.DoesNotContain(new string('x', 65), ex.Message);
    }

    [Fact]
    public void Tanim_hatalari_programci_hatasi_olarak_firlar()
    {
        Assert.Throws<ArgumentException>(() => Harita().Alan("ad", s => s.Puan));        // iki kez
        Assert.Throws<ArgumentException>(() => Harita().Alan("AD", s => s.Puan));        // harf duyarsız çakışma
        Assert.Throws<ArgumentException>(() => Harita().Alan("-x", s => s.Puan));        // '-' önekli
        Assert.Throws<ArgumentException>(() => Harita().Alan(" x", s => s.Puan));        // boşluklu
        Assert.Throws<ArgumentException>(() => Harita().Default("yok"));              // tanımsız alan
        Assert.Throws<ArgumentException>(() => Harita().Default(" "));                // boş
    }
}

/// <summary>
/// F1.3 — gerçek PostgreSQL'e karşı sayfalama (racar_app + RLS). Eşitlik bozucu Id'ler yalnız
/// SON baytta ayrışır: hem .NET hem PostgreSQL uuid sıralaması bu baytta aynı sonucu verir →
/// beklenen sıra elle yazılabilir.
/// </summary>
[Collection("postgres")]
public sealed class ListeSozlesmesiPostgresTests(PostgresFixture fx)
{
    private static readonly SortFieldMap<Brand> Harita = SortFieldMap<Brand>
        .Create(b => b.Id)
        .Alan("ad", b => b.Ad)
        .Alan("kod", b => b.Kod)
        .Default("kod");

    [Fact]
    public async Task Gercek_sorgu_sayfalanir_esit_anahtarlar_sayfalar_arasinda_kaymaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        var kok = Guid.NewGuid().ToString("D")[..34];
        Guid Id(int n) => Guid.Parse(kok + n.ToString("x2"));

        // Başka tenant'ın markası: Toplam'a GİRMEMELİ (query filter + RLS).
        using (var diger = host.ScopeFor(Guid.NewGuid()))
        {
            var f = diger.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await f.CreateDbContextAsync();
            db.Brands.Add(new Brand { Id = Guid.NewGuid(), Kod = "YABANCI", Ad = "Aaa" });
            await db.SaveChangesAsync();
        }

        using var scope = host.ScopeFor(tenant);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            // "Fiat" üç kez: yalnız Ad'a göre sıralama belirsiz olurdu.
            db.Brands.AddRange(
                new Brand { Id = Id(5), Kod = "K5", Ad = "Fiat" },
                new Brand { Id = Id(1), Kod = "K1", Ad = "Fiat" },
                new Brand { Id = Id(4), Kod = "K4", Ad = "BMW" },
                new Brand { Id = Id(3), Kod = "K3", Ad = "Fiat" },
                new Brand { Id = Id(2), Kod = "K2", Ad = "Audi" });
            await db.SaveChangesAsync();
        }

        async Task<Sayfa<Guid>> Getir(ListeIstegi istek)
        {
            await using var db = await factory.CreateDbContextAsync();
            return await db.Brands.AsNoTracking().SayfalaAsync(istek, Harita, b => b.Id);
        }

        // Artan ad: Audi(2), BMW(4), Fiat(1), Fiat(3), Fiat(5) — Fiat'lar Id artan.
        var s1 = await Getir(new ListeIstegi(1, 2, "ad"));
        var s2 = await Getir(new ListeIstegi(2, 2, "ad"));
        var s3 = await Getir(new ListeIstegi(3, 2, "ad"));
        Assert.Equal([Id(2), Id(4)], s1.Kayitlar);
        Assert.Equal([Id(1), Id(3)], s2.Kayitlar);
        Assert.Equal([Id(5)], s3.Kayitlar);
        Assert.All(new[] { s1, s2, s3 }, s => Assert.Equal(5, s.Toplam));
        Assert.Equal(3, s1.ToplamSayfa);
        Assert.Equal((2, 2), (s2.SayfaNo, s2.Boyut));

        // Azalan ad: Fiat(5), Fiat(3), Fiat(1), BMW(4), Audi(2) — artanın tam tersi.
        Assert.Equal([Id(5), Id(3)], (await Getir(new ListeIstegi(1, 2, "-ad"))).Kayitlar);
        Assert.Equal([Id(1), Id(4)], (await Getir(new ListeIstegi(2, 2, "-ad"))).Kayitlar);
        Assert.Equal([Id(2)], (await Getir(new ListeIstegi(3, 2, "-ad"))).Kayitlar);

        // Sıralama boş → varsayılan "kod": K1..K5.
        Assert.Equal([Id(1), Id(2), Id(3)], (await Getir(new ListeIstegi(1, 3))).Kayitlar);

        // Son sayfanın ötesi ve int taşıracak sayfa numarası → boş sayfa, toplam yine 5.
        var bos = await Getir(new ListeIstegi(4, 2, "ad"));
        Assert.Empty(bos.Kayitlar);
        Assert.Equal((5, 4), (bos.Toplam, bos.SayfaNo));
        var dev = await Getir(new ListeIstegi(int.MaxValue, 200, "ad"));
        Assert.Empty(dev.Kayitlar);
        Assert.Equal(5, dev.Toplam);

        // Boyut 10000 → 200'e kırpılır; hepsi tek sayfada.
        var hepsi = await Getir(new ListeIstegi(1, 10000, "-kod"));
        Assert.Equal(200, hepsi.Boyut);
        Assert.Equal([Id(5), Id(4), Id(3), Id(2), Id(1)], hepsi.Kayitlar);

        // Entity izdüşümsüz aşırı yükleme de aynı sırayı verir.
        await using (var db = await factory.CreateDbContextAsync())
        {
            var markalar = await db.Brands.AsNoTracking().SayfalaAsync(new ListeIstegi(1, 2, "ad"), Harita);
            Assert.Equal(["Audi", "BMW"], markalar.Kayitlar.Select(b => b.Ad));
        }

        // Beyaz liste dışı alan → ValidationException (sorgu atılmadan).
        await Assert.ThrowsAsync<ValidationException>(() => Getir(new ListeIstegi(1, 2, "TenantId")));
    }
}
