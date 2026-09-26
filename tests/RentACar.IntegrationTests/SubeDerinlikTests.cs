using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.Users;
using RentACar.Application.Vehicles;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-23 — Şube derinliği + şubeye özel ücretsiz hizmet + ŞUBE BİRLEŞTİRME.
///
/// <para><b>Birleştirme bu fazın riskli parçasıdır:</b> tek çağrıda çok sayıda kaydı değiştirir ve
/// geri alınamaz. Üç savunma test edilir: (1) önizleme yazma YAPMAZ ve gerçek işlemle AYNI sayıyı
/// verir, (2) kaynak şube SİLİNMEZ (pasife çekilir), (3) kaynak=hedef ve pasif hedef reddedilir.</para>
///
/// <para><b>Kapsam çiti:</b> şubeye referans veren tablo sayısı arttıkça birleştirme listesi de
/// büyümeli. Son test, entity'lerdeki şube referanslarını YANSIMAYLA sayar ve birleştirme kodunda
/// karşılığı olmayan yeni bir referans belirirse kırmızıya döner — aksi hâlde yeni tablo sessizce
/// pasif şubede kalır ve şube kapsamı onu kimseye göstermez.</para>
/// </summary>
[Collection("postgres")]
public sealed class SubeDerinlikTests(PostgresFixture fx)
{
    private static async Task<Guid> BranchAsync(IServiceProvider sp, string code, string name)
        => await sp.GetRequiredService<BranchService>().CreateAsync(new BranchInput { Kod = code, Ad = name });

    [Fact]
    public async Task Yeni_sube_alanlari_round_trip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<BranchService>();

        var id = await svc.CreateAsync(new BranchInput
        {
            Kod = "MRK", Ad = "Merkez",
            KomisyonOran = 0.10m, HizmetKomisyonOran = 0.05m,   // İKİSİ AYRI kavram
            WebIsim = "Merkez Ofis", FirmaUnvani = "Yüce Turizm A.Ş.",
            WebRezOncesiSaat = 4, Enlem = 36.884804m, Boylam = 30.704044m,
            RezervasyonRengi = "#0ea5e9", AlisSubesiDegilMi = true, WebSira = 2,
            WebOtoparkId = "OTP-1", BayiCariKod = "BYK-1", BayiOfisId = "OFS-1",
            KomisyonHesabi = "Satıştan", OnlineRezId = "ONL-9",
            SozlesmeNoFormati = "MRK-{yyyy}-{no}", EntegrasyonKodu = "ENT-3",
            ResimDosyasi = "/img/merkez.png", HaftalikCalismaSaatleri = "Pzt-Cum 08:00-19:00"
        });

