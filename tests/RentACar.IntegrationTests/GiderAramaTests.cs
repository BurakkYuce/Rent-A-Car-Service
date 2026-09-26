using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Customers;
using RentACar.Application.ExpenseCategories;
using RentACar.Application.Expenses;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-63 — Gider arama filtresi (canlı <c>gider_ara.aspx</c>) + gider türü listesinde <c>Tür</c> kolonu.
///
/// <para>Bağımsız oracle: 3 gider elle kurulur (farklı plaka / tedarikçi / şube / tarih / tür) ve her
/// filtre için beklenen alt küme testte SABİT olarak yazılır — servisin dönüşünden türetilmez.</para>
///
/// <para><b>Kritik güvenlik kuralı:</b> filtre şube KAPSAMINI genişletemez. Operatörün kendi şubesi
/// dışındaki bir şubeyi filtreye yazması boş sonuç vermeli, o şubenin giderlerini AÇMAMALI —
/// aşağıda ayrı test.</para>
/// </summary>
[Collection("postgres")]
public sealed class GiderAramaTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Base = TestZaman.Now().AddDays(-30);

    private sealed record Senaryo(Guid A, Guid B, Guid C, Guid Tedarikci1, Guid Tedarikci2);

    /// <summary>3 gider: A (34 AA 01 / Merkez / Arac), B (06 BB 02 / Şube2 / Sigorta), C (araçsız / Merkez / Genel).</summary>
    private static async Task<Senaryo> ExchangeRateAsync(IServiceProvider sp)
    {
        var veh = sp.GetRequiredService<VehicleService>();
        var cust = sp.GetRequiredService<CustomerService>();
        var exp = sp.GetRequiredService<ExpenseService>();

        var v1 = await veh.CreateAsync(new VehicleInput { Plaka = "34 AA 01" });
        var v2 = await veh.CreateAsync(new VehicleInput { Plaka = "06 BB 02" });
        var t1 = await cust.CreateAsync(new CustomerInput { Tip = CustomerType.Kurumsal, Unvan = "Alfa Servis" });
        var t2 = await cust.CreateAsync(new CustomerInput { Tip = CustomerType.Kurumsal, Unvan = "Beta Sigorta" });

        var a = await exp.CreateAsync(new ExpenseInput
        {
            Tip = ExpenseType.Arac, VehicleId = v1, CariId = t1, NetTutar = 1000m, KdvOrani = 0.20m,
            Sube = "Merkez", EvrakNo = "FTR-100", Aciklama = "Lastik değişimi", Tarih = Base
        });
        var b = await exp.CreateAsync(new ExpenseInput
        {
            Tip = ExpenseType.Sigorta, VehicleId = v2, CariId = t2, NetTutar = 2000m, KdvOrani = 0.20m,
            Sube = "Şube2", EvrakNo = "FTR-200", Aciklama = "Kasko poliçesi", Tarih = Base.AddDays(10)
        });
        var c = await exp.CreateAsync(new ExpenseInput
        {
            Tip = ExpenseType.Genel, CariId = t1, NetTutar = 300m, KdvOrani = 0.20m,
            Sube = "Merkez", EvrakNo = "FTR-300", Aciklama = "Kırtasiye", Tarih = Base.AddDays(20)
        });
        return new Senaryo(a, b, c, t1, t2);
    }

    [Fact]
    public async Task Filtresiz_cagri_ESKI_davranisi_korur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        await ExchangeRateAsync(s.ServiceProvider);
        var exp = s.ServiceProvider.GetRequiredService<ExpenseService>();

        Assert.Equal(3, (await exp.ListAsync()).Count);
        // Boş filtre nesnesi de daraltmamalı.
        Assert.Equal(3, (await exp.ListAsync(new ExpenseFilter())).Count);
        // Sıralama: tarihe göre AZALAN (en yeni önce) — eski davranış.
        var list = await exp.ListAsync();
        Assert.True(list[0].Tarih >= list[1].Tarih && list[1].Tarih >= list[2].Tarih);
    }

    [Fact]
    public async Task Her_filtre_ELLE_BEKLENEN_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sen = await ExchangeRateAsync(s.ServiceProvider);
        var exp = s.ServiceProvider.GetRequiredService<ExpenseService>();

        // Metin araması: evrak no
        Assert.Equal(sen.B, Assert.Single(await exp.ListAsync(new ExpenseFilter { Ara = "FTR-200" })).Id);
        // …açıklama (küçük-büyük harf duyarsız)
        Assert.Equal(sen.A, Assert.Single(await exp.ListAsync(new ExpenseFilter { Ara = "lastik" })).Id);
        // …hiçbirine uymayan metin
        Assert.Empty(await exp.ListAsync(new ExpenseFilter { Ara = "bulunmayan-metin" }));

        // Plaka (kısmi, araç tablosundan çözülür). Araçsız gider (C) ASLA plaka filtresine düşmemeli.
        Assert.Equal(sen.A, Assert.Single(await exp.ListAsync(new ExpenseFilter { Plaka = "34 AA" })).Id);
        Assert.Equal(sen.B, Assert.Single(await exp.ListAsync(new ExpenseFilter { Plaka = "bb" })).Id);
        Assert.DoesNotContain(await exp.ListAsync(new ExpenseFilter { Plaka = "0" }), e => e.Id == sen.C);

        // Tedarikçi: t1 → A ve C
        Assert.Equal(2, (await exp.ListAsync(new ExpenseFilter { CariId = sen.Tedarikci1 })).Count);
        Assert.Equal(sen.B, Assert.Single(await exp.ListAsync(new ExpenseFilter { CariId = sen.Tedarikci2 })).Id);

        // Gider türü
        Assert.Equal(sen.B, Assert.Single(await exp.ListAsync(new ExpenseFilter { Tip = ExpenseType.Sigorta })).Id);

        // Şube (tam eşleşme)
        Assert.Equal(2, (await exp.ListAsync(new ExpenseFilter { Sube = "Merkez" })).Count);
        Assert.Equal(sen.B, Assert.Single(await exp.ListAsync(new ExpenseFilter { Sube = "Şube2" })).Id);

        // Tarih aralığı: Taban+5 sonrası → B ve C
        Assert.Equal(2, (await exp.ListAsync(new ExpenseFilter { Bas = Base.AddDays(5) })).Count);
        // Üst sınır: Taban+15'e kadar → A ve B
        Assert.Equal(2, (await exp.ListAsync(new ExpenseFilter { Bit = Base.AddDays(15) })).Count);
        // Kapalı aralık: yalnız B
        Assert.Equal(sen.B, Assert.Single(await exp.ListAsync(
            new ExpenseFilter { Bas = Base.AddDays(5), Bit = Base.AddDays(15) })).Id);

        // Birleşik: Merkez + Genel türü → yalnız C
        Assert.Equal(sen.C, Assert.Single(await exp.ListAsync(
            new ExpenseFilter { Sube = "Merkez", Tip = ExpenseType.Genel })).Id);
        // Çelişen birleşim → boş (Şube2 + Genel yok)
        Assert.Empty(await exp.ListAsync(new ExpenseFilter { Sube = "Şube2", Tip = ExpenseType.Genel }));
    }

    [Fact]
    public async Task Filtre_sube_KAPSAMINI_GENISLETEMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using (var admin = host.ScopeFor(tenant)) await ExchangeRateAsync(admin.ServiceProvider);

        // Operatör yalnız "Merkez" şubesini görür.
        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Merkez");
        var exp = op.ServiceProvider.GetRequiredService<ExpenseService>();

        Assert.Equal(2, (await exp.ListAsync()).Count);                 // kapsam: A + C
        // Kapsam DIŞINDAKİ şubeyi filtreye yazmak o şubeyi AÇMAZ.
        Assert.Empty(await exp.ListAsync(new ExpenseFilter { Sube = "Şube2" }));
        // Kapsam içindeki şube normal daraltır.
        Assert.Equal(2, (await exp.ListAsync(new ExpenseFilter { Sube = "Merkez" })).Count);
        // Kapsam dışındaki aracın plakası da sızmamalı.
        Assert.Empty(await exp.ListAsync(new ExpenseFilter { Plaka = "06 BB" }));
    }

    [Fact]
    public async Task Giderler_tenant_izolasyonlu_filtreyle_de()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        using (var s1 = host.ScopeFor(t1)) await ExchangeRateAsync(s1.ServiceProvider);

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var exp = s2.ServiceProvider.GetRequiredService<ExpenseService>();
        Assert.Empty(await exp.ListAsync());
        Assert.Empty(await exp.ListAsync(new ExpenseFilter { Ara = "FTR" }));
        Assert.Empty(await exp.ListAsync(new ExpenseFilter { Plaka = "34" }));
    }

    [Fact]
    public async Task Gider_turu_Tur_alani_round_trip_ve_listede_gorunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<ExpenseCategoryService>();

        var id = await svc.CreateAsync(new ExpenseCategoryInput { Kod = "YKT", Ad = "Yakıt", Tur = "Araç Gideri" });
        Assert.Equal("Araç Gideri", (await svc.GetAsync(id))!.Tur);

        // Sayfa çiti: kolon sessizce düşerse test kırmızıya döner.
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        // F13.1a: Blazor ExpenseCategoryList.razor silindi; yeni arayüzün tanım kataloğunda gider türü "tur" alanı formda
        // ve listede (inList: false YOK) durmalı.
        var catalog = File.ReadAllText(Path.Combine(d!.FullName,
            "src/RentACar.Frontend/src/app/features/definitions/definition-catalog.ts"));
        var block = Regex.Match(catalog, @"case 'expenseCategory':(?<b>[\s\S]*?)case '").Groups["b"].Value;
        Assert.Contains("text('tur', l('tur')", block, StringComparison.Ordinal);
        Assert.DoesNotContain("inList: false", block, StringComparison.Ordinal);
    }
}
