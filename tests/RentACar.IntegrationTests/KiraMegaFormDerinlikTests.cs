using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Locations;
using RentACar.Application.Personnel;
using RentACar.Application.RentalAddOns;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-47 — kira mega-formu: şube (türetilmiş) / teslim eden personel / ödeme şekli /
/// misafir 2. sürücü derinliği.
///
/// <para><b>Bağımsız oracle:</b> para beklentileri ELLE kurulan senaryodan gelir — kira 3 gün ×
/// günlük 100 = <b>300</b> (Tutar = GenelToplam = Bakiye, Tahsilat 0). Bu sabit hiçbir servis/motor
/// çağrısından türetilmez; yeni alanların para hesabına sızmadığı bu sabite karşı sınanır.</para>
///
/// <para>Kilitlenen sözleşmeler:
/// (1) yeni alanlar round-trip ediyor ve form İKİ KEZ kaydedilince hiçbir alan KAYMIYOR,
/// (2) yeni alanlar (ödeme şekli, misafir 2. sürücü, teslim eden) para hesabına GİRMİYOR ve
///     ek hizmet/ücret satırı ÜRETMİYOR (kırılgan regresyon),
/// (3) kayıtlı (FK) + misafir (serbest metin) 2. sürücü BİRLİKTE reddediliyor,
/// (4) işlem şubesi yalnız ÇIKIŞ OFİSİNDEN türetiliyor (ekrandaki salt-okunur kutunun sözleşmesi),
/// (5) teslim-eden personel dropdown'ı Operatör'de PATLAMIYOR (ListForSelectAsync; ListAsync
///     ManageUsers ister — CLAUDE.md §6 mega-form tuzağı),
/// (6) tenant izolasyonu (racar_app) ve yetki (OperationsWrite) yeni alanlarda da geçerli.</para>
/// </summary>
[Collection("postgres")]
public sealed class KiraMegaFormDerinlikTests(PostgresFixture fx)
{
    /// <summary>PG timestamptz mikrosaniye, .NET tick 100ns — round-trip eşitliği Linux CI'da
    /// düşmesin diye tarih tabanı TAM SANİYEYE hizalanır.</summary>
    private static DateTimeOffset Base(int daysLater)
    {
        var t = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(daysLater), DateTimeKind.Utc), TimeSpan.Zero);
        return t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));
    }

    private const decimal DailyFee = 100m;
    /// <summary>Bağımsız oracle: 3 gün × 100 = 300 (elle kurulan senaryo; motordan türetilmedi).</summary>
    private const decimal ExpectedTotal = 300m;

    private static Task<Guid> CustomerAsync(IServiceScope s, string name)
        => s.ServiceProvider.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = name, Soyad = "Test" });

    private static Task<Guid> VehicleAsync(IServiceScope s, string plate)
        => s.ServiceProvider.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plate });

    /// <summary>3 günlük, günlük 100 TL kira açar (BookingInput üzerinden — create yolunun ta kendisi).</summary>
    private static Task<Guid> RentalAsync(IServiceScope s, Guid account, Guid vehicle, Action<BookingInput>? extra = null)
    {
        var input = new BookingInput
        {
            MusteriId = account,
            VehicleId = vehicle,
            BasTar = Base(0),
            BitTar = Base(3),
            GunlukUcret = DailyFee
        };
        extra?.Invoke(input);
        return s.ServiceProvider.GetRequiredService<RentalService>().CreateDirectAsync(input);
    }

    /// <summary>
    /// Mega-formun "tam durum gönderir" davranışını taklit eder: sözleşmenin GÜNCEL değerleri
    /// whitelist input'una prefill edilir. İki kez kaydetme testinin anlamı buradan gelir —
    /// kullanıcı hiçbir alana dokunmadan Kaydet'e basmış gibi olur.
    /// </summary>
    private static RentalUpdateInput FormState(RentACar.Domain.Entities.RentalContract c) => new()
    {
        CikisOfisi = c.CikisOfisi,
        DonusOfisi = c.DonusOfisi,
        IkinciSurucuId = c.IkinciSurucuId,
        Aciklama = c.Aciklama,
        Kaynak = c.Kaynak,
        KiralamaTuru = c.KiralamaTuru,
        DonemselFaturalama = c.DonemselFaturalama,
        FaturalamaTipi = c.FaturalamaTipi,
        KmLimit = c.KmLimit,
        FazlaKmUcret = c.FazlaKmUcret,
        YakitBirimUcret = c.YakitBirimUcret,
        Provizyon = c.Provizyon,
        Depozito = c.Depozito,
        KomisyonOran = c.KomisyonOran,
        KomisyonTutar = c.KomisyonTutar,
        DropUcreti = c.DropUcreti,
        SonraOdeOran = c.SonraOdeOran,
        UyariAciklama = c.UyariAciklama,
        OzelFaturaAciklama = c.OzelFaturaAciklama,
        FaturaListesindeGizle = c.FaturaListesindeGizle,
        UcusNo = c.UcusNo,
        ProvizyonNo = c.ProvizyonNo,
        ProvizyonTarih = c.ProvizyonTarih,
        OnayKodu = c.OnayKodu,
        FirmaKodu = c.FirmaKodu,
        ProjeAdi = c.ProjeAdi,
        OzelKod = c.OzelKod,
        OzelKdvOran = c.OzelKdvOran,
        DamgaVergisi = c.DamgaVergisi,
        TalepTuru = c.TalepTuru,
        GeldigiBirim = c.GeldigiBirim,
        KefilBilgisi = c.KefilBilgisi,
        AssistFirma = c.AssistFirma,
        OzelSoforBilgisi = c.OzelSoforBilgisi,
        EkKosullar = c.EkKosullar,
        BelgeSablonId = c.BelgeSablonId,
        ManuelFindexPuan = c.ManuelFindexPuan,
        OpsiyonNet = c.OpsiyonNet,
        OpsiyonGun = c.OpsiyonGun,
        KabisCikis = c.KabisCikis,
        KabisDonus = c.KabisDonus,
        OtomatikUzat = c.OtomatikUzat,
        AksLastikCikis = c.AksLastikCikis,
        AksLastikDonus = c.AksLastikDonus,
        // FAZ-47
        TeslimEdenPersonelId = c.TeslimEdenPersonelId,
        OdemeSekli = c.OdemeSekli,
        IkinciSurucuSerbestAd = c.IkinciSurucuSerbestAd,
        IkinciSurucuSerbestSoyad = c.IkinciSurucuSerbestSoyad,
        IkinciSurucuSerbestTel = c.IkinciSurucuSerbestTel,
        IkinciSurucuSerbestEhliyetSinifi = c.IkinciSurucuSerbestEhliyetSinifi
    };

    // ------------------------------------------------------------------ round-trip

    [Fact]
    public async Task Yeni_alanlar_round_trip_ve_iki_kez_kaydetmede_kaymiyor()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();

        var account = await CustomerAsync(scope, "Mega");
        var vehicle = await VehicleAsync(scope, "34 MF 01");
        var staff = await sp.GetRequiredService<PersonnelService>()
            .CreateAsync(new PersonelInput { Kod = "P1", Ad = "Teslim", Soyad = "Eden" });

        // CREATE yolu: ödeme şekli + misafir 2. sürücü formdan gelir.
        var id = await RentalAsync(scope, account, vehicle, i =>
        {
            i.OdemeSekli = "Havale/EFT";
            i.IkinciSurucuSerbestAd = "Misafir";
            i.IkinciSurucuSerbestSoyad = "Sürücü";
            i.IkinciSurucuSerbestTel = "0555 111 22 33";
            i.IkinciSurucuSerbestEhliyetSinifi = "B";
        });

        var c1 = (await rentals.GetAsync(id))!;
        Assert.Equal("Havale/EFT", c1.OdemeSekli);
        Assert.Equal("Misafir", c1.IkinciSurucuSerbestAd);
        Assert.Equal("Sürücü", c1.IkinciSurucuSerbestSoyad);
        Assert.Equal("0555 111 22 33", c1.IkinciSurucuSerbestTel);
        Assert.Equal("B", c1.IkinciSurucuSerbestEhliyetSinifi);
        Assert.Null(c1.TeslimEdenPersonelId); // create'te yok (teslim çıkış anında atanır)

        // UPDATE yolu: teslim eden personel + ödeme şekli değişimi.
        var form = FormState(c1);
        form.TeslimEdenPersonelId = staff;
        form.OdemeSekli = "Kredi Kartı";
        Assert.True(await rentals.UpdateOpenAsync(id, form));

        var c2 = (await rentals.GetAsync(id))!;
        Assert.Equal(staff, c2.TeslimEdenPersonelId);
        Assert.Equal("Kredi Kartı", c2.OdemeSekli);
        Assert.Equal("Misafir", c2.IkinciSurucuSerbestAd);
        Assert.Equal("B", c2.IkinciSurucuSerbestEhliyetSinifi);

        // İKİ KEZ KAYDET: form değişmeden yeniden gönderilir — hiçbir alan kaymamalı
        // (prefill round-trip tuzağı: ondalık/tarih/kültür kayması burada yakalanır).
        Assert.True(await rentals.UpdateOpenAsync(id, FormState(c2)));
        var c3 = (await rentals.GetAsync(id))!;

        Assert.Equal(c2.TeslimEdenPersonelId, c3.TeslimEdenPersonelId);
        Assert.Equal(c2.OdemeSekli, c3.OdemeSekli);
        Assert.Equal(c2.IkinciSurucuSerbestAd, c3.IkinciSurucuSerbestAd);
        Assert.Equal(c2.IkinciSurucuSerbestSoyad, c3.IkinciSurucuSerbestSoyad);
        Assert.Equal(c2.IkinciSurucuSerbestTel, c3.IkinciSurucuSerbestTel);
        Assert.Equal(c2.IkinciSurucuSerbestEhliyetSinifi, c3.IkinciSurucuSerbestEhliyetSinifi);
        // Komşu alanlar da kaymamalı (yeni alanlar mevcut whitelist'i bozmadı)
        Assert.Equal(c2.CikisOfisi, c3.CikisOfisi);
        Assert.Equal(c2.KmLimit, c3.KmLimit);
        Assert.Equal(c2.FazlaKmUcret, c3.FazlaKmUcret);
        Assert.Equal(c2.BasTar, c3.BasTar);
        Assert.Equal(c2.BitTar, c3.BitTar);
        Assert.Equal(c2.GenelToplam, c3.GenelToplam);
    }

    // ------------------------------------------------------------------ para regresyonu (kırılgan)

    [Fact]
    public async Task Yeni_alanlar_para_hesabina_GIRMIYOR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();

        var account = await CustomerAsync(scope, "Para");
        var vehicle = await VehicleAsync(scope, "34 MF 02");
        var staff = await sp.GetRequiredService<PersonnelService>()
            .CreateAsync(new PersonelInput { Kod = "P2", Ad = "Para", Soyad = "Personel" });

        // 1. senaryo: yeni alanlar BOŞ — elle kurulan oracle (3 gün × 100 = 300).
        var id = await RentalAsync(scope, account, vehicle);
        var empty = (await rentals.GetAsync(id))!;
        Assert.Equal(3, empty.Gun);
        Assert.Equal(ExpectedTotal, empty.Tutar);
        Assert.Equal(ExpectedTotal, empty.GenelToplam);
        Assert.Equal(ExpectedTotal, empty.Bakiye);
        Assert.Equal(0m, empty.Tahsilat);

        // 2. senaryo: TÜM yeni alanlar UÇUK değerlerle dolu — toplamlar AYNI sabitte kalmalı.
        var form = FormState(empty);
        form.TeslimEdenPersonelId = staff;
        form.OdemeSekli = "Ödeme Yok (Bedelsiz)";
        form.IkinciSurucuSerbestAd = "Ücretsiz";
        form.IkinciSurucuSerbestSoyad = "Misafir";
        form.IkinciSurucuSerbestTel = "0555 999 88 77";
        form.IkinciSurucuSerbestEhliyetSinifi = "B1";
        Assert.True(await rentals.UpdateOpenAsync(id, form));

        var filled = (await rentals.GetAsync(id))!;
        Assert.Equal(ExpectedTotal, filled.Tutar);
        Assert.Equal(ExpectedTotal, filled.GenelToplam);
        Assert.Equal(ExpectedTotal, filled.Bakiye);
        Assert.Equal(0m, filled.Tahsilat);
        Assert.Equal(DailyFee, filled.GunlukUcret);

        // MİSAFİR 2. sürücü ek-sürücü ÜCRET SATIRI üretmez (FK'lı sürücüden bilinçli farkı):
        // ek hizmet/sistem ücreti listesi boş kalır.
        Assert.Empty(await sp.GetRequiredService<RentalAddOnService>().ListAsync(id));
    }

    // ------------------------------------------------------------------ 2. sürücü tek yol

    [Fact]
    public async Task Kayitli_ve_misafir_2_surucu_BIRLIKTE_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();

        var account = await CustomerAsync(scope, "Ana");
        var secondAccount = await CustomerAsync(scope, "İkinci");
        var vehicle = await VehicleAsync(scope, "34 MF 03");

        // CREATE: FK + serbest metin birlikte → gürültülü red.
        var error = await Assert.ThrowsAsync<ValidationException>(() => RentalAsync(scope, account, vehicle, i =>
        {
            i.IkinciSurucuId = secondAccount;
            i.IkinciSurucuSerbestAd = "Misafir";
        }));
        Assert.Contains("2. sürücü", error.Message);

        // Yalnız FK ile açılır (kabul edilen yol).
        var id = await RentalAsync(scope, account, vehicle, i => i.IkinciSurucuId = secondAccount);
        var c = (await rentals.GetAsync(id))!;
        Assert.Equal(secondAccount, c.IkinciSurucuId);
        Assert.Null(c.IkinciSurucuSerbestAd);

        // UPDATE: FK dururken serbest metin doldurulursa → red (guard KODDA, formda değil).
        var form = FormState(c);
        form.IkinciSurucuSerbestAd = "Kaçak";
        await Assert.ThrowsAsync<ValidationException>(() => rentals.UpdateOpenAsync(id, form));

        // FK kaldırılıp serbest metne geçilebilir (geçiş yolu açık).
        var transition = FormState(c);
        transition.IkinciSurucuId = null;
        transition.IkinciSurucuSerbestAd = "Misafir";
        transition.IkinciSurucuSerbestSoyad = "Sürücü";
        Assert.True(await rentals.UpdateOpenAsync(id, transition));
        var last = (await rentals.GetAsync(id))!;
        Assert.Null(last.IkinciSurucuId);
        Assert.Equal("Misafir", last.IkinciSurucuSerbestAd);
    }

    [Fact]
    public async Task Misafir_2_surucu_SOZLESME_belgesine_basilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        var account = await CustomerAsync(scope, "Belge");
        var vehicle = await VehicleAsync(scope, "34 MF 08");
        var id = await RentalAsync(scope, account, vehicle, i =>
        {
            i.IkinciSurucuSerbestAd = "Misafir";
            i.IkinciSurucuSerbestSoyad = "Sürücü";
            i.IkinciSurucuSerbestEhliyetSinifi = "B";
        });

        var view = (await sp.GetRequiredService<ContractService>().GetAsync(id))!;
        Assert.Equal("Misafir Sürücü", view.IkinciSurucuAd);
        Assert.Equal("B", view.IkinciEhliyetSinifi);
        // TC / ehliyet NUMARASI misafir katmanında hiç tutulmaz → belgede de boş.
        Assert.Null(view.IkinciTcKimlik);
        Assert.Null(view.IkinciEhliyetNo);

        // 2. sürücü hiç girilmemiş kirada satır HİÇ görünmez (boşluk basılmaz).
        var vehicle2 = await VehicleAsync(scope, "34 MF 09"); // aynı araç+tarih çakışır → ayrı araç
        var tek = await RentalAsync(scope, account, vehicle2);
        Assert.Null((await sp.GetRequiredService<ContractService>().GetAsync(tek))!.IkinciSurucuAd);
    }

    // ------------------------------------------------------------------ işlem şube (türetilmiş)

    [Fact]
    public async Task Islem_sube_yalniz_cikis_ofisinden_turetilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        // Bağımsız oracle: şube id'leri burada ELLE kurulur; beklenen değer bu id'lerdir.
        var branches = sp.GetRequiredService<BranchService>();
        var headOffice = await branches.CreateAsync(new BranchInput { Kod = "MRK", Ad = "Merkez" });
        var ankara = await branches.CreateAsync(new BranchInput { Kod = "ANK", Ad = "Ankara" });
        var locs = sp.GetRequiredService<LocationService>();
        await locs.CreateAsync(new LocationInput { Kod = "L1", Ad = "Merkez Ofis", Sube = "Merkez" });
        await locs.CreateAsync(new LocationInput { Kod = "L2", Ad = "Ankara Ofis", Sube = "Ankara" });

        var account = await CustomerAsync(scope, "Şube");
        var vehicle = await VehicleAsync(scope, "34 MF 04");
        var rentals = sp.GetRequiredService<RentalService>();

        var id = await RentalAsync(scope, account, vehicle, i => i.CikisOfisi = "Merkez Ofis");
        var c1 = (await rentals.GetAsync(id))!;
        Assert.Equal(headOffice, c1.CikisSubeId); // ekrandaki "İşlem Şube" kutusunun kaynağı

        // Şube YALNIZ çıkış ofisi değişince değişir (ayrı bir "şube override" alanı YOK).
        var form = FormState(c1);
        form.CikisOfisi = "Ankara Ofis";
        Assert.True(await rentals.UpdateOpenAsync(id, form));
        Assert.Equal(ankara, (await rentals.GetAsync(id))!.CikisSubeId);

        // Location master'ında olmayan ofis → FK null (salt-metin davranış; kilitlenme yok).
        var free = FormState((await rentals.GetAsync(id))!);
        free.CikisOfisi = "Depo";
        Assert.True(await rentals.UpdateOpenAsync(id, free));
        Assert.Null((await rentals.GetAsync(id))!.CikisSubeId);
    }

    // ------------------------------------------------------------------ personel dropdown guard

    [Fact]
    public async Task Teslim_eden_personel_listesi_OPERATORDE_patlamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();

        Guid account, vehicle, staff, rental;
        using (var admin = host.ScopeFor(tenant))
        {
            account = await CustomerAsync(admin, "Op");
            vehicle = await VehicleAsync(admin, "34 MF 05");
            staff = await admin.ServiceProvider.GetRequiredService<PersonnelService>()
                .CreateAsync(new PersonelInput { Kod = "P3", Ad = "Ofis", Soyad = "Görevlisi" });
            rental = await RentalAsync(admin, account, vehicle);
        }

        using var op = host.ScopeFor(tenant, userId: Guid.NewGuid(), userName: "operator", role: UserRole.Operator);
        var sp = op.ServiceProvider;
        var staffMembers = sp.GetRequiredService<PersonnelService>();

        // Mega-formun kullandığı yol: PII'siz seçim projeksiyonu — Operatör'de ÇALIŞIR.
        var selection = await staffMembers.ListForSelectAsync();
        Assert.Contains(selection, p => p.Id == staff);

        // Eski yol (ManageUsers'lı tam liste) Operatör'de PATLAR — dropdown asla buna bağlanmamalı.
        await Assert.ThrowsAsync<NoPermissionException>(() => staffMembers.ListAsync());

        // Operatör teslim-eden personeli atayabilir (OperationsWrite yeter).
        var rentals = sp.GetRequiredService<RentalService>();
        var form = FormState((await rentals.GetAsync(rental))!);
        form.TeslimEdenPersonelId = staff;
        Assert.True(await rentals.UpdateOpenAsync(rental, form));
        Assert.Equal(staff, (await rentals.GetAsync(rental))!.TeslimEdenPersonelId);
    }

    // ------------------------------------------------------------------ yetki

    [Fact]
    public async Task Muhasebe_rolu_yeni_alanlari_guncelleyemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();

        Guid rental;
        RentACar.Domain.Entities.RentalContract existing;
        using (var admin = host.ScopeFor(tenant))
        {
            var account = await CustomerAsync(admin, "Yetki");
            var vehicle = await VehicleAsync(admin, "34 MF 06");
            rental = await RentalAsync(admin, account, vehicle);
            existing = (await admin.ServiceProvider.GetRequiredService<RentalService>().GetAsync(rental))!;
        }

        using var acct = host.ScopeFor(tenant, userId: Guid.NewGuid(), userName: "muhasebe", role: UserRole.Muhasebe);
        var form = FormState(existing);
        form.OdemeSekli = "Nakit";
        await Assert.ThrowsAsync<NoPermissionException>(() =>
            acct.ServiceProvider.GetRequiredService<RentalService>().UpdateOpenAsync(rental, form));
    }

    // ------------------------------------------------------------------ tenant izolasyonu

    [Fact]
    public async Task Tenant_izolasyonu_yeni_alanlarda_da_gecerli()
    {
        using var host = new TestHost(fx.AppConnectionString); // racar_app (NOSUPERUSER NOBYPASSRLS)
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        Guid rental;
        using (var s1 = host.ScopeFor(t1))
        {
            var account = await CustomerAsync(s1, "T1");
            var vehicle = await VehicleAsync(s1, "34 MF 07");
            rental = await RentalAsync(s1, account, vehicle, i =>
            {
                i.OdemeSekli = "Nakit";
                i.IkinciSurucuSerbestAd = "Gizli";
            });
        }

        using var s2 = host.ScopeFor(t2);
        var rentals2 = s2.ServiceProvider.GetRequiredService<RentalService>();
        Assert.Null(await rentals2.GetAsync(rental));                     // okuma sızmaz
        Assert.Empty(await rentals2.SearchAsync(new RentalFilter()));   // listede de yok
        // Yazma da sızmaz (RLS 0 satır → "bulunamadı" davranışı).
        Assert.False(await rentals2.UpdateOpenAsync(rental, new RentalUpdateInput { OdemeSekli = "Çek" }));

        using var s1b = host.ScopeFor(t1);
        var c = (await s1b.ServiceProvider.GetRequiredService<RentalService>().GetAsync(rental))!;
        Assert.Equal("Nakit", c.OdemeSekli);        // sahibinin verisi bozulmadı
        Assert.Equal("Gizli", c.IkinciSurucuSerbestAd);
    }
}
