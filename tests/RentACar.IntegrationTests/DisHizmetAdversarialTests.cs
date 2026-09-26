using Microsoft.Extensions.DependencyInjection;
using Npgsql;
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
/// ADVERSARIAL PROBE (commit 13eef7a — DisHizmetAlimi). Worktree-only, commit edilmez.
/// Bağımsız oracle: tüm beklenen değerler elle (1000 + %10 → gider 1000 / komisyon 100 / cari −900;
/// iptal sonrası HER rapor 0 göstermeli).
/// </summary>
[Collection("postgres")]public sealed class DisHizmetAdversarialTests(PostgresFixture fx)
{
    // 4.3 adversarial probe'larından kalıcılaştırıldı: (C) GelirGider raporu iptal sonrası SIFIR —
    // Medium bulgunun düzeltme-sonrası kilidi (Gider artık iki yönlü netlenir; Karlilik/karne/GelirGider
    // mutabakatı); (F) yeni tablonun RLS/tenant izolasyonu (CLAUDE.md §5 adım 9).
    private static readonly DateTimeOffset Start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-3);

    private static DisHizmetInput Input(Guid rental, Guid supplier, decimal charge = 1000m, decimal rate = 10m) => new()
    {
        RentalId = rental, FaturaKesilecekCariId = supplier,
        AlinanHizmet = "Probe hizmet", HizmetBedeli = charge, TedarikciKomisyonOran = rate
    };

    private static async Task<(Guid kira, Guid arac, Guid tedarikci)> ExchangeRateAsync(IServiceProvider sp, string plate)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plate });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Musteri", Soyad = "P" });
        var t = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Kurumsal, Unvan = "Probe Tedarikçi AŞ" });
        var rental = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = m, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 100m });
        return (kira: rental, v, t);
    }

    [Fact]
    public async Task ProbeC_gelir_gider_raporu_iptal_sonrasi_sifir_ve_mutabakat()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (rental, vehicle, supplier) = await ExchangeRateAsync(sp, "34 PR 03");
        var svc = sp.GetRequiredService<OutsourcedServiceService>();
        var rs = sp.GetRequiredService<ReportService>();

        var id = await svc.CreateAsync(Input(rental, supplier)); // 1000 + %10
        var gg1 = await rs.GetRevenueExpenseAsync();
        Assert.Equal(100m, gg1.GelirToplam);
        Assert.Equal(1000m, gg1.GiderToplam);

        await svc.CancelAsync(id);
        var gg2 = await rs.GetRevenueExpenseAsync();
        Assert.Equal(0m, gg2.GelirToplam);                 // gelir netleşiyor mu?
        Assert.Equal(0m, gg2.GiderToplam);                 // GİDER netleşiyor mu? (şüpheli: Debit-only)
        var profitability = await rs.GetProfitabilityAsync();
        Assert.Equal(gg2.GiderToplam, profitability.ToplamGider); // raporlar-arası mutabakat
    }

    [Fact]
    public async Task ProbeF_tenant_izolasyon_rls_ve_capraz_tenant()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        Guid rentalA, supplierA, recordA;
        using (var sa = host.ScopeFor(a))
        {
            var sp = sa.ServiceProvider;
            (rentalA, _, supplierA) = await ExchangeRateAsync(sp, "34 PR 07");
            recordA = await sp.GetRequiredService<OutsourcedServiceService>().CreateAsync(Input(rentalA, supplierA));
        }

        using (var sb = host.ScopeFor(b))
        {
            var svcB = sb.ServiceProvider.GetRequiredService<OutsourcedServiceService>();
            Assert.Empty(await svcB.ListForRentalAsync(rentalA));                       // görünmez
            await Assert.ThrowsAsync<ValidationException>(() => svcB.CancelAsync(recordA)); // iptal edemez
            // B, A'nın kirasına kayıt açamaz (RLS/null)
            Guid tedB;
            var t = await sb.ServiceProvider.GetRequiredService<CustomerService>().CreateAsync(
                new CustomerInput { Tip = CustomerType.Kurumsal, Unvan = "B Tedarikçi" });
            tedB = t;
            await Assert.ThrowsAsync<ValidationException>(() => svcB.CreateAsync(Input(rentalA, tedB)));
        }

        // HAM RLS: racar_app + B GUC'u → A satırı yok; UPDATE 0 satır; A GUC'u → 1 satır; DELETE grant yok.
        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();
        async Task SetTenant(Guid t)
        {
            await using var set = new NpgsqlCommand("select set_config('app.tenant_id', @t, false)", conn);
            set.Parameters.AddWithValue("t", t.ToString());
            await set.ExecuteScalarAsync();
        }
        await SetTenant(b);
        await using (var cmd = new NpgsqlCommand("select count(*) from \"DisHizmetAlimlari\" where \"TenantId\" = @a", conn))
        {
            cmd.Parameters.AddWithValue("a", a);
            Assert.Equal(0L, (long)(await cmd.ExecuteScalarAsync())!);
        }
        await using (var upd = new NpgsqlCommand("update \"DisHizmetAlimlari\" set \"Aciklama\" = 'hack' where \"TenantId\" = @a", conn))
        {
            upd.Parameters.AddWithValue("a", a);
            Assert.Equal(0, await upd.ExecuteNonQueryAsync()); // RLS: 0 satır
        }
        await SetTenant(a);
        await using (var cmd2 = new NpgsqlCommand("select count(*) from \"DisHizmetAlimlari\" where \"TenantId\" = @a", conn))
        {
            cmd2.Parameters.AddWithValue("a", a);
            Assert.Equal(1L, (long)(await cmd2.ExecuteScalarAsync())!);
        }
        await using (var del = new NpgsqlCommand("delete from \"DisHizmetAlimlari\"", conn))
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => del.ExecuteNonQueryAsync());
            Assert.Equal("42501", ex.SqlState); // insufficient_privilege — mali iz korunur
        }
    }
}
