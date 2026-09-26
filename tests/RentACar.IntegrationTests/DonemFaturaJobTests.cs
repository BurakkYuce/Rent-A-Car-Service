using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.FaturaDonemleri;
using RentACar.Application.Finance;
using RentACar.Application.TenantSettings;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ4-4.2-B4 — Dönemsel fatura job üreticisi. BAĞIMSIZ ORACLE (elle): 60 gün önce başlamış
/// 90 günlük kira (100/gün, Tutar 9000); vadesi GEÇMİŞ ilk 2 dönem job'ca kesilir (3. dönem
/// gelecekte — atlanır); tutarlar MANUEL yolla ÖZDEŞ (aynı saf matematik). Ayar/bayrak kapıları:
/// tenant DonemselFaturalamaJob kapalı → no-op; kira DonemselFaturalama=false → atlanır. Aynı gün
/// İKİNCİ koşu → no-op (Kesildi idempotency). Oto-tahsilat ayarı: deterministik anahtarlı tahsilat
/// + kira Tahsilat/Bakiye işler; ikinci koşu çift yazmaz. Kilitli muhasebe dönemi → tenant atlanır
/// (log satırı) — kesim yok.
/// </summary>
[Collection("postgres")]
public sealed class DonemFaturaJobTests(PostgresFixture fx)
{
    private static async Task<(Guid kira, Guid cari)> JobluKiraAsync(IServiceProvider sp, string plaka, bool jobBayragi = true)
    {
        // -65 gün: iki ay-çıpalı dönemin (en kötü 31+31=62 gün) kesin geçmişte bitmesi için tampon.
        var bas = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-65);
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Job", Soyad = "M" });
        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = m, VehicleId = v, BasTar = bas, BitTar = bas.AddDays(90), GunlukUcret = 100m,
            DonemselFaturalama = jobBayragi
        });
        return (id, m);
    }

    private static async Task<DonemFaturaUretici.Sonuc> KosAsync(IServiceProvider sp, Guid tenantId)
    {
        // Job'ın doğrudan-context yolu (VadeBildirimUretici test emsali — BildirimTests.UretAsync).
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await DonemFaturaUretici.RunAsync(db, tenantId, DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Vadesi_gecen_donemler_kesilir_ikinci_kosu_noop()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<ITenantSettingsRepository>().UpsertAsync(s => s.DonemselFaturalamaJob = true);
        var (kira, _) = await JobluKiraAsync(sp, "34 JB 01");

        // 60 gün geçmiş → D1 + D2 vadesi geçmiş (30'ar gün civarı), D3 gelecekte.
        var s1 = await KosAsync(sp, tenant);
        Assert.Equal(2, s1.Kesilen);

        // Tutarlar manuel yol matematiğiyle özdeş: Σ kesilen = ilk 2 dönem tahakkuku; Σ tüm plan = 9000.
        var donemler = await sp.GetRequiredService<IInvoicePeriodRepository>().ListForRentalAsync(kira);
        Assert.Equal(InvoicePeriodStatus.Kesildi, donemler[0].Durum);
        Assert.Equal(InvoicePeriodStatus.Kesildi, donemler[1].Durum);
        Assert.Equal(InvoicePeriodStatus.Planlandi, donemler[2].Durum);
        var (faturalanan, _) = await sp.GetRequiredService<IInvoiceRepository>().GetDifferenceStateAsync(kira);
        Assert.Equal(donemler[0].KesilenTutar + donemler[1].KesilenTutar, faturalanan);

        // Aynı gün ikinci koşu → no-op (idempotent).
        var s2 = await KosAsync(sp, tenant);
        Assert.Equal(0, s2.Kesilen);
        Assert.Equal(faturalanan, (await sp.GetRequiredService<IInvoiceRepository>().GetDifferenceStateAsync(kira)).FaturalananBrut);
    }

    [Fact]
    public async Task Eszamanli_job_ve_manuel_kesim_celiskili_durum_birakmaz() // adversarial B4-Yüksek-1
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<ITenantSettingsRepository>().UpsertAsync(s => s.DonemselFaturalamaJob = true);
        var (kira, _) = await JobluKiraAsync(sp, "34 JB 09");
        var invoices = sp.GetRequiredService<InvoiceService>();

        // Düzeltme öncesi: job'ın bayat change-tracker'ı KESİLMİŞ dönemi Atlandi'ye yazıyordu
        // (Durum=Atlandi + InvoiceId dolu — çelişkili iz; 8/8 turda üretilmişti). 3 aktör paraleli:
        var t1 = Task.Run(() => KosAsync(sp, tenant));
        var t2 = Task.Run(() => KosAsync(sp, tenant));
        var t3 = Task.Run(async () =>
        { try { await invoices.CreatePeriodInvoiceAsync(kira, 1); } catch (RentACar.Application.Common.ValidationException) { } });
        await Task.WhenAll(t1, t2, t3);

        var donemler = await sp.GetRequiredService<IInvoicePeriodRepository>().ListForRentalAsync(kira);
        // İnvaryantlar: Kesildi satırın InvoiceId'si VAR; Atlandi/Planlandi satırın InvoiceId'si YOK;
        // vadesi geçmiş ilk 2 dönem KESİLDİ (sessiz atlama yok); faturalanan = Σ kesilen.
        Assert.All(donemler, d =>
        {
            if (d.Durum == InvoicePeriodStatus.Kesildi) Assert.NotNull(d.InvoiceId);
            else Assert.Null(d.InvoiceId);
        });
        Assert.Equal(InvoicePeriodStatus.Kesildi, donemler[0].Durum);
        Assert.Equal(InvoicePeriodStatus.Kesildi, donemler[1].Durum);
        var (faturalanan, _) = await sp.GetRequiredService<IInvoiceRepository>().GetDifferenceStateAsync(kira);
        Assert.Equal(donemler.Where(d => d.Durum == InvoicePeriodStatus.Kesildi).Sum(d => d.KesilenTutar ?? 0m), faturalanan);
    }

    [Fact]
    public async Task Ayar_ve_bayrak_kapilari()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;

        // Tenant ayarı KAPALI → job hiç kesmez (kira bayraklı olsa bile).
        await JobluKiraAsync(sp, "34 JB 02");
        Assert.Equal(0, (await KosAsync(sp, tenant)).Kesilen);

        // Ayar açık ama kira bayrağı KAPALI → o kira atlanır.
        await sp.GetRequiredService<ITenantSettingsRepository>().UpsertAsync(s => s.DonemselFaturalamaJob = true);
        await JobluKiraAsync(sp, "34 JB 03", jobBayragi: false);
        var sonuc = await KosAsync(sp, tenant);
        Assert.Equal(2, sonuc.Kesilen); // yalnız bayraklı (JB 02) kirasının 2 geçmiş dönemi
    }

    [Fact]
    public async Task Oto_tahsilat_deterministik_ve_kilitli_donem_atlanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<ITenantSettingsRepository>()
            .UpsertAsync(s => { s.DonemselFaturalamaJob = true; s.DonemselOtomatikTahsilat = true; });
        var (kira, cari) = await JobluKiraAsync(sp, "34 JB 04");

        var s1 = await KosAsync(sp, tenant);
        Assert.Equal(2, s1.Kesilen);
        Assert.Equal(2, s1.Tahsilat);

        // Cari bakiye 0 (fatura borç == tahsilat alacak); kira Tahsilat alanı işledi.
        Assert.Equal(0m, await sp.GetRequiredService<CashService>().GetAccountBalanceAsync(cari));
        var (faturalanan, _) = await sp.GetRequiredService<IInvoiceRepository>().GetDifferenceStateAsync(kira);
        Assert.Equal(faturalanan, (await sp.GetRequiredService<RentalService>().GetAsync(kira))!.Tahsilat);

        // İkinci koşu: kesim yok + tahsilat çift yazılmaz (deterministik anahtar).
        var s2 = await KosAsync(sp, tenant);
        Assert.Equal(0, s2.Kesilen);
        Assert.Equal(0m, await sp.GetRequiredService<CashService>().GetAccountBalanceAsync(cari));

        // KİLİTLİ muhasebe dönemi: yeni tenant, kilit bugünü kapsar → tenant atlanır (log), kesim yok.
        var tenant2 = Guid.NewGuid();
        using var scope2 = host.ScopeFor(tenant2);
        var sp2 = scope2.ServiceProvider;
        await sp2.GetRequiredService<ITenantSettingsRepository>().UpsertAsync(s => s.DonemselFaturalamaJob = true);
        await JobluKiraAsync(sp2, "34 JB 05");
        await sp2.GetRequiredService<RentACar.Application.Periods.PeriodLockService>()
            .LockAsync(DateTimeOffset.UtcNow.AddDays(1));
        var s3 = await KosAsync(sp2, tenant2);
        Assert.Equal(0, s3.Kesilen);
        Assert.Contains(s3.Atlananlar, a => a.Contains("kilitli"));
    }
}
