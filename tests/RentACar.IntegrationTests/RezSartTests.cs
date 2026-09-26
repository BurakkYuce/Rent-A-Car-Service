using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.RezSartlar;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-25 — Rez şartı (müşteri özel talebi) yeni dikeyi. CRUD + filtre + karşılandı yaşam döngüsü +
/// yetki + tenant izolasyonu (racar_app).
///
/// <para>Bağımsız oracle: senaryolar elle kurulur (3 kayıt: 2 karşılanmış, 1 bekleyen) ve beklenen
/// SAYILAR testte sabittir — repo sorgusundan türetilmez.</para>
///
/// <para>Bu tablo PARA TAŞIMAZ: deftere kayıt postlamadığı için adversarial para incelemesi
/// kapsamında değil. Buna karşılık iki kural teste bağlandı: (a) durum tek kaynaktan türetilir
/// (<c>KarsilamaTarihi</c> null ⇔ bekliyor — ayrı bayrak yok), (b) tekrar "karşılandı" tıklaması ilk
/// karşılanma anını İLERİ KAYDIRMAZ.</para>
/// </summary>
[Collection("postgres")]
public sealed class RezSartTests(PostgresFixture fx)
{
    /// <summary>Tam saniyeye hizalı "şimdi" — PG µs/Linux 100ns tuzağı (bkz. <see cref="TestZaman"/>).</summary>
    private static DateTimeOffset Simdi() => TestZaman.Simdi();

