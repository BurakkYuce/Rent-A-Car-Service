using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Pricing;
using RentACar.Application.Reporting;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Pricing;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-74 — kaydedilmiş maliyet teklifi (canlı <c>maliyet_hesaplama_ara.aspx</c>).
///
/// <para><b>BAĞIMSIZ ORACLE:</b> beklenen tutarlar testte ELLE hesaplanır (aritmetiği yorumda),
/// asla <see cref="MaliyetHesapService"/> çağrılarak üretilmez.</para>
///
/// <para><b>EN KRİTİK KİLİT:</b> "maliyet hesabı deftere/rapora SIZMAZ" regresyonu — teklif uçuk
/// tutarlarla kaydedildiğinde defter satır sayısı, gelir-gider raporu ve cari bakiye DEĞİŞMEZ.
/// Bu iki yolu (planlama ↔ muhasebe) birbirine bağlamak çift-sayım üretirdi.</para>
/// </summary>
[Collection("postgres")]
public sealed class MaliyetTeklifiTests(PostgresFixture fx)
{
    /// <summary>1.000.000 alış, %30 kalıntı, 36 ay, finansmansız, kâr 0, KDV %20.</summary>
    private static MaliyetHesapInput Girdi() => new()
    {
        AlisBedeli = 1_000_000m, ResidualYuzde = 0.30m, SureAy = 36,
        FaizOran = 0m, KkdfOran = 0m, BsmvOran = 0m, DamgaOran = 0m,
        KarMarji = 0m, KdvOran = 0.20m
    };

    private static MaliyetTeklifiInput Teklif(string baslik, MaliyetHesapInput? g = null,
        string? plaka = null, Guid? cari = null, DateTimeOffset? tarih = null) => new()
    {
        Baslik = baslik, Plaka = plaka, CariId = cari, Tarih = tarih, Girdi = g ?? Girdi()
    };

    [Fact]
    public async Task Kayit_no_BOSLUKSUZ_ve_snapshot_ELLE_hesaplanan_degerle_yazilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<MaliyetTeklifiService>();

        var g = Girdi();
        g.KaskoYillik = 12_000m;          // 12000/12 × 36 = 36.000
        g.YonetimGideriAylik = 500m;      //   500 × 36    = 18.000
        g.BankaDosyaDigerMasraf = 4_000m; //   tek seferlik =  4.000
        //                                   TOPLAM GİDER  = 58.000
        // kalıntı 300.000 → amortisman 700.000 → toplam maliyet 758.000
        // başabaş/ay = 758.000/36 = 21.055,5555… → 21.055,56
        // teklif net = 758.000 (kâr 0) → aylık 21.055,56 → KDV'li 758.000 × 1,20 = 909.600
        var id1 = await svc.CreateAsync(Teklif("İlk teklif", g, plaka: "34 MT 74"));
        var id2 = await svc.CreateAsync(Teklif("İkinci teklif"));
        var id3 = await svc.CreateAsync(Teklif("Üçüncü teklif"));

        var t1 = (await svc.GetAsync(id1))!;
        BelgeNoOracle.BeklenenlerdenBiri(17, 1, t1.KayitNo);
        BelgeNoOracle.BeklenenlerdenBiri(17, 2, (await svc.GetAsync(id2))!.KayitNo);
        BelgeNoOracle.BeklenenlerdenBiri(17, 3, (await svc.GetAsync(id3))!.KayitNo);   // sıra atlamıyor

        Assert.Equal(58_000m, t1.ToplamGider);
        Assert.Equal(700_000m, t1.NetAmortisman);
        Assert.Equal(758_000m, t1.ToplamMaliyet);
        Assert.Equal(21_055.56m, t1.BasaBasAylik);
        Assert.Equal(758_000m, t1.TeklifNet);
        Assert.Equal(21_055.56m, t1.TeklifAylikNet);
        Assert.Equal(909_600m, t1.TeklifKdvli);

        // GİRDİ de snapshot: kalem alanları satıra yazıldı (dökümü yeniden üretebilmek için).
        Assert.Equal(12_000m, t1.KaskoYillik);
        Assert.Equal(500m, t1.YonetimGideriAylik);
        Assert.Equal(4_000m, t1.BankaDosyaDigerMasraf);
        Assert.Equal("34 MT 74", t1.Plaka);
        Assert.Equal(KrediHesaplamaSekli.EsitTaksitli, t1.KrediHesaplamaSekli);

