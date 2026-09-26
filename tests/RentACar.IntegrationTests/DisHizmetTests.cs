using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.DisHizmetler;
using RentACar.Application.Finance;
using RentACar.Application.Kur;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ4-4.3 — B2B dış hizmet alımı (TAM DEFTERLİ). BAĞIMSIZ ORACLE (elle): bedel 1000 + tedarikçi
/// komisyon %10 → Borç Gider(araç) 1000 / Alacak Cari(tedarikçi) 1000 + Borç Cari 100 / Alacak
/// Gelir 100 → tedarikçi bakiyesi −900 (biz borçluyuz); karne HER İKİ YÖNÜ gösterir (gider
/// kategorisi + "DisHizmet" gelir kaynağı kira→araç atıflı). FX bedel: SabitKur 40 → base
/// 100×40=4000 gider / komisyon 10×40=400 (satır-bazlı yuvarlama — K2); kur çözülmezse temiz red
/// (1.1). İPTAL ters kayıtla → cari bakiye ve karne NET SIFIR; çift iptal red. Çift-submit
/// (IslemAnahtari) ikinci kayıt yazamaz. Yetki: FinanceWrite (Operatör red).
/// </summary>
[Collection("postgres")]
public sealed class DisHizmetTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-3);

    private static async Task<(Guid kira, Guid arac, Guid tedarikci)> KurAsync(IServiceProvider sp, string plaka)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Musteri", Soyad = "K" });
        var t = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Kurumsal, Unvan = "Tedarikçi AŞ" });
        var kira = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 100m });
        return (kira, v, t);
    }

    private static DisHizmetInput Girdi(Guid kira, Guid tedarikci, decimal bedel = 1000m, decimal oran = 10m) => new()
    {
        RentalId = kira, FaturaKesilecekCariId = tedarikci,
        AlinanHizmet = "Şoförlü transfer", HizmetBedeli = bedel, TedarikciKomisyonOran = oran
    };

    [Fact]
    public async Task Tam_defterli_kayit_ve_karne_iki_yon()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (kira, arac, tedarikci) = await KurAsync(sp, "34 DH 01");
        var svc = sp.GetRequiredService<OutsourcedServiceService>();

        var id = await svc.CreateAsync(Girdi(kira, tedarikci));
        var kayit = (await svc.ListForRentalAsync(kira)).Single();
        BelgeNoOracle.BeklenenlerdenBiri(10, 1, kayit.No);   // 10 = DisHizmet
        Assert.Equal(DisHizmetDurum.Kayitli, kayit.Durum);

        // Tedarikçi bakiyesi: alacak 1000 − borç 100 = −900 (elle; pozitif = müşteri borçlu konvansiyonu).
        Assert.Equal(-900m, await sp.GetRequiredService<CashService>().GetAccountBalanceAsync(tedarikci));

        // Karne İKİ YÖNÜ gösterir: gider 1000 (araçta) + gelir 100 ("DisHizmet" → kira → araç).
        var karne = (await sp.GetRequiredService<ReportService>().GetVehicleScorecardAsync(arac))!;
        Assert.Equal(1000m, karne.ToplamGider);
        Assert.Equal(100m, karne.ToplamGelir);
        Assert.Contains(karne.GelirKaynak, k => k.Kategori == "Dış Hizmet Komisyonu" && k.Tutar == 100m);

        // Çift-submit: aynı IslemAnahtari ikinci kayıt yazamaz.
        var anahtar = Guid.NewGuid();
        var g1 = Girdi(kira, tedarikci, bedel: 500m, oran: 0m); g1.IslemAnahtari = anahtar;
        var g2 = Girdi(kira, tedarikci, bedel: 500m, oran: 0m); g2.IslemAnahtari = anahtar;
        await svc.CreateAsync(g1);
        await Assert.ThrowsAsync<DuplicateOperationException>(() => svc.CreateAsync(g2));
        Assert.Equal(2, (await svc.ListForRentalAsync(kira)).Count);
    }

    [Fact]
    public async Task Fx_bedel_satir_bazli_ve_kur_cozulmezse_red()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (kira, arac, tedarikci) = await KurAsync(sp, "34 DH 02");
        await sp.GetRequiredService<FixedExchangeRateService>().UpsertAsync(new SabitKurInput { Kod = "EUR", Kur = 40m, Aktif = true });
        var svc = sp.GetRequiredService<OutsourcedServiceService>();

        // 100 EUR + %10: base gider 100×40=4000; komisyon 10 EUR → base 400 (satır-bazlı — K2 elle).
        var g = Girdi(kira, tedarikci, bedel: 100m, oran: 10m); g.Doviz = "EUR";
        await svc.CreateAsync(g);
        var karne = (await sp.GetRequiredService<ReportService>().GetVehicleScorecardAsync(arac))!;
        Assert.Equal(4000m, karne.ToplamGider);
        Assert.Equal(400m, karne.ToplamGelir);
        Assert.Equal(-3600m, await sp.GetRequiredService<CashService>().GetAccountBalanceAsync(tedarikci));

        // Kuru olmayan döviz (DKK): 1.1 sözleşmesi — sessiz kur=1 YOK, temiz red + yan etki yok.
        var bozuk = Girdi(kira, tedarikci); bozuk.Doviz = "DKK";
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(bozuk));
        Assert.Single(await svc.ListForRentalAsync(kira));
    }

    [Fact]
    public async Task Iptal_ters_kayitla_net_sifir_ve_cift_iptal_reddi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (kira, arac, tedarikci) = await KurAsync(sp, "34 DH 03");
        var svc = sp.GetRequiredService<OutsourcedServiceService>();

        var id = await svc.CreateAsync(Girdi(kira, tedarikci));
        await svc.CancelAsync(id);

        // Ters kayıt: cari bakiye 0; karne iki yönü de net sıfır; kayıt Iptal (silinmedi).
        Assert.Equal(0m, await sp.GetRequiredService<CashService>().GetAccountBalanceAsync(tedarikci));
        var karne = (await sp.GetRequiredService<ReportService>().GetVehicleScorecardAsync(arac))!;
        Assert.Equal(0m, karne.ToplamGider);
        Assert.Equal(0m, karne.ToplamGelir);
        Assert.Equal(DisHizmetDurum.Iptal, (await svc.ListForRentalAsync(kira)).Single().Durum);

        await Assert.ThrowsAsync<ValidationException>(() => svc.CancelAsync(id));
    }

    [Fact]
    public async Task Yetki_ve_dogrulamalar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid kira, tedarikci;
        using (var admin = host.ScopeFor(tenant))
        {
            (kira, _, tedarikci) = await KurAsync(admin.ServiceProvider, "34 DH 04");
        }

        // Operatör (FinanceWrite yok) dış hizmet kaydı giremez.
        using var op = host.ScopeFor(tenant, role: UserRole.Operator);
        await Assert.ThrowsAsync<NoPermissionException>(
            () => op.ServiceProvider.GetRequiredService<OutsourcedServiceService>().CreateAsync(Girdi(kira, tedarikci)));

        // Doğrulamalar: bedel ≤ 0; oran > 100.
        using var admin2 = host.ScopeFor(tenant);
        var svc = admin2.ServiceProvider.GetRequiredService<OutsourcedServiceService>();
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Girdi(kira, tedarikci, bedel: 0m)));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Girdi(kira, tedarikci, oran: 150m)));
    }
}
