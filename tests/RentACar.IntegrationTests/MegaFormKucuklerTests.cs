using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ 4.4 — mega-form küçükleri: (1) kira-seviyesi Opsiyon alanları (bilgi; persist + update);
/// (2) RİSK GUARD'ı — cari RiskLimiti (>0) aşımında kira açılışı yalnız Yönetici/Admin RiskOnay'ıyla
/// (guard GİRİŞ noktasında; Operatör onayı geçersiz); (3) sözleşme çıktısına Ek Koşullar.
/// BAĞIMSIZ ORACLE: limit 100, manuel fatura borcu 150 → aşım (elle).
/// </summary>
[Collection("postgres")]
public sealed class MegaFormKucuklerTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-2).AddHours(9);

    private static BookingInput Kira(Guid musteri, Guid arac) => new()
    { MusteriId = musteri, VehicleId = arac, BasTar = Bas, BitTar = Bas.AddDays(2), GunlukUcret = 100m };

    [Fact]
    public async Task Opsiyon_alanlari_persist_ve_update()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var arac = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 MK 01" });
        var musteri = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Ops", Soyad = "Cari" });
        var rentals = sp.GetRequiredService<RentalService>();

        var girdi = Kira(musteri, arac);
        girdi.OpsiyonNet = 150.50m; girdi.OpsiyonGun = 3;
        var id = await rentals.CreateDirectAsync(girdi);

        var c = await rentals.GetAsync(id);
        Assert.Equal(150.50m, c!.OpsiyonNet);
        Assert.Equal(3, c.OpsiyonGun);

        // Bilgi alanı — açık kirada güncellenebilir; whitelist para/tarih alanlarını hâlâ TİPTE taşımaz.
        await rentals.UpdateOpenAsync(id, new RentalUpdateInput { OpsiyonNet = 200m, OpsiyonGun = 5 });
        c = await rentals.GetAsync(id);
        Assert.Equal(200m, c!.OpsiyonNet);
        Assert.Equal(5, c.OpsiyonGun);
        Assert.Equal(200m, c.GenelToplam); // 2g×100 — para alanları update'ten etkilenmedi (elle)
    }

    [Fact]
    public async Task Risk_limiti_asiminda_onaysiz_kira_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant); // Admin
        var sp = scope.ServiceProvider;
        var arac = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 MK 02" });
        var musteri = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Riskli", Soyad = "Cari", RiskLimiti = 100m });
        // Borç 150 (elle): manuel fatura → Borç Cari 150 > limit 100.
        await sp.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput
        { CariId = musteri, NetTutar = 150m, KdvOrani = 0m, Aciklama = "borç" });

        var rentals = sp.GetRequiredService<RentalService>();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => rentals.CreateDirectAsync(Kira(musteri, arac)));
        Assert.Contains("Risk limiti aşıldı", ex.Message);

        // Onaylı + Admin → geçer.
        var onayli = Kira(musteri, arac);
        onayli.RiskOnay = true;
        var id = await rentals.CreateDirectAsync(onayli);
        Assert.True((await rentals.GetAsync(id))!.RiskOnay);
    }

    [Fact]
    public async Task Risk_onayini_operator_veremez_limitsizde_guard_yok()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid arac, riskli, limitsiz;
        using (var seed = host.ScopeFor(tenant))
        {
            var sp = seed.ServiceProvider;
            arac = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 MK 03" });
            riskli = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
            { Tip = CustomerType.Bireysel, Ad = "Riskli2", Soyad = "Cari", RiskLimiti = 100m });
            limitsiz = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
            { Tip = CustomerType.Bireysel, Ad = "Limitsiz", Soyad = "Cari" }); // RiskLimiti 0 → guard yok
            await sp.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput
            { CariId = riskli, NetTutar = 150m, KdvOrani = 0m, Aciklama = "borç" });
        }

        // Operatör: onay işaretlese bile RED (rol doğrulaması serviste — UI gizlemesi yeterli değil).
        using (var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator))
        {
            var rentals = op.ServiceProvider.GetRequiredService<RentalService>();
            var girdi = Kira(riskli, arac);
            girdi.RiskOnay = true;
            var ex = await Assert.ThrowsAsync<ValidationException>(() => rentals.CreateDirectAsync(girdi));
            Assert.Contains("Yönetici/Admin", ex.Message);

            // Limitsiz cari: borcu olsa da limit tanımsız (0) → guard devrede değil.
            var serbest = await rentals.CreateDirectAsync(Kira(limitsiz, arac));
            Assert.NotEqual(Guid.Empty, serbest);
        }
    }

    [Fact]
    public async Task Ek_kosullar_sozlesme_ciktisina_akar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var arac = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 MK 04" });
        var musteri = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Ek", Soyad = "Kosul" });
        var rentals = sp.GetRequiredService<RentalService>();

        var girdi = Kira(musteri, arac);
        girdi.EkKosullar = "Araç yurt dışına çıkarılamaz.";
        var id = await rentals.CreateDirectAsync(girdi);

        var view = await sp.GetRequiredService<ContractService>().GetAsync(id);
        Assert.Equal("Araç yurt dışına çıkarılamaz.", view!.EkKosullar); // PDF/print aynı view-model'den basar
    }
}
