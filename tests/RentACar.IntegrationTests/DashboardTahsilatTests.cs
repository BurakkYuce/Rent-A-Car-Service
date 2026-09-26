using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Kur;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Finance;

namespace RentACar.IntegrationTests;

/// <summary>
/// Pano/liste "Tahsil Et" hızlı-tahsilat (PR: dashboard-tahsil-et). Beklenen değerler ELLE kurulmuş
/// senaryodan (bağımsız oracle) — servis kodundan değil. Kritik kapsam: deterministik TahsilatAnahtar'ın
/// ZAMANSAL ÇAKIŞMA regresyonu (aylık kirada Bakiye aynı değere döner; işlem-sayacı bileşeni anahtarı
/// ayrıştırmalı) + çift-submit idempotency + aşan tutar + çok-döviz (K2) + yetki.
/// </summary>
[Collection("postgres")]
public sealed class DashboardTahsilatTests(PostgresFixture fx)
{
    private static async Task<Guid> SeedCustomerAsync(IServiceScope scope, string name)
    {
        var customers = scope.ServiceProvider.GetRequiredService<CustomerService>();
        return await customers.CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = name, Soyad = "Test" });
    }

    private static IDbContextFactory<AppDbContext> Factory(IServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

    private static async Task<Guid> SeedRentalAsync(IServiceScope scope, Guid account,
        decimal grandTotal, decimal collection, string? currency = null)
    {
        var rentalId = Guid.NewGuid();
        await using var db = await Factory(scope).CreateDbContextAsync();
        db.Rentals.Add(new RentalContract
        {
            Id = rentalId, SozlesmeNo = $"KS-DT{rentalId.ToString()[..6]}", MusteriId = account, VehicleId = Guid.NewGuid(),
            BasTar = DateTimeOffset.UtcNow, BitTar = DateTimeOffset.UtcNow.AddDays(1),
            GenelToplam = grandTotal, Tahsilat = collection, Bakiye = grandTotal - collection, Doviz = currency
        });
        await db.SaveChangesAsync();
        return rentalId;
    }

    // ---- 1. Mutlu yol: 300 toplam − 100 tahsil edilmiş → 200 tahsil = Bakiye 0, defter tam 2 satır ----
    [Fact]
    public async Task HizliTahsilat_bakiyeyi_kapatir_ve_dengeli_defter_yazar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var account = await SeedCustomerAsync(scope, "MutluYol");
        var rentalId = await SeedRentalAsync(scope, account, grandTotal: 300m, collection: 100m);

        // Panonun üreteceği anahtar: bakiye=200, işlem sayısı=0 (hiç kasa işlemi yok).
        var key = CollectionKey.Generate(rentalId, 200m, 0);
        var txId = await cash.CollectAsync(new CashInput
        {
            CariId = account, RentalId = rentalId, Tutar = 200m,
            Hesap = LedgerAccountType.Kasa, IslemAnahtari = key
        });

        await using var db = await Factory(scope).CreateDbContextAsync();
        var r = await db.Rentals.AsNoTracking().FirstAsync(x => x.Id == rentalId);
        Assert.Equal(300m, r.Tahsilat);   // 100 + 200 (elle)
        Assert.Equal(0m, r.Bakiye);       // 300 − 300 (elle)

        // Bu işlemin defteri TAM 2 satır: Borç Kasa 200 / Alacak Cari 200 (base).
        var rows = await db.AccountLedgerEntries.AsNoTracking().Where(e => e.SourceId == txId).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(200m, rows.Single(e => e.AccountType == LedgerAccountType.Kasa && e.Direction == LedgerDirection.Debit).Amount.AmountInBase);
        Assert.Equal(200m, rows.Single(e => e.AccountType == LedgerAccountType.Cari && e.Direction == LedgerDirection.Credit).Amount.AmountInBase);
    }

    // ---- 2. Çift-submit: aynı deterministik anahtar 2× → TEK işlem (kasa 200, 400 değil) ----
    [Fact]
    public async Task CiftSubmit_ayni_anahtar_ikinci_istegi_idempotent_reddeder()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var account = await SeedCustomerAsync(scope, "CiftSubmit");
        var rentalId = await SeedRentalAsync(scope, account, grandTotal: 200m, collection: 0m);

        var key = CollectionKey.Generate(rentalId, 200m, 0);
        CashInput Input() => new()
        {
            CariId = account, RentalId = rentalId, Tutar = 200m,
            Hesap = LedgerAccountType.Kasa, IslemAnahtari = key
        };

        await cash.CollectAsync(Input());
        // İkinci gönderim: DB kısmi-unique-index → ValidationException (zarif; endpoint ?hata= redirect'ler).
        await Assert.ThrowsAsync<DuplicateOperationException>(() => cash.CollectAsync(Input()));

        await using var db = await Factory(scope).CreateDbContextAsync();
        Assert.Equal(1, await db.CashTransactions.AsNoTracking().CountAsync(t => t.RentalId == rentalId));
        var r = await db.Rentals.AsNoTracking().FirstAsync(x => x.Id == rentalId);
        Assert.Equal(200m, r.Tahsilat); // 200, 400 DEĞİL
        Assert.Equal(0m, r.Bakiye);
    }

    // ---- 3. ZAMANSAL ÇAKIŞMA regresyonu: yeni tahakkukla Bakiye AYNI değere döner → sayaç
    //         bileşeni anahtarı ayrıştırır, MEŞRU ikinci tahsilat GEÇER ----
    [Fact]
    public async Task AylikKira_bakiye_ayni_degere_donunce_yeni_anahtar_uretilir_ve_tahsilat_gecer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var account = await SeedCustomerAsync(scope, "AylikKira");
        var rentalId = await SeedRentalAsync(scope, account, grandTotal: 5000m, collection: 0m);

        // Ay 1: bakiye 5000, işlem sayısı 0 → K1 ile tahsil.
        var k1 = CollectionKey.Generate(rentalId, 5000m, 0);
        await cash.CollectAsync(new CashInput
        {
            CariId = account, RentalId = rentalId, Tutar = 5000m,
            Hesap = LedgerAccountType.Kasa, IslemAnahtari = k1
        });

        // Ay 2 tahakkuku (dönem faturası benzeri): GenelToplam 10000 → Bakiye YİNE 5000.
        await using (var db = await Factory(scope).CreateDbContextAsync())
        {
            await db.Rentals.Where(x => x.Id == rentalId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.GenelToplam, 10000m).SetProperty(x => x.Bakiye, 5000m));
        }

        // Pano yeniden render: bakiye 5000 AMA işlem sayısı artık 1 → K2 ≠ K1 (eski tasarım burada kilitleniyordu).
        var numbers = await cash.GetRentalTransactionCountsAsync([rentalId]);
        Assert.Equal(1, numbers[rentalId]);
        var k2 = CollectionKey.Generate(rentalId, 5000m, numbers[rentalId]);
        Assert.NotEqual(k1, k2);

        await cash.CollectAsync(new CashInput // MEŞRU ikinci ay tahsilatı — GEÇMELİ
        {
            CariId = account, RentalId = rentalId, Tutar = 5000m,
            Hesap = LedgerAccountType.Kasa, IslemAnahtari = k2
        });

        // K2'nin çift-submit'i hâlâ bloklanır.
        await Assert.ThrowsAsync<DuplicateOperationException>(() => cash.CollectAsync(new CashInput
        {
            CariId = account, RentalId = rentalId, Tutar = 5000m,
            Hesap = LedgerAccountType.Kasa, IslemAnahtari = k2
        }));

        await using (var db = await Factory(scope).CreateDbContextAsync())
        {
            var r = await db.Rentals.AsNoTracking().FirstAsync(x => x.Id == rentalId);
            Assert.Equal(10000m, r.Tahsilat); // 5000 + 5000 (elle)
            Assert.Equal(0m, r.Bakiye);
        }
    }

    // ---- 4. Aşan tutar: Bakiye 200 iken 250 → KABUL, Bakiye −50 (müşteri lehine) ----
    [Fact]
    public async Task AsanTutar_kabul_edilir_bakiye_musteri_lehine_eksiye_gecer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var account = await SeedCustomerAsync(scope, "AsanTutar");
        var rentalId = await SeedRentalAsync(scope, account, grandTotal: 200m, collection: 0m);

        await cash.CollectAsync(new CashInput
        {
            CariId = account, RentalId = rentalId, Tutar = 250m,
            Hesap = LedgerAccountType.Kasa, IslemAnahtari = CollectionKey.Generate(rentalId, 200m, 0)
        });

        await using var db = await Factory(scope).CreateDbContextAsync();
        var r = await db.Rentals.AsNoTracking().FirstAsync(x => x.Id == rentalId);
        Assert.Equal(250m, r.Tahsilat);
        Assert.Equal(-50m, r.Bakiye);              // 200 − 250 (elle; ön-ödeme/alacak)
        Assert.Equal(-250m, await cash.GetAccountBalanceAsync(account)); // cari 250 alacaklı (elle)
    }

    // ---- 5. Çok-döviz: EUR kira + sabit kur 40; kur BOŞ → KurCozucu; base 4000. TRY denemesi → red (K2) ----
    [Fact]
    public async Task EuroKira_kur_bos_otomatik_cozulur_try_denemesi_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        await sp.GetRequiredService<FixedExchangeRateService>().UpsertAsync(new SabitKurInput { Kod = "EUR", Kur = 40m, Aktif = true });
        var account = await SeedCustomerAsync(scope, "EuroKira");
        var rentalId = await SeedRentalAsync(scope, account, grandTotal: 100m, collection: 0m, currency: "EURO");

        // Panonun göndereceği biçim: doviz = NormalizeKod("EURO") = EUR, kur yok.
        var txId = await cash.CollectAsync(new CashInput
        {
            CariId = account, RentalId = rentalId, Tutar = 100m, Doviz = ExchangeRateService.NormalizeCode("EURO"),
            Hesap = LedgerAccountType.Banka, IslemAnahtari = CollectionKey.Generate(rentalId, 100m, 0)
        });

        await using var db = await Factory(scope).CreateDbContextAsync();
        var bank = await db.AccountLedgerEntries.AsNoTracking()
            .SingleAsync(e => e.SourceId == txId && e.AccountType == LedgerAccountType.Banka);
        Assert.Equal(4000m, bank.Amount.AmountInBase); // 100 EUR × 40 (elle)
        var r = await db.Rentals.AsNoTracking().FirstAsync(x => x.Id == rentalId);
        Assert.Equal(100m, r.Tahsilat); // kira dövizinde (K2 semantiği)
        Assert.Equal(0m, r.Bakiye);

        // Yanlış döviz (TRY) EUR kiraya → repo K2 çiti reddeder.
        await Assert.ThrowsAsync<ValidationException>(() => cash.CollectAsync(new CashInput
        {
            CariId = account, RentalId = rentalId, Tutar = 50m, Doviz = "TRY",
            Hesap = LedgerAccountType.Kasa, IslemAnahtari = Guid.NewGuid()
        }));
    }

    // ---- 6. Yetki: Operator tahsilat YAPAMAZ (FinanceWrite guard) ----
    [Fact]
    public async Task Operator_hizli_tahsilat_yapamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid account, rentalId;
        using (var admin = host.ScopeFor(tenant))
        {
            account = await SeedCustomerAsync(admin, "YetkiTest");
            rentalId = await SeedRentalAsync(admin, account, grandTotal: 100m, collection: 0m);
        }

        using var op = host.ScopeFor(tenant, role: UserRole.Operator);
        var cash = op.ServiceProvider.GetRequiredService<CashService>();
        await Assert.ThrowsAsync<NoPermissionException>(() => cash.CollectAsync(new CashInput
        {
            CariId = account, RentalId = rentalId, Tutar = 100m,
            Hesap = LedgerAccountType.Kasa, IslemAnahtari = CollectionKey.Generate(rentalId, 100m, 0)
        }));
    }

    // ---- 7. Anahtar determinizmi: aynı girdi → aynı Guid; bakiye YA DA sayaç farkı → farklı ----
    [Fact]
    public void TahsilatAnahtar_deterministik_ve_bilesene_duyarli()
    {
        var id = Guid.NewGuid();
        Assert.Equal(CollectionKey.Generate(id, 1250.50m, 3), CollectionKey.Generate(id, 1250.50m, 3));
        Assert.NotEqual(CollectionKey.Generate(id, 1250.50m, 3), CollectionKey.Generate(id, 1250.51m, 3));
        Assert.NotEqual(CollectionKey.Generate(id, 1250.50m, 3), CollectionKey.Generate(id, 1250.50m, 4));
        Assert.NotEqual(CollectionKey.Generate(id, 1250.50m, 3), CollectionKey.Generate(Guid.NewGuid(), 1250.50m, 3));
    }
}
