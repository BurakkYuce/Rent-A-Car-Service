using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Penalties;
using RentACar.Application.Personnel;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kira mega-form PR-A: 31 yeni detay/aksesuar alanı persist + Kaynak parite fix + UpdateOpenAsync
/// (whitelist, durum kuralları, şube kapsamı, tenant izolasyonu) + fatura/ceza ListByRental +
/// Personel ListForSelect yetkisi. BAĞIMSIZ ORACLE: beklenen değerler senaryodan elle kurulur
/// (3 gün × 120 = 360; aşım (700−500)×2 = 400), servis kodundan türetilmez.
/// </summary>
[Collection("postgres")]
public sealed class KiraFormAlanlariTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-10).AddHours(9);

    private static async Task<(Guid cari, Guid veh)> CariAracAsync(IServiceProvider sp, string plaka)
    {
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Form", Soyad = "Musteri" });
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        return (cari, veh);
    }

    /// <summary>3 gün × 120 = 360 TL baz (elle oracle) — detay alanları parametreyle.</summary>
    private static BookingInput Kira(Guid cari, Guid veh, Action<BookingInput>? detay = null)
    {
        var input = new BookingInput
        {
            MusteriId = cari, VehicleId = veh, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 120m
        };
        detay?.Invoke(input);
        return input;
    }

    // ---------- Create: yeni alanlar persist ----------
    [Fact]
    public async Task Create_detay_alanlari_persist_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (cari, veh) = await CariAracAsync(sp, "34 KF 01");
        var rentals = sp.GetRequiredService<RentalService>();

        var id = await rentals.CreateDirectAsync(Kira(cari, veh, i =>
        {
            i.Kaynak = "Web Sitesi";
            i.UyariAciklama = "Depozito hatırlat";
            i.OzelFaturaAciklama = "Proje X faturası";
            i.FaturaListesindeGizle = true;
            i.UcusNo = "TK1923";
            i.ProvizyonNo = "PRV-777";
            i.ProvizyonTarih = Bas;
            i.OnayKodu = "ON-1";
            i.FirmaKodu = "FRM-9";
            i.ProjeAdi = "Şantiye Kuzey";
            i.OzelKod = "OZL-3";
            i.TalepTuru = "Telefon";
            i.GeldigiBirim = "Çağrı Merkezi";
            i.KefilBilgisi = "Kefil: A. Veli 0555";
            i.AssistFirma = "Yol Yardım AŞ";
            i.OzelSoforBilgisi = "Şoför: M. Can";
            i.EkKosullar = "Sigara içilmez; evcil hayvan yok.";
            i.ManuelFindexPuan = 1450;
            i.KabisCikis = true;
            i.KabisDonus = false;
            i.OtomatikUzat = true;
            i.AksYedekAnahtarCikis = true;
            i.AksStepneCikis = true;
            i.AksZincirCikis = false;
            i.AksIlkYardimCikis = true;
            i.AksLastikCikis = "ÖS:iyi ÖSğ:iyi AS:orta ASğ:iyi";
        }));

        var c = await rentals.GetAsync(id);
        Assert.NotNull(c);
        Assert.Equal(360m, c!.Tutar); // 3 × 120 (elle) — detay alanları parayı ETKİLEMEZ
        Assert.Equal("Web Sitesi", c.Kaynak);
        Assert.Equal("Depozito hatırlat", c.UyariAciklama);
        Assert.Equal("Proje X faturası", c.OzelFaturaAciklama);
        Assert.True(c.FaturaListesindeGizle);
        Assert.Equal("TK1923", c.UcusNo);
        Assert.Equal("PRV-777", c.ProvizyonNo);
        Assert.Equal(Bas, c.ProvizyonTarih);
        Assert.Equal("ON-1", c.OnayKodu);
        Assert.Equal("FRM-9", c.FirmaKodu);
        Assert.Equal("Şantiye Kuzey", c.ProjeAdi);
        Assert.Equal("OZL-3", c.OzelKod);
        Assert.Equal("Telefon", c.TalepTuru);
        Assert.Equal("Çağrı Merkezi", c.GeldigiBirim);
        Assert.Equal("Kefil: A. Veli 0555", c.KefilBilgisi);
        Assert.Equal("Yol Yardım AŞ", c.AssistFirma);
        Assert.Equal("Şoför: M. Can", c.OzelSoforBilgisi);
        Assert.Equal("Sigara içilmez; evcil hayvan yok.", c.EkKosullar);
        Assert.Equal(1450, c.ManuelFindexPuan);
        Assert.True(c.KabisCikis);
        Assert.False(c.KabisDonus);
        Assert.True(c.OtomatikUzat);
        Assert.True(c.AksYedekAnahtarCikis);
        Assert.True(c.AksStepneCikis);
        Assert.False(c.AksZincirCikis);
        Assert.True(c.AksIlkYardimCikis);
        Assert.Equal("ÖS:iyi ÖSğ:iyi AS:orta ASğ:iyi", c.AksLastikCikis);
        // Girilmeyenler null (üçlü mantık: dokunulmadı)
        Assert.Null(c.AksYedekAnahtarDonus);
        Assert.Null(c.AksLastikDonus);
    }

    [Fact]
    public async Task Create_detaysiz_eski_akis_regresyon()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (cari, veh) = await CariAracAsync(sp, "34 KF 02");
        var rentals = sp.GetRequiredService<RentalService>();
        var id = await rentals.CreateDirectAsync(Kira(cari, veh));
        var c = await rentals.GetAsync(id);
        Assert.Equal(360m, c!.Tutar);
        Assert.Null(c.Kaynak);
        Assert.Null(c.KabisCikis);
        Assert.Null(c.EkKosullar);
    }

    // ---------- UpdateOpenAsync ----------
    [Fact]
    public async Task UpdateOpen_bilgi_alanlari_degisir_para_ve_tarih_degismez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (cari, veh) = await CariAracAsync(sp, "34 KF 03");
        var rentals = sp.GetRequiredService<RentalService>();
        var id = await rentals.CreateDirectAsync(Kira(cari, veh, i => i.Kaynak = "Telefon"));

        var ok = await rentals.UpdateOpenAsync(id, new RentalUpdateInput
        {
            Aciklama = "Güncellendi",
            Kaynak = "Web Sitesi",
            KiralamaTuru = "Uzun Dönem",
            KmLimit = 500,
            FazlaKmUcret = 2m,
            YakitBirimUcret = 30m,
            UcusNo = "PC404",
            AksYedekAnahtarDonus = true,
            AksLastikDonus = "hepsi iyi",
            ManuelFindexPuan = 900
        });
        Assert.True(ok);

        var c = await rentals.GetAsync(id);
        // Para/tarih DONMUŞ (elle sabitler — create anındakiyle birebir):
        Assert.Equal(360m, c!.Tutar);
        Assert.Equal(360m, c.GenelToplam);
        Assert.Equal(120m, c.GunlukUcret);
        Assert.Equal(Bas, c.BasTar);
        Assert.Equal(Bas.AddDays(3), c.BitTar);
        Assert.Equal(3, c.Gun);
        // Whitelist alanları değişti:
        Assert.Equal("Güncellendi", c.Aciklama);
        Assert.Equal("Web Sitesi", c.Kaynak);
        Assert.Equal("Uzun Dönem", c.KiralamaTuru);
        Assert.Equal(500, c.KmLimit);
        Assert.Equal(2m, c.FazlaKmUcret);
        Assert.Equal(30m, c.YakitBirimUcret);
        Assert.Equal("PC404", c.UcusNo);
        Assert.True(c.AksYedekAnahtarDonus);
        Assert.Equal("hepsi iyi", c.AksLastikDonus);
        Assert.Equal(900, c.ManuelFindexPuan);
    }

    [Fact]
    public async Task UpdateOpen_iptal_kirada_red()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (cari, veh) = await CariAracAsync(sp, "34 KF 04");
        var rentals = sp.GetRequiredService<RentalService>();
        var id = await rentals.CreateDirectAsync(Kira(cari, veh));
        await rentals.CancelAsync(id);
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => rentals.UpdateOpenAsync(id, new RentalUpdateInput { Aciklama = "x" }));
        Assert.Contains("İptal", ex.Message);
    }

    [Fact]
    public async Task UpdateOpen_tamamlandi_asim_parametreleri_donmus_bilgi_serbest()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (cari, veh) = await CariAracAsync(sp, "34 KF 05");
        var rentals = sp.GetRequiredService<RentalService>();
        var id = await rentals.CreateDirectAsync(Kira(cari, veh, i => { i.KmLimit = 300; i.FazlaKmUcret = 2m; }));
        await rentals.DeliverAsync(id, cikisKm: 10000, cikisYakit: 8);
        await rentals.ReturnAsync(id, donusKm: 10100, donusYakit: 8, Bas.AddDays(3));

        // Aşım parametresi değişikliği RED (ReturnMath koştu; retroaktif oynanamaz):
        var ex = await Assert.ThrowsAsync<ValidationException>(() => rentals.UpdateOpenAsync(id,
            new RentalUpdateInput { KmLimit = 1, FazlaKmUcret = 2m, YakitBirimUcret = 0m }));
        Assert.Contains("aşım parametreleri", ex.Message);

        // Aynı parametrelerle bilgi alanı güncelleme SERBEST (dönüş-sonrası not/aksesuar tespiti):
        var ok = await rentals.UpdateOpenAsync(id, new RentalUpdateInput
        {
            KmLimit = 300, FazlaKmUcret = 2m, YakitBirimUcret = 0m,
            Aciklama = "Dönüş notu", AksStepneDonus = true
        });
        Assert.True(ok);
        var c = await rentals.GetAsync(id);
        Assert.Equal("Dönüş notu", c!.Aciklama);
        Assert.True(c.AksStepneDonus);
        Assert.Equal(RentalStatus.Tamamlandi, c.Durum);
    }

    [Fact]
    public async Task UpdateOpen_ikinci_surucu_dogrulama()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (cari, veh) = await CariAracAsync(sp, "34 KF 06");
        var rentals = sp.GetRequiredService<RentalService>();
        var id = await rentals.CreateDirectAsync(Kira(cari, veh));

        // Müşteriyle aynı → red
        var ex = await Assert.ThrowsAsync<ValidationException>(() => rentals.UpdateOpenAsync(id,
            new RentalUpdateInput { IkinciSurucuId = cari }));
        Assert.Contains("aynı olamaz", ex.Message);
        // Olmayan cari → red
        var ex2 = await Assert.ThrowsAsync<ValidationException>(() => rentals.UpdateOpenAsync(id,
            new RentalUpdateInput { IkinciSurucuId = Guid.NewGuid() }));
        Assert.Contains("bulunamadı", ex2.Message);
        // Geçerli cari → atanır
        var ikinci = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "İkinci", Soyad = "Sürücü" });
        Assert.True(await rentals.UpdateOpenAsync(id, new RentalUpdateInput { IkinciSurucuId = ikinci }));
        Assert.Equal(ikinci, (await rentals.GetAsync(id))!.IkinciSurucuId);
    }

    [Fact]
    public async Task UpdateOpen_sube_kapsami_operator()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid id;
        using (var seed = host.ScopeFor(tenant))
        {
            var sp = seed.ServiceProvider;
            var (cari, veh) = await CariAracAsync(sp, "34 KF 07");
            id = await sp.GetRequiredService<RentalService>()
                .CreateDirectAsync(Kira(cari, veh, i => i.CikisOfisi = "Ankara"));
        }
        // Operatör (Merkez): Ankara kirasına dokunamaz
        using (var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Merkez"))
        {
            var rentals = op.ServiceProvider.GetRequiredService<RentalService>();
            await Assert.ThrowsAsync<ValidationException>(
                () => rentals.UpdateOpenAsync(id, new RentalUpdateInput { CikisOfisi = "Ankara", Aciklama = "x" }));
        }
        // Operatör (Ankara): kendi kirası ama kapsam DIŞINA (Merkez'e) taşıyamaz
        using (var op2 = host.ScopeFor(tenant, Guid.NewGuid(), "op2", UserRole.Operator, assignedBranch: "Ankara"))
        {
            var rentals = op2.ServiceProvider.GetRequiredService<RentalService>();
            await Assert.ThrowsAsync<ValidationException>(
                () => rentals.UpdateOpenAsync(id, new RentalUpdateInput { CikisOfisi = "Merkez" }));
            // Kendi kapsamı içinde güncelleme serbest
            Assert.True(await rentals.UpdateOpenAsync(id,
                new RentalUpdateInput { CikisOfisi = "Ankara", Aciklama = "Ankara notu" }));
        }
    }

    [Fact]
    public async Task UpdateOpen_tenant_izolasyonu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant1 = Guid.NewGuid();
        Guid id;
        using (var s1 = host.ScopeFor(tenant1))
        {
            var sp = s1.ServiceProvider;
            var (cari, veh) = await CariAracAsync(sp, "34 KF 08");
            id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(Kira(cari, veh));
        }
        // tenant2 aynı kirayı GÖREMEZ → update false (RLS + query filter)
        using var s2 = host.ScopeFor(Guid.NewGuid());
        var rentals2 = s2.ServiceProvider.GetRequiredService<RentalService>();
        Assert.False(await rentals2.UpdateOpenAsync(id, new RentalUpdateInput { Aciklama = "sızma" }));
        // tenant1'de dokunulmamış
        using var s1b = host.ScopeFor(tenant1);
        Assert.Null((await s1b.ServiceProvider.GetRequiredService<RentalService>().GetAsync(id))!.Aciklama);
    }

    [Fact]
    public async Task UpdateOpen_kmlimit_donus_hesabina_yansir_oracle()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (cari, veh) = await CariAracAsync(sp, "34 KF 09");
        var rentals = sp.GetRequiredService<RentalService>();
        var id = await rentals.CreateDirectAsync(Kira(cari, veh, i => { i.KmLimit = 1000; i.FazlaKmUcret = 2m; }));

        // Kirada: limit 1000 → 500'e indirildi (mega-formdan)
        Assert.True(await rentals.UpdateOpenAsync(id, new RentalUpdateInput
        { KmLimit = 500, FazlaKmUcret = 2m, YakitBirimUcret = 0m }));

        await rentals.DeliverAsync(id, cikisKm: 10000, cikisYakit: 8);
        await rentals.ReturnAsync(id, donusKm: 10700, donusYakit: 8, Bas.AddDays(3));

        var c = await rentals.GetAsync(id);
        // ELLE ORACLE: kat edilen 700 − limit 500 = 200 fazla × 2 TL = 400 TL
        Assert.Equal(200, c!.FazlaKm);
        Assert.Equal(400m, c.FazlaKmBedeli);
        Assert.Equal(360m + 400m, c.GenelToplam); // baz 360 + aşım 400
    }

    // ---------- Fatura / Ceza ListByRental ----------
    [Fact]
    public async Task Fatura_ListByRental_base_ve_fark_baska_kira_sizmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        var invoices = sp.GetRequiredService<InvoiceService>();

        var (cariA, vehA) = await CariAracAsync(sp, "34 KF 10");
        var kiraA = await rentals.CreateDirectAsync(Kira(cariA, vehA));
        var (cariB, vehB) = await CariAracAsync(sp, "34 KF 11");
        var kiraB = await rentals.CreateDirectAsync(Kira(cariB, vehB));

        await invoices.CreateFromRentalAsync(kiraA);            // base fatura A
        await invoices.CreateFromRentalAsync(kiraB);            // base fatura B
        await rentals.ExtendAsync(kiraA, Bas.AddDays(4));        // +1 gün × 120 → fark doğar
        await invoices.CreateFromRentalAsync(kiraA);            // fark faturası A (KaynakKiraId)

        var listeA = await invoices.ListByRentalAsync(kiraA);
        var listeB = await invoices.ListByRentalAsync(kiraB);
        Assert.Equal(2, listeA.Count); // base + fark — B'ninki YOK
        Assert.Single(listeB);
        Assert.All(listeA, i => Assert.True(i.RentalId == kiraA || i.KaynakKiraId == kiraA));
    }

    [Fact]
    public async Task Ceza_ListByRental_yalniz_bagli_cezalar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        var cezalar = sp.GetRequiredService<PenaltyService>();

        var (cari, veh) = await CariAracAsync(sp, "34 KF 12");
        var kira = await rentals.CreateDirectAsync(Kira(cari, veh));

        await cezalar.CreateAsync(new PenaltyInput { CezaTuru = "Hız", Tutar = 1500m, RentalId = kira, CariId = cari });
        await cezalar.CreateAsync(new PenaltyInput { CezaTuru = "Park", Tutar = 500m }); // kirasız ceza

        var liste = await cezalar.ListByRentalAsync(kira);
        Assert.Single(liste);
        Assert.Equal("Hız", liste[0].CezaTuru);
        Assert.Equal(1500m, liste[0].Tutar);
    }

    // ---------- Personel seçim listesi yetkisi ----------
    [Fact]
    public async Task Personel_ListForSelect_operator_calisir_ListAsync_admin_kalir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using (var admin = host.ScopeFor(tenant))
        {
            await admin.ServiceProvider.GetRequiredService<PersonelService>().CreateAsync(
                new PersonelInput { Kod = "P-10", Ad = "Aktif", Soyad = "Personel" });
            await admin.ServiceProvider.GetRequiredService<PersonelService>().CreateAsync(
                new PersonelInput { Kod = "P-11", Ad = "Pasif", Soyad = "Personel", Aktif = false });
        }
        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Merkez");
        var svc = op.ServiceProvider.GetRequiredService<PersonelService>();

        // Operatör dönüş formu için seçim listesini ALABİLİR (önceki gizli bug: ManageUsers guard patlıyordu)
        var secim = await svc.ListForSelectAsync();
        Assert.Single(secim); // yalnız Aktif
        Assert.Equal("Aktif", secim[0].Ad);

        // Tam liste (PII'lı satır nesnesi) hâlâ Admin'e kilitli
        await Assert.ThrowsAsync<ValidationException>(() => svc.ListAsync());
    }
}