    private static async Task<Guid> MusteriAsync(IServiceProvider sp, string ad = "Talep", string soyad = "Sahibi")
        => await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = ad, Soyad = soyad });

    [Fact]
    public async Task Create_roundtrips_ve_bekleyen_olarak_baslar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var m = await MusteriAsync(sp);
        var svc = sp.GetRequiredService<ReservationTermService>();

        var bas = DateTimeOffset.UtcNow.Date.AddDays(3);
        var id = await svc.CreateAsync(new RezSartInput
        {
            MusteriId = m, Sart = "  Bebek koltuğu istiyor  ", Grup = " Ekipman ",
            BasTar = new DateTimeOffset(bas, TimeSpan.Zero), BitTar = new DateTimeOffset(bas.AddDays(4), TimeSpan.Zero),
            TeslimEden = " Ahmet "
        });

        var r = await svc.GetAsync(id);
        Assert.NotNull(r);
        Assert.Equal("Bebek koltuğu istiyor", r!.Sart);   // trim
        Assert.Equal("Ekipman", r.Grup);
        Assert.Equal("Ahmet", r.TeslimEden);
        Assert.Equal(m, r.MusteriId);
        Assert.Null(r.KarsilamaTarihi);                    // yeni talep = bekliyor
        Assert.True(r.TalepTarihi <= DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task Bos_sart_ve_gecersiz_musteri_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var m = await MusteriAsync(sp);
        var svc = sp.GetRequiredService<ReservationTermService>();

        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new RezSartInput { MusteriId = m, Sart = "   " }));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new RezSartInput { MusteriId = Guid.Empty, Sart = "Bir şey" }));
        // Var olmayan müşteri: "bulunamadı" — sessiz yetim kayıt oluşmamalı.
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new RezSartInput { MusteriId = Guid.NewGuid(), Sart = "Bir şey" }));
        Assert.Contains("müşteri bulunamadı", ex.Message);
        Assert.Empty(await svc.ListAsync());
    }

    [Fact]
    public async Task Ters_tarih_ve_gelecek_talep_tarihi_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var m = await MusteriAsync(sp);
        var svc = sp.GetRequiredService<ReservationTermService>();

        var g = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new RezSartInput
        { MusteriId = m, Sart = "Ters", BasTar = g.AddDays(5), BitTar = g.AddDays(2) }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new RezSartInput
        { MusteriId = m, Sart = "Gelecek talep", TalepTarihi = g.AddDays(10) }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new RezSartInput
        { MusteriId = m, Sart = "Gelecek karşılama", KarsilamaTarihi = g.AddDays(10) }));
    }

    [Fact]
    public async Task Durum_filtresi_ELLE_KURULAN_sayilari_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var m = await MusteriAsync(sp);
        var svc = sp.GetRequiredService<ReservationTermService>();

        // Senaryo elle: 3 talep, 2'si karşılandı, 1'i bekliyor.
        var a = await svc.CreateAsync(new RezSartInput { MusteriId = m, Sart = "A" });
        var b = await svc.CreateAsync(new RezSartInput { MusteriId = m, Sart = "B" });
        await svc.CreateAsync(new RezSartInput { MusteriId = m, Sart = "C" });
        await svc.MarkFulfilledAsync(a, "Ayşe");
        await svc.MarkFulfilledAsync(b);

        Assert.Equal(3, (await svc.ListAsync()).Count);
        Assert.Equal(2, (await svc.ListAsync(new RezSartFilter { Karsilandi = true })).Count);
        var bekleyen = await svc.ListAsync(new RezSartFilter { Karsilandi = false });
        Assert.Single(bekleyen);
        Assert.Equal("C", bekleyen[0].Sart);

        // Teslim eden yalnız verildiğinde yazılır; verilmeyende null kalır.
        Assert.Equal("Ayşe", (await svc.GetAsync(a))!.TeslimEden);
        Assert.Null((await svc.GetAsync(b))!.TeslimEden);
    }

    [Fact]
    public async Task Musteri_ve_tarih_filtresi_dogru_daraltir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var m1 = await MusteriAsync(sp, "Bir", "Musteri");
        var m2 = await MusteriAsync(sp, "Iki", "Musteri");
        var svc = sp.GetRequiredService<ReservationTermService>();

        var dun = Simdi().AddDays(-1);
        var geven = Simdi().AddDays(-10);
        await svc.CreateAsync(new RezSartInput { MusteriId = m1, Sart = "Dünkü", TalepTarihi = dun });
        await svc.CreateAsync(new RezSartInput { MusteriId = m1, Sart = "Eski", TalepTarihi = geven });
        await svc.CreateAsync(new RezSartInput { MusteriId = m2, Sart = "Diğer müşteri" });

        Assert.Equal(2, (await svc.ListAsync(new RezSartFilter { MusteriId = m1 })).Count);
        Assert.Single(await svc.ListAsync(new RezSartFilter { MusteriId = m2 }));

        // Son 3 gün: yalnız "Dünkü" ve m2'nin bugünkü kaydı → 2.
        var son3 = await svc.ListAsync(new RezSartFilter { TarihBas = DateTimeOffset.UtcNow.AddDays(-3) });
        Assert.Equal(2, son3.Count);
        Assert.DoesNotContain(son3, r => r.Sart == "Eski");
    }

    [Fact]
    public async Task Tekrar_karsilandi_ilk_ani_ILERI_KAYDIRMAZ_geri_alma_calisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var m = await MusteriAsync(sp);
        var svc = sp.GetRequiredService<ReservationTermService>();

        var id = await svc.CreateAsync(new RezSartInput { MusteriId = m, Sart = "Tek sefer" });
        await svc.MarkFulfilledAsync(id);
        var ilk = (await svc.GetAsync(id))!.KarsilamaTarihi;
        Assert.NotNull(ilk);

        await Task.Delay(30);
        await svc.MarkFulfilledAsync(id);
        Assert.Equal(ilk, (await svc.GetAsync(id))!.KarsilamaTarihi);   // idempotent

        await svc.UndoFulfillmentAsync(id);
        Assert.Null((await svc.GetAsync(id))!.KarsilamaTarihi);
    }

    [Fact]
    public async Task Update_talep_tarihi_gonderilmezse_KORUNUR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var m = await MusteriAsync(sp);
        var svc = sp.GetRequiredService<ReservationTermService>();

        var eski = Simdi().AddDays(-20);
        var id = await svc.CreateAsync(new RezSartInput { MusteriId = m, Sart = "Eski talep", TalepTarihi = eski });
        await svc.UpdateAsync(id, new RezSartInput { MusteriId = m, Sart = "Düzeltildi" });   // TalepTarihi YOK

        var r = await svc.GetAsync(id);
        Assert.Equal("Düzeltildi", r!.Sart);
        Assert.Equal(eski, r.TalepTarihi);   // geçmiş sessizce "şimdi"ye kaymadı
    }

    [Fact]
    public async Task Yetkisiz_rol_yazamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid m;
        using (var admin = host.ScopeFor(tenant)) m = await MusteriAsync(admin.ServiceProvider);

        // Muhasebe: FinanceWrite var, OperationsWrite YOK.
        using var s = host.ScopeFor(tenant, Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = s.ServiceProvider.GetRequiredService<ReservationTermService>();
        await Assert.ThrowsAsync<NoPermissionException>(
            () => svc.CreateAsync(new RezSartInput { MusteriId = m, Sart = "Yetkisiz" }));
    }

    [Fact]
    public async Task RezSartlar_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
        {
            var m = await MusteriAsync(s1.ServiceProvider);
            await s1.ServiceProvider.GetRequiredService<ReservationTermService>()
                .CreateAsync(new RezSartInput { MusteriId = m, Sart = "T1 gizli talep" });
        }

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<ReservationTermService>();
        Assert.Empty(await svc2.ListAsync());

        var m2 = await MusteriAsync(s2.ServiceProvider);
        await svc2.CreateAsync(new RezSartInput { MusteriId = m2, Sart = "T2 talebi" });
        Assert.Single(await svc2.ListAsync());
    }

    [Fact]
    public async Task Baska_tenantin_musterisine_talep_ACILAMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        Guid yabanciMusteri;
        using (var s1 = host.ScopeFor(t1)) yabanciMusteri = await MusteriAsync(s1.ServiceProvider);

        using var s2 = host.ScopeFor(t2);
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            s2.ServiceProvider.GetRequiredService<ReservationTermService>()
                .CreateAsync(new RezSartInput { MusteriId = yabanciMusteri, Sart = "Sızıntı denemesi" }));
        Assert.Contains("müşteri bulunamadı", ex.Message);
    }
}