        var b = await svc.GetAsync(id);
        Assert.NotNull(b);
        // İki komisyon alanı BİRBİRİNE KARIŞMAMALI.
        Assert.Equal(0.10m, b!.KomisyonOran);
        Assert.Equal(0.05m, b.HizmetKomisyonOran);
        Assert.Equal("Merkez Ofis", b.WebIsim);
        Assert.Equal("Yüce Turizm A.Ş.", b.FirmaUnvani);
        Assert.Equal(4, b.WebRezOncesiSaat);
        Assert.Equal(36.884804m, b.Enlem);
        Assert.Equal(30.704044m, b.Boylam);
        Assert.Equal("#0ea5e9", b.RezervasyonRengi);
        Assert.True(b.AlisSubesiDegilMi);
        Assert.Equal(2, b.WebSira);
        Assert.Equal("OTP-1", b.WebOtoparkId);
        Assert.Equal("Satıştan", b.KomisyonHesabi);
        Assert.Equal("MRK-{yyyy}-{no}", b.SozlesmeNoFormati);
        Assert.Equal("/img/merkez.png", b.ResimDosyasi);
        Assert.Equal("Pzt-Cum 08:00-19:00", b.HaftalikCalismaSaatleri);
    }

    [Fact]
    public async Task Ucretsiz_hizmet_CRUD_ve_tenant_izolasyonu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        Guid branch, service;
        using (var s1 = host.ScopeFor(t1))
        {
            var sp = s1.ServiceProvider;
            var svc = sp.GetRequiredService<BranchService>();
            branch = await BranchAsync(sp, "MRK", "Merkez");
            service = await svc.AddServiceAsync(new SubeUcretsizHizmetInput
            { SubeId = branch, HizmetAdi = "  Havalimanı teslim  ", Aciklama = " ek ücret yok " });

            var list = await svc.ListServicesAsync(branch);
            Assert.Equal("Havalimanı teslim", Assert.Single(list).HizmetAdi);   // trim
            Assert.Equal("ek ücret yok", list[0].Aciklama);

            // Var olmayan şubeye hizmet eklenemez.
            await Assert.ThrowsAsync<ValidationException>(() => svc.AddServiceAsync(
                new SubeUcretsizHizmetInput { SubeId = Guid.NewGuid(), HizmetAdi = "X" }));
            await Assert.ThrowsAsync<ValidationException>(() => svc.AddServiceAsync(
                new SubeUcretsizHizmetInput { SubeId = branch, HizmetAdi = "   " }));
        }

        // Başka tenant hizmeti göremez ve silemez.
        using (var s2 = host.ScopeFor(Guid.NewGuid()))
        {
            var svc2 = s2.ServiceProvider.GetRequiredService<BranchService>();
            Assert.Empty(await svc2.ListServicesAsync(branch));
            Assert.False(await svc2.RemoveServiceAsync(service));
        }

        using var s3 = host.ScopeFor(t1);
        Assert.True(await s3.ServiceProvider.GetRequiredService<BranchService>().RemoveServiceAsync(service));
    }

    [Fact]
    public async Task Birlestirme_TUM_referanslari_tasir_kaynagi_SILMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var s = host.ScopeFor(tenant);
        var sp = s.ServiceProvider;
        var branches = sp.GetRequiredService<BranchService>();

        var old = await BranchAsync(sp, "ESK", "Eski Şube");
        var newItem = await BranchAsync(sp, "YNI", "Yeni Şube");

        // ELLE: 2 araç + 1 gider + 1 ücretsiz hizmet eski şubeye bağlanıyor.
        var vehicle = sp.GetRequiredService<VehicleService>();
        await vehicle.CreateAsync(new VehicleInput { Plaka = "34 SB 01", Sube = "Eski Şube" });
        await vehicle.CreateAsync(new VehicleInput { Plaka = "34 SB 02", Sube = "Eski Şube" });
        var supplier = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Kurumsal, Unvan = "Tedarikçi" });
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Genel, CariId = supplier, NetTutar = 100m, KdvOrani = 0.20m, Sube = "Eski Şube" });
        await branches.AddServiceAsync(new SubeUcretsizHizmetInput { SubeId = old, HizmetAdi = "Otopark" });

        // ÖNİZLEME yazma yapmamalı ve dolu gelmeli.
        var preview = await branches.PreviewMergeAsync(old, newItem);
        Assert.NotNull(preview);
        Assert.Equal("Eski Şube", preview!.KaynakAd);
        Assert.Equal("Yeni Şube", preview.HedefAd);
        Assert.True(preview.Toplam >= 4, $"beklenen ≥4, gelen {preview.Toplam}");
        // Önizleme sonrası hiçbir şey değişmemiş olmalı.
        Assert.Equal(2, (await vehicle.ListAsync()).Count(v => v.Sube == "Eski Şube"));

        var moved = await branches.MergeAsync(old, newItem);
        Assert.True(moved >= 4);

        // Araçlar ve gider hedefe geçti, kaynakta kayıt kalmadı.
        var vehicles = await vehicle.ListAsync();
        Assert.Equal(2, vehicles.Count(v => v.Sube == "Yeni Şube"));
        Assert.Empty(vehicles.Where(v => v.Sube == "Eski Şube"));
        Assert.All(vehicles, v => Assert.NotEqual(old, v.SubeId));

        // GİDER TAŞINMAZ: değişmez mali belge (DB trigger + grant). Şubesi olduğu gibi kalır —
        // kesilmiş bir gider belgesini geriye dönük başka şubeye yazmak muhasebeyi tahrif ederdi.
        var expenses = await sp.GetRequiredService<ExpenseService>().ListAsync();
        Assert.Equal("Eski Şube", Assert.Single(expenses).Sube);
        // …ama kullanıcı bunu ÖNİZLEMEDE görmüş olmalı.
        Assert.Contains(preview.Etkilenen, x => x.Tablo.Contains("TAŞINMAZ"));

        // Child kayıt da taşındı.
        Assert.Empty(await branches.ListServicesAsync(old));
        Assert.Single(await branches.ListServicesAsync(newItem));

        // Kaynak şube SİLİNMEDİ — pasife çekildi (geçmiş kayıtların adı çözülebilir kalsın).
        var source = await branches.GetAsync(old);
        Assert.NotNull(source);
        Assert.False(source!.Aktif);
        Assert.True((await branches.GetAsync(newItem))!.Aktif);

        // Birleştirme sonrası önizlemede TAŞINABİLİR kayıt kalmamalı; yalnız taşınamayan
        // (değişmez mali belge) gider satırı görünmeye devam eder.
        var after = await branches.PreviewMergeAsync(old, newItem);
        Assert.All(after!.Etkilenen, x => Assert.Contains("TAŞINMAZ", x.Tablo));
    }

    [Fact]
    public async Task Kullanicinin_atanmis_subesi_de_tasinir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var s = host.ScopeFor(tenant);
        var sp = s.ServiceProvider;
        var branches = sp.GetRequiredService<BranchService>();
        var old = await BranchAsync(sp, "ESK", "Eski");
        var newItem = await BranchAsync(sp, "YNI", "Yeni");

        var uid = await sp.GetRequiredService<UserService>().CreateAsync(new UserInput
        { UserName = "op1", DisplayName = "Operatör", Rol = UserRole.Operator, Password = "sifre123", AtanmisSube = "Eski" });

        await branches.MergeAsync(old, newItem);

        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var u = await db.Users.AsNoTracking().SingleAsync(x => x.Id == uid);
        Assert.Equal(newItem, u.AtanmisSubeId);
        Assert.Equal("Yeni", u.AtanmisSube);
    }

    [Fact]
    public async Task Gecersiz_birlestirme_reddedilir_ve_HICBIR_SEY_degismez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var branches = sp.GetRequiredService<BranchService>();
        var a = await BranchAsync(sp, "A", "A Şube");
        var b = await BranchAsync(sp, "B", "B Şube");
        await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 SB 09", Sube = "A Şube" });

        // Kendine birleştirme: tüm referansları kendine yazıp şubeyi pasife çekerdi.
        var ex = await Assert.ThrowsAsync<ValidationException>(() => branches.MergeAsync(a, a));
        Assert.Contains("farklı olmalıdır", ex.Message);

        await Assert.ThrowsAsync<ValidationException>(() => branches.MergeAsync(a, Guid.NewGuid()));
        await Assert.ThrowsAsync<ValidationException>(() => branches.MergeAsync(Guid.NewGuid(), b));

        // Pasif hedefe taşımak kayıtları görünmez yapardı.
        await branches.UpdateAsync(b, new BranchInput { Kod = "B", Ad = "B Şube", Aktif = false });
        var ex2 = await Assert.ThrowsAsync<ValidationException>(() => branches.MergeAsync(a, b));
        Assert.Contains("pasif", ex2.Message);

        // Hiçbir red yazma yapmamış olmalı.
        Assert.Equal("A Şube", Assert.Single(await sp.GetRequiredService<VehicleService>().ListAsync()).Sube);
        Assert.True((await branches.GetAsync(a))!.Aktif);
    }

    [Fact]
    public async Task Birlestirme_ADMIN_ister_operator_yapamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid a, b;
        using (var admin = host.ScopeFor(tenant))
        {
            a = await BranchAsync(admin.ServiceProvider, "A", "A");
            b = await BranchAsync(admin.ServiceProvider, "B", "B");
        }

        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator);
        var svc = op.ServiceProvider.GetRequiredService<BranchService>();
        await Assert.ThrowsAsync<NoPermissionException>(() => svc.MergeAsync(a, b));
        await Assert.ThrowsAsync<NoPermissionException>(() => svc.PreviewMergeAsync(a, b));
    }

    [Fact]
    public void Birlestirme_KAPSAMI_tum_sube_referanslarini_iceriyor()
    {
        // Şubeye referans veren her entity birleştirmede ele alınmalı. Yeni bir tablo şube
        // referansı eklerse (SubeId / CikisSubeId / Sube metni) ve birleştirme kodunda adı
        // geçmiyorsa bu test kırmızıya döner — aksi hâlde o tablo PASİF bir şubede kalır ve
        // şube kapsamı onu kimseye göstermez (sessiz veri kaybı gibi davranır).
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "RentACar.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var repoSource = File.ReadAllText(Path.Combine(root!.FullName,
            "src/RentACar.Infrastructure/Persistence/Repositories/BranchRepository.cs"));

        var missing = new List<string>();
        foreach (var t in typeof(Vehicle).Assembly.GetTypes()
                     .Where(t => t.IsClass && !t.IsAbstract && typeof(ITenantOwned).IsAssignableFrom(t)))
        {
            var branchField = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(p => p.Name is "SubeId" or "CikisSubeId" or "Sube");
            if (!branchField) continue;
            if (t == typeof(Branch) || t == typeof(SubeUcretsizHizmet)) continue;  // şubenin kendisi/child
            // Entity adı ile DbSet adı her zaman aynı değil (RentalContract→Rentals,
            // PublicBookingRequest→SiteTalepleri). Bilinen eşlemeler de kabul edilir.
            if (!Names(t.Name).Any(a => repoSource.Contains(a, StringComparison.Ordinal)))
                missing.Add(t.Name);
        }

        Assert.True(missing.Count == 0,
            "Şube referansı olan şu entity'ler birleştirme kapsamında görünmüyor:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>Entity adı → birleştirme kodunda geçebilecek adlar (DbSet adı entity adından farklı olabilir).</summary>
    private static string[] Names(string entity) => entity switch
    {
        "RentalContract" => ["Rentals"],
        "PublicBookingRequest" => ["SiteTalepleri"],
        "RateMatrix" => ["RateMatrices"],
        "Personel" => ["Personeller"],
        "Baf" => ["Baflar"],
        "DropTanim" => ["DropTanimlari"],
        "CariVirmanBilgi" => ["CariVirmanBilgileri"],
        "KasaVirmanBilgi" => ["KasaVirmanBilgileri"],
        "PersonelVardiya" => ["PersonelVardiyalari"],
        _ => [entity, entity + "s"]
    };
}