        // Snapshot'tan üretilen döküm, kayıtlı toplamla TUTAR (girdi ↔ sonuç ayrışmaz).
        var kalemler = MaliyetTeklifiService.Kalemler(t1);
        Assert.Equal(3, kalemler.Count);
        Assert.Equal(t1.ToplamGider, kalemler.Sum(k => k.DonemTutar));
    }

    [Fact]
    public async Task Snapshot_girdi_sonradan_degisse_SONUC_KAYMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var s = host.ScopeFor(tenant);
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<MaliyetTeklifiService>();

        var id = await svc.CreateAsync(Teklif("KDV snapshot"));
        // ELLE: 700.000 net amortisman, gider 0 → teklif net 700.000 → KDV'li 700.000×1,20 = 840.000
        Assert.Equal(840_000m, (await svc.GetAsync(id))!.TeklifKdvli);

        // GİRDİ kolonunu (KdvOran) doğrudan DB'de değiştir — sonucun TÜRETİLMİŞ değil SAKLANMIŞ
        // olduğunun kanıtı: KDV oranı %1'e düşse bile kayıtlı TeklifKdvli oynamaz.
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.MaliyetTeklifleri.FirstAsync(x => x.Id == id);
            row.KdvOran = 0.01m;
            await db.SaveChangesAsync();
        }

        var sonra = (await svc.GetAsync(id))!;
        Assert.Equal(0.01m, sonra.KdvOran);          // girdi değişti
        Assert.Equal(840_000m, sonra.TeklifKdvli);   // SONUÇ DEĞİŞMEDİ (snapshot)
    }

    [Fact]
    public async Task ROTATIF_kaydetmeye_calisinca_temiz_red_ve_HICBIR_satir_yazilmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<MaliyetTeklifiService>();

        var g = Girdi();
        g.KrediHesaplamaSekli = KrediHesaplamaSekli.Rotatif;

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(Teklif("Rotatif deneme", g)));
        Assert.Equal(MaliyetHesapService.RotatifRedMesaji, ex.Message);
        Assert.Empty(await svc.SearchAsync());   // yarım/yanlış kayıt YOK

        // Numara da TÜKETİLMEDİ: red hesap aşamasında, sıra tahsisinden önce olur.
        await svc.CreateAsync(Teklif("Eşit taksitli"));
        BelgeNoOracle.BeklenenlerdenBiri(17, 1, Assert.Single(await svc.SearchAsync()).KayitNo);
    }

    [Fact]
    public async Task Maliyet_teklifi_DEFTERE_ve_RAPORA_sizmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var s = host.ScopeFor(tenant);
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<MaliyetTeklifiService>();
        var rapor = sp.GetRequiredService<ReportService>();
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Kurumsal, Unvan = "Teklif AŞ" });

        var onceLedger = await LedgerSayimAsync(sp);
        var onceGg = await rapor.GetGelirGiderAsync();
        var onceBakiye = await rapor.GetCariBalancesAsync();

        // UÇUK tutarlar: 50 milyon alış, 100 araç, her kalem dolu. Rapora sızsaydı gözden kaçmazdı.
        var g = new MaliyetHesapInput
        {
            AlisBedeli = 50_000_000m, ResidualYuzde = 0.10m, SureAy = 60,
            FaizOran = 0.50m, KkdfOran = 0.15m, BsmvOran = 0.15m, DamgaOran = 0.01m,
            KarMarji = 0.35m, KdvOran = 0.20m, EnflasyonOran = 0.50m, AracSayisi = 100,
            KaskoYillik = 900_000m, TrafikSigortasiYillik = 400_000m, MtvYillik = 250_000m,
            BakimYillik = 700_000m, LastikYillik = 300_000m, LastikKisYillik = 350_000m,
            AracTakipYillik = 90_000m, TescilPlakaYillik = 60_000m, MuayeneEmisyonYillik = 45_000m,
            YedekAracYillik = 500_000m, YonetimGideriAylik = 75_000m, AylikGider = 25_000m,
            BankaDosyaDigerMasraf = 1_250_000m
        };
        var id = await svc.CreateAsync(Teklif("Dev filo teklifi", g, cari: cari));

        // Kayıt GERÇEKTEN yazıldı (test boşa dönmüyor) — ama defter/rapor kıpırdamadı.
        Assert.True((await svc.GetAsync(id))!.TeklifKdvli > 0m);
        Assert.Equal(onceLedger, await LedgerSayimAsync(sp));

        var sonraGg = await rapor.GetGelirGiderAsync();
        Assert.Equal(onceGg.GelirToplam, sonraGg.GelirToplam);
        Assert.Equal(onceGg.GiderToplam, sonraGg.GiderToplam);
        Assert.Equal(onceGg.NetKar, sonraGg.NetKar);

        // Cari bakiye de değişmedi: teklif borç/alacak DOĞURMAZ.
        var sonraBakiye = await rapor.GetCariBalancesAsync();
        Assert.Equal(onceBakiye.Sum(x => x.Bakiye), sonraBakiye.Sum(x => x.Bakiye));
        Assert.DoesNotContain(sonraBakiye, x => x.CariId == cari && x.Bakiye != 0m);
    }

    [Fact]
    public async Task Arama_filtreleri_calisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<MaliyetTeklifiService>();
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Kurumsal, Unvan = "Filo AŞ" });

        // Tarih tabanı TAM SANİYEYE hizalı (Linux CI 100ns tick ↔ Mac µs farkı testi patlatır).
        var t = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-30), DateTimeKind.Utc), TimeSpan.Zero);
        t = t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));

        // A: 1.000.000 alış, 36 ay → aylık net 700.000/36 = 19.444,4444… → 19.444,44
        await svc.CreateAsync(Teklif("Ankara filosu", Girdi(), plaka: "06 AA 11", cari: cari, tarih: t));
        // B: 2.000.000 alış → amortisman 1.400.000 → aylık net 1.400.000/36 = 38.888,8888… → 38.888,89
        var gB = Girdi(); gB.AlisBedeli = 2_000_000m;
        var izmirId = await svc.CreateAsync(Teklif("İzmir filosu", gB, plaka: "35 BB 22", tarih: t.AddDays(10)));

        Assert.Equal(2, (await svc.SearchAsync()).Count);
        Assert.Equal("Ankara filosu", Assert.Single(await svc.SearchAsync(new MaliyetTeklifiFilter { Metin = "ankara" })).Baslik);
        // Arama, ÜRETİLEN numarayla yapılır (format değiştiği için sabit dize yazılamaz).
        var ikinciNo = (await svc.GetAsync(izmirId))!.KayitNo;
        Assert.Equal(ikinciNo, Assert.Single(await svc.SearchAsync(new MaliyetTeklifiFilter { Metin = ikinciNo })).KayitNo);
        Assert.Equal("06 AA 11", Assert.Single(await svc.SearchAsync(new MaliyetTeklifiFilter { Plaka = "06 aa" })).Plaka);
        Assert.Equal("Ankara filosu", Assert.Single(await svc.SearchAsync(new MaliyetTeklifiFilter { CariId = cari })).Baslik);

        // Tarih aralığı: yalnız ilk teklif (t) — ikincisi 10 gün sonra.
        var tarihli = await svc.SearchAsync(new MaliyetTeklifiFilter { TarihMin = t.AddDays(-1), TarihMax = t.AddDays(1) });
        Assert.Equal("Ankara filosu", Assert.Single(tarihli).Baslik);

        // Fiyat aralığı ARAÇ BAŞINA aylık net üzerinden (elle: 19.444,44 ve 38.888,89).
        Assert.Equal("İzmir filosu", Assert.Single(
            await svc.SearchAsync(new MaliyetTeklifiFilter { FiyatMin = 30_000m })).Baslik);
        Assert.Equal("Ankara filosu", Assert.Single(
            await svc.SearchAsync(new MaliyetTeklifiFilter { FiyatMax = 20_000m })).Baslik);
        Assert.Equal(2, (await svc.SearchAsync(new MaliyetTeklifiFilter { FiyatMin = 19_000m, FiyatMax = 39_000m })).Count);
    }

    [Fact]
    public async Task Guncelleme_SNAPSHOTU_yeniden_hesaplar_kayit_no_DEGISMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<MaliyetTeklifiService>();

        var id = await svc.CreateAsync(Teklif("Taslak"));
        Assert.Equal(840_000m, (await svc.GetAsync(id))!.TeklifKdvli);   // 700.000 × 1,20

        // ELLE: Kasko 6.000/yıl → 6000/12 × 36 = 18.000 gider → maliyet 718.000 → KDV'li 861.600
        var g = Girdi(); g.KaskoYillik = 6_000m;
        Assert.True(await svc.UpdateAsync(id, Teklif("Revize", g, plaka: "34 RV 01")));

        var t = (await svc.GetAsync(id))!;
        BelgeNoOracle.BeklenenlerdenBiri(17, 1, t.KayitNo);      // numara KORUNUR
        Assert.Equal("Revize", t.Baslik);
        Assert.Equal(18_000m, t.ToplamGider);
        Assert.Equal(718_000m, t.ToplamMaliyet);
        Assert.Equal(861_600m, t.TeklifKdvli);
        Assert.NotNull(t.UpdatedAtUtc);

        // GEÇERSİZ güncelleme mevcut satıra DOKUNMAZ (hesap önce, yazma sonra).
        var bozuk = Girdi(); bozuk.KrediHesaplamaSekli = KrediHesaplamaSekli.Rotatif;
        await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(id, Teklif("Bozuk", bozuk)));
        Assert.Equal("Revize", (await svc.GetAsync(id))!.Baslik);

        Assert.True(await svc.DeleteAsync(id));
        Assert.False(await svc.DeleteAsync(id));
        Assert.Empty(await svc.SearchAsync());
    }

    [Fact]
    public async Task Dogrulamalar_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<MaliyetTeklifiService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Teklif("  ")));      // başlıksız
        var sifir = Girdi(); sifir.AlisBedeli = 0m;
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Teklif("Sıfır", sifir)));
        // GELECEK tarihli teklif reddedilir (arama/dönem süzgeci sessizce yanlış kovaya düşmesin).
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(Teklif("Gelecek", tarih: DateTimeOffset.UtcNow.AddDays(30))));

        Assert.Empty(await svc.SearchAsync());
    }

    [Fact]
    public async Task Yetki_operator_goremez_muhasebe_yazar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();

        // Operatör: ne FinanceWrite ne ViewReports → maliyet/kâr marjı ticari sır, OKUYAMAZ da.
        using (var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Merkez"))
        {
            var svc = op.ServiceProvider.GetRequiredService<MaliyetTeklifiService>();
            await Assert.ThrowsAsync<ValidationException>(() => svc.SearchAsync());
            await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Teklif("Operatör")));
        }

        // Muhasebe: FinanceWrite → yazar VE okur.
        using var mh = host.ScopeFor(tenant, Guid.NewGuid(), "mh", UserRole.Muhasebe);
        var m = mh.ServiceProvider.GetRequiredService<MaliyetTeklifiService>();
        await m.CreateAsync(Teklif("Muhasebe teklifi"));
        Assert.Single(await m.SearchAsync());
    }

    [Fact]
    public async Task Tenant_izolasyonu_racar_app_ile()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        using (var sa = host.ScopeFor(a))
        {
            var g = Girdi(); g.KaskoYillik = 99_999m;
            await sa.ServiceProvider.GetRequiredService<MaliyetTeklifiService>()
                .CreateAsync(Teklif("A'nın gizli teklifi", g, plaka: "34 GZ 01"));
        }

        using (var sb = host.ScopeFor(b))
        {
            var svc = sb.ServiceProvider.GetRequiredService<MaliyetTeklifiService>();
            Assert.Empty(await svc.SearchAsync());
            Assert.Empty(await svc.SearchAsync(new MaliyetTeklifiFilter { Plaka = "34 GZ 01" }));
            Assert.Equal(0m, MaliyetTeklifiService.Ozet(await svc.SearchAsync()).FiloKdvli);

            // B kendi teklifini yazınca numara 1'DEN başlar (sıra tenant başına).
            await svc.CreateAsync(Teklif("B'nin teklifi"));
            BelgeNoOracle.BeklenenlerdenBiri(17, 1, Assert.Single(await svc.SearchAsync()).KayitNo);
        }

        // HAM RLS (racar_app, NOBYPASSRLS): B GUC'uyla A'nın satırı görünmez, UPDATE/DELETE 0 satır.
        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();

        await using (var kat = new NpgsqlCommand(
            "select relrowsecurity, relforcerowsecurity from pg_class where relname = 'MaliyetTeklifleri'", conn))
        await using (var r = await kat.ExecuteReaderAsync())
        {
            Assert.True(await r.ReadAsync());
            Assert.True(r.GetBoolean(0), "ENABLE ROW LEVEL SECURITY yok");
            Assert.True(r.GetBoolean(1), "FORCE ROW LEVEL SECURITY yok");
        }

        await using (var set = new NpgsqlCommand("select set_config('app.tenant_id', @t, false)", conn))
        {
            set.Parameters.AddWithValue("t", b.ToString());
            await set.ExecuteScalarAsync();
        }
        await using (var say = new NpgsqlCommand(
            "select count(*) from \"MaliyetTeklifleri\" where \"TenantId\" = @a", conn))
        {
            say.Parameters.AddWithValue("a", a);
            Assert.Equal(0L, (long)(await say.ExecuteScalarAsync())!);
        }
        await using (var upd = new NpgsqlCommand(
            "update \"MaliyetTeklifleri\" set \"Baslik\" = 'hack' where \"TenantId\" = @a", conn))
        {
            upd.Parameters.AddWithValue("a", a);
            Assert.Equal(0, await upd.ExecuteNonQueryAsync());
        }
        await using (var del = new NpgsqlCommand(
            "delete from \"MaliyetTeklifleri\" where \"TenantId\" = @a", conn))
        {
            del.Parameters.AddWithValue("a", a);
            Assert.Equal(0, await del.ExecuteNonQueryAsync());
        }
    }

    [Fact]
    public async Task Ozet_FILO_toplamlarini_adetle_carpar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<MaliyetTeklifiService>();

        // ELLE: A → 700.000/36 = 19.444,44 aylık, 5 araç → 97.222,20 ; KDV'li 840.000 × 5 = 4.200.000
        var gA = Girdi(); gA.AracSayisi = 5;
        await svc.CreateAsync(Teklif("Beşli", gA));
        // B → 2.000.000 alış: amortisman 1.400.000 → aylık 38.888,89, 2 araç → 77.777,78
        //     KDV'li 1.400.000 × 1,20 = 1.680.000 × 2 = 3.360.000
        var gB = Girdi(); gB.AlisBedeli = 2_000_000m; gB.AracSayisi = 2;
        await svc.CreateAsync(Teklif("İkili", gB));

        var ozet = MaliyetTeklifiService.Ozet(await svc.SearchAsync());
        Assert.Equal(2, ozet.Adet);
        Assert.Equal(7, ozet.AracAdet);
        Assert.Equal(97_222.20m + 77_777.78m, ozet.FiloAylikNet);   // 174.999,98
        Assert.Equal(4_200_000m + 3_360_000m, ozet.FiloKdvli);      // 7.560.000
    }

    [Fact]
    public async Task Form_ROUND_TRIP_iki_kez_kaydetmek_degeri_KAYDIRMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<MaliyetTeklifiService>();

        // Ondalıklı, "kayma"ya en açık girdi (tr-TR virgül/InvariantCulture nokta tuzağı).
        var g = new MaliyetHesapInput
        {
            AlisBedeli = 1_234_567.89m, ResidualYuzde = 0.275m, SureAy = 41,
            FaizOran = 0.335m, KkdfOran = 0.15m, BsmvOran = 0.15m, DamgaOran = 0.0075m,
            KarMarji = 0.185m, KdvOran = 0.20m, EnflasyonOran = 0.325m, AracSayisi = 7,
            KaskoYillik = 12_345.67m, TrafikSigortasiYillik = 6_789.01m, MtvYillik = 3_456.78m,
            BakimYillik = 9_876.54m, LastikYillik = 4_321.09m, LastikKisYillik = 5_432.10m,
            AracTakipYillik = 1_111.11m, TescilPlakaYillik = 2_222.22m,
            MuayeneEmisyonYillik = 3_333.33m, YedekAracYillik = 4_444.44m,
            YonetimGideriAylik = 555.55m, AylikGider = 666.66m, BankaDosyaDigerMasraf = 7_777.77m
        };
        var id = await svc.CreateAsync(Teklif("Round-trip", g, plaka: "34 RT 01"));
        var ilk = (await svc.GetAsync(id))!;

        // 1) Kayıtlı snapshot'ı FORM ALANLARINA yaz (liste sayfasının "Forma Yükle" bağı gibi:
        //    InvariantCulture) → web ayrıştırıcısından geri oku → AYNI girdi çıkmalı.
        var alanlar = new Dictionary<string, string?>
        {
            ["alisBedeli"] = Inv(ilk.AlisBedeli), ["residual"] = Inv(ilk.ResidualYuzde),
            ["sureAy"] = ilk.SureAy.ToString(CultureInfo.InvariantCulture),
            ["faiz"] = Inv(ilk.FaizOran), ["kkdf"] = Inv(ilk.KkdfOran), ["bsmv"] = Inv(ilk.BsmvOran),
            ["damga"] = Inv(ilk.DamgaOran), ["aylikGider"] = Inv(ilk.AylikGider),
            ["kar"] = Inv(ilk.KarMarji), ["kdv"] = Inv(ilk.KdvOran),
            ["kasko"] = Inv(ilk.KaskoYillik), ["trafik"] = Inv(ilk.TrafikSigortasiYillik),
            ["mtv"] = Inv(ilk.MtvYillik), ["bakim"] = Inv(ilk.BakimYillik),
            ["lastik"] = Inv(ilk.LastikYillik), ["lastikKis"] = Inv(ilk.LastikKisYillik),
            ["takip"] = Inv(ilk.AracTakipYillik), ["tescil"] = Inv(ilk.TescilPlakaYillik),
            ["muayene"] = Inv(ilk.MuayeneEmisyonYillik), ["yedek"] = Inv(ilk.YedekAracYillik),
            ["yonetim"] = Inv(ilk.YonetimGideriAylik), ["dosya"] = Inv(ilk.BankaDosyaDigerMasraf),
            ["enflasyon"] = Inv(ilk.EnflasyonOran), ["kredi"] = ilk.KrediHesaplamaSekli.ToString(),
            ["adet"] = ilk.AracSayisi.ToString(CultureInfo.InvariantCulture)
        };
        // Ekranın gizli alan listesi ile ayrıştırıcı AYNI ad kümesini kullanmalı — biri
        // eklenip diğeri unutulursa alan sessizce kaybolur (formu ikinci kaydedişte sıfırlanır).
        Assert.Equal(MaliyetHesapGirdi.AlanAdlari.OrderBy(x => x), alanlar.Keys.OrderBy(x => x));

        var geri = MaliyetHesapGirdi.Kur(ad => alanlar.GetValueOrDefault(ad));

        // 2) İKİNCİ kez kaydet — hiçbir rakam kaymamalı.
        var id2 = await svc.CreateAsync(new MaliyetTeklifiInput { Baslik = "Round-trip 2", Girdi = geri });
        var ikinci = (await svc.GetAsync(id2))!;

        Assert.Equal(ilk.AlisBedeli, ikinci.AlisBedeli);
        Assert.Equal(ilk.ResidualYuzde, ikinci.ResidualYuzde);
        Assert.Equal(ilk.SureAy, ikinci.SureAy);
        Assert.Equal(ilk.EnflasyonOran, ikinci.EnflasyonOran);
        Assert.Equal(ilk.AracSayisi, ikinci.AracSayisi);
        Assert.Equal(ilk.KaskoYillik, ikinci.KaskoYillik);
        Assert.Equal(ilk.BankaDosyaDigerMasraf, ikinci.BankaDosyaDigerMasraf);
        Assert.Equal(ilk.ToplamGider, ikinci.ToplamGider);
        Assert.Equal(ilk.ToplamMaliyet, ikinci.ToplamMaliyet);
        Assert.Equal(ilk.TeklifAylikNet, ikinci.TeklifAylikNet);
        Assert.Equal(ilk.TeklifKdvli, ikinci.TeklifKdvli);

        // 3) Snapshot ↔ yeniden hesap tutarlılığı: kayıtlı girdiden hesap, kayıtlı sonucu verir.
        var yeniden = MaliyetHesapService.Hesapla(MaliyetTeklifiService.GirdiyeCevir(ilk));
        Assert.Equal(ilk.ToplamGider, yeniden.ToplamGider);
        Assert.Equal(ilk.ToplamMaliyet, yeniden.ToplamMaliyet);
        Assert.Equal(ilk.TeklifKdvli, yeniden.TeklifKdvli);
    }

    private static string Inv(decimal v) => v.ToString(CultureInfo.InvariantCulture);

    private static async Task<int> LedgerSayimAsync(IServiceProvider sp)
    {
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        return await db.AccountLedgerEntries.CountAsync();
    }
}
