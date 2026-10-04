using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Finance;
using RentACar.Application.GelenEFaturalar;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>#378 adversarial tur 3 probe'larının kalıcı hâli (beklenenler elle kurulan senaryodan).</summary>
public sealed partial class UiReportTests
{
    private static async Task<string> ExportBody(Session s, string url)
    {
        var res = await s.C.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await res.Content.ReadAsStringAsync();
    }

    /// <summary>M1: bireysel carinin vergi no'su (TC) ne cariler'de ne fatura-detaylari'nda yazılır (ekranda da yok).</summary>
    [Fact]
    public async Task Invoice_details_export_does_not_write_individual_tax_number()
    {
        var e = await SetupAsync(ledger: false);
        var s = await LoginAsync(e, Who.Admin);
        const string tc = "98765432109";
        await WriteAsync(e.TenantId, db => db.Customers.Where(c => c.Id == e.CustomerA).ExecuteUpdate(u => u.SetProperty(c => c.VergiNo, tc)));
        using (var host = new TestHost(fx.Pg.AppConnectionString))
        using (var scope = host.ScopeFor(e.TenantId))
            await scope.ServiceProvider.GetRequiredService<InvoiceService>()
                .CreateManualAsync(new ManualInvoiceInput { CariId = e.CustomerA, NetTutar = 100m, KdvOrani = 0.20m });

        Assert.DoesNotContain(tc, await ExportBody(s, "/listeler/export/cariler?format=csv"));
        var details = await ExportBody(s, "/listeler/export/fatura-detaylari?format=csv");
        Assert.DoesNotContain(tc, details);
        Assert.Contains(CustomerA, details);   // satır dosyada (boş dosyayla geçmesin)
    }

    /// <summary>L1: anonim B ile AYNI ADLI ama anonim OLMAYAN D carisi gerçek adıyla yazılır (ekran kimlikle karar verir).</summary>
    [Fact]
    public async Task Same_name_non_anonymous_customer_is_not_masked_when_id_known()
    {
        var e = await SetupAsync(ledger: false);
        var s = await LoginAsync(e, Who.Admin);
        var d = new Customer { Tip = RentACar.Domain.Enums.CustomerType.Bireysel, Ad = AnonymousRealName, Soyad = "Kisi", CepTel = "05321234567" };
        await WriteAsync(e.TenantId, db => db.Customers.Add(d));
        var body = await ExportBody(s, "/listeler/export/cariler?format=csv");
        Assert.Contains(AnonymousRealName + " Kisi", body);
        Assert.Contains("05321234567", body);
        Assert.DoesNotContain("05329999999", body);   // anonim B'nin telefonu hâlâ gizli
    }

    /// <summary>Operatör (SubeA) fatura-detaylari export'unda SubeB kirasının faturasını görmez (ekran şube kapsamlı).</summary>
    [Fact]
    public async Task Invoice_details_export_respects_operator_branch_scope()
    {
        var e = await SetupAsync(ledger: false);
        string plateB;
        using (var host = new TestHost(fx.Pg.AppConnectionString))
        using (var scope = host.ScopeFor(e.TenantId))
        {
            var sp = scope.ServiceProvider;
            await using (var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
                plateB = (await db.Vehicles.SingleAsync(v => v.Id == e.VehicleB)).Plaka;
            var rid = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
            {
                MusteriId = e.CustomerA, VehicleId = e.VehicleB, GunlukUcret = 300m,
                BasTar = TestZaman.DaysLater(-9), BitTar = TestZaman.DaysLater(-5),
            });
            await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(rid, vatRate: 0.20m);
        }
        var op = await LoginAsync(e, Who.OperatorA);
        var res = await op.C.GetAsync("/listeler/export/fatura-detaylari?format=csv");
        var body = await res.Content.ReadAsStringAsync();
        Assert.False(res.StatusCode == HttpStatusCode.OK && body.Contains(plateB), $"{(int)res.StatusCode}: SubeB plakası dosyada");
    }

    /// <summary>M2: kanonik biçimden önce küçük harfle saklanmış ETTN, aynı belgenin büyük harfli yeni girişini engeller;
    /// DB'de (TenantId, upper(Ettn)) unique index'i yazım farkıyla mükerrer satırı da reddeder.</summary>
    [Fact]
    public async Task Legacy_lowercase_ettn_blocks_canonical_duplicate()
    {
        var e = await SetupAsync(ledger: false);
        var ettn = Guid.NewGuid().ToString("D");
        GelenEFatura Row(string value) => new()
        {
            TenantId = e.TenantId, Ettn = value, GonderenVkn = "1234567890", GonderenUnvan = "T",
            Tarih = TestZaman.DaysLater(-3), NetTutar = 100m, KdvTutar = 20m, GenelToplam = 120m
        };
        await WriteAsync(e.TenantId, db => db.GelenEFaturalar.Add(Row(ettn.ToLowerInvariant())));

        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(e.TenantId);
        var svc = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();
        await Assert.ThrowsAnyAsync<RentACar.Application.Common.ValidationException>(() => svc.CreateManualAsync(new GelenEFaturaInput
        {
            Ettn = ettn.ToUpperInvariant(), GonderenVkn = "1234567890", GonderenUnvan = "T",
            Tarih = TestZaman.DaysLater(-3), NetTutar = 100m, KdvTutar = 20m, GenelToplam = 120m, Currency = "TRY"
        }));

        // Servisi atlayan yazım (yarış / doğrudan SQL) da DB index'ine çarpar.
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => WriteAsync(e.TenantId, db => db.GelenEFaturalar.Add(Row(ettn.ToUpperInvariant()))));
        Assert.Contains("IX_GelenEFaturalar_TenantId_UpperEttn", ex.InnerException?.Message ?? "");
    }
}
