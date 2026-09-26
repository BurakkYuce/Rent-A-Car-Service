using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-54 — fatura listesi süzgeci + TOPLU FATURALAMA.
///
/// <para><b>Fazın asıl sözleşmesi:</b> toplu kesim para mantığını YENİDEN YAZMAZ — her kira mevcut
/// <c>CreateFromRentalAsync</c> yolundan geçer (aynı guard'lar, aynı KDV/kur zinciri). Spec'in
/// "faturalanabilirlik ölçütü" ve "KDV/kur çözümü" kararlarını sorması bu yüzden gereksizdi: ikisi
/// de tekil yolda zaten verilmiş ve adversarial incelemelerle sertleşmiş durumda.</para>
///
/// <para><b>Parti ATOMİK DEĞİL (bilinçli):</b> her fatura bağımsız bir mali belgedir ve boşluksuz
/// numara alır. Hep-ya-hiç olsaydı seçimdeki tek bozuk sözleşme geçerli faturaları da geri alırdı.
/// FAZ-30 (dönem faturası elle tetikleme) aynı gerekçeyle satır-bazlı çalışır.</para>
///
/// <para><b>Bağımsız oracle:</b> 3 gün × 100 = 300 brüt; %20 KDV ile net 250 / KDV 50.</para>
/// </summary>
[Collection("postgres")]
public sealed class FaturaListesiTopluTests(PostgresFixture fx)
{
    private static DateTimeOffset Day(int difference)
    {
        var t = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(difference), DateTimeKind.Utc), TimeSpan.Zero);
        return t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));
    }

    private static int _taxCounter = 1000000000;

    /// <summary>Vergi No tenant içinde BENZERSİZ (blind-index kısıtı) — her cariye farklı üretilir.</summary>
    private static Task<Guid> CustomerAsync(IServiceScope s, string name, string? customCode = null, string? taxNo = null)
        => s.ServiceProvider.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput
            {
                Tip = CustomerType.Kurumsal, Unvan = name, OzelKod = customCode,
                VergiNo = taxNo ?? Interlocked.Increment(ref _taxCounter).ToString(),
                VergiDairesi = "Kadıköy"
            });

    /// <summary>3 günlük, günlük 100 → 300 brüt kira kurar.</summary>
    private static async Task<Guid> RentalAsync(IServiceScope s, Guid account, string plate, string? office = null)
    {
        var veh = await s.ServiceProvider.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plate, Durum = VehicleStatus.Musait });
        return await s.ServiceProvider.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = account, VehicleId = veh,
            BasTar = Day(-5), BitTar = Day(-2), GunlukUcret = 100m, CikisOfisi = office
        });
    }

    private static async Task<(int Adet, decimal Borc, decimal Alacak)> LedgerAsync(TestHost host, Guid tenant)
    {
        using var scope = host.ScopeFor(tenant);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.SourceType == "Fatura")
            .Select(e => new { e.Direction, A = e.Amount.Amount, R = e.Amount.Rate }).ToListAsync();
        return (rows.Count,
            rows.Where(x => x.Direction == LedgerDirection.Debit).Sum(x => x.A * x.R),
            rows.Where(x => x.Direction == LedgerDirection.Credit).Sum(x => x.A * x.R));
    }

    // ---------------------------------------------------------------- Toplu faturalama

    [Fact]
    public async Task Toplu_kesim_her_kira_icin_dengeli_fatura_yazar()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var account = await CustomerAsync(scope, "Toplu A.Ş.");
        var k1 = await RentalAsync(scope, account, "34 TF 01");
        var k2 = await RentalAsync(scope, account, "34 TF 02");
        var k3 = await RentalAsync(scope, account, "34 TF 03");

        var result = await invoices.BatchCreateFromRentalsAsync([k1, k2, k3]);

        Assert.Equal(3, result.Kesilen.Count);
        Assert.Empty(result.Atlananlar);

        // Elle: 3 fatura × 300 brüt = 900. Her fatura Borç Cari 300 / Alacak Gelir 250 + Kdv 50.
        var (count, debit, credit) = await LedgerAsync(host, tenant);
        Assert.Equal(9, count);              // 3 fatura × 3 satır
        Assert.Equal(900m, debit);
        Assert.Equal(900m, credit);         // denge
        var list = await invoices.SearchAsync();
        Assert.Equal(3, list.Count);
        Assert.All(list, x => Assert.Equal(250m, x.Fatura.NetTutar));
        Assert.All(list, x => Assert.Equal(50m, x.Fatura.KdvTutar));
    }

    [Fact]
    public async Task Bir_kira_bozuksa_digerleri_KESILIR_ve_atlanan_raporlanir()
    {
        // ATOMİK OLMAMA kararının kanıtı: hep-ya-hiç olsaydı 2 geçerli fatura da geri alınırdı.
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var rentals = scope.ServiceProvider.GetRequiredService<RentalService>();
        var account = await CustomerAsync(scope, "Kismi");
        var k1 = await RentalAsync(scope, account, "34 KB 01");
        var cancel = await RentalAsync(scope, account, "34 KB 02");
        var k3 = await RentalAsync(scope, account, "34 KB 03");
        await rentals.CancelAsync(cancel);   // iptal kiraya fatura kesilemez

        var result = await invoices.BatchCreateFromRentalsAsync([k1, cancel, k3]);

        Assert.Equal(2, result.Kesilen.Count);
        var skipped = Assert.Single(result.Atlananlar);
        Assert.Contains("İptal", skipped);
        // Atlanan mesajı SÖZLEŞME NO taşır — çok seçimli kesimde hangisi olduğu ayırt edilebilmeli.
        Assert.Matches(@"\d{13,}", skipped);   // atlanan kiranın numarası mesajda geçmeli

        var (_, debit, credit) = await LedgerAsync(host, tenant);
        Assert.Equal(600m, debit);   // elle: 2 × 300
        Assert.Equal(debit, credit);
    }

    [Fact]
    public async Task Ayni_secim_ikinci_kez_gonderilirse_yeni_belge_URETMEZ()
    {
        // Çift-submit: tekil yol "zaten tam faturalanmış" diye reddeder → atlananlara düşer.
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var account = await CustomerAsync(scope, "CiftSubmit");
        var k1 = await RentalAsync(scope, account, "34 CS 01");

        var first = await invoices.BatchCreateFromRentalsAsync([k1]);
        Assert.Single(first.Kesilen);
        var ledgerAfter = await LedgerAsync(host, tenant);

        var second = await invoices.BatchCreateFromRentalsAsync([k1]);
        Assert.Empty(second.Kesilen);
        Assert.Single(second.Atlananlar);
        Assert.Equal(ledgerAfter, await LedgerAsync(host, tenant));   // defter DEĞİŞMEDİ
        Assert.Single(await invoices.SearchAsync());
    }

    [Fact]
    public async Task Bos_secim_ve_sinir_asimi_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();

        await Assert.ThrowsAsync<ValidationException>(() => invoices.BatchCreateFromRentalsAsync([]));
        var cok = Enumerable.Range(0, InvoiceService.MaxBulkSelection + 1).Select(_ => Guid.NewGuid()).ToList();
        await Assert.ThrowsAsync<ValidationException>(() => invoices.BatchCreateFromRentalsAsync(cok));
    }

    [Fact]
    public async Task Uydurma_kira_kimligi_atlanir_digerleri_kesilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var account = await CustomerAsync(scope, "Uydurma");
        var k1 = await RentalAsync(scope, account, "34 UY 01");

        var result = await invoices.BatchCreateFromRentalsAsync([k1, Guid.NewGuid()]);
        Assert.Single(result.Kesilen);
        Assert.Contains(result.Atlananlar, a => a.Contains("bulunamadı"));
    }

    [Fact]
    public async Task Toplu_kesim_yetki_ve_tenant_izolasyonu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        Guid rental;
        using (var s1 = host.ScopeFor(t1))
        {
            var account = await CustomerAsync(s1, "T1");
            rental = await RentalAsync(s1, account, "34 TN 01");
        }

        // Başka tenant'ın kira kimliği: bulunamaz → atlanır, fatura YAZILMAZ.
        using (var s2 = host.ScopeFor(t2))
        {
            var result = await s2.ServiceProvider.GetRequiredService<InvoiceService>()
                .BatchCreateFromRentalsAsync([rental]);
            Assert.Empty(result.Kesilen);
            Assert.Single(result.Atlananlar);
            Assert.Empty(await s2.ServiceProvider.GetRequiredService<InvoiceService>().SearchAsync());
        }

        // Operatör toplu fatura kesemez (FinanceWrite yok).
        using var op = host.ScopeFor(t1, role: UserRole.Operator);
        await Assert.ThrowsAsync<NoPermissionException>(() => op.ServiceProvider
            .GetRequiredService<InvoiceService>().BatchCreateFromRentalsAsync([rental]));
    }

    // ---------------------------------------------------------------- Süzgeçler

    [Fact]
    public async Task Fatura_listesi_suzgecleri_daraltir_boşken_daraltmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var a = await CustomerAsync(scope, "Alfa A.Ş.", "OZL-A");
        var b = await CustomerAsync(scope, "Beta Ltd.", "OZL-B");
        var k1 = await RentalAsync(scope, a, "34 FL 01", office: "Kadıköy");
        var k2 = await RentalAsync(scope, b, "34 FL 02", office: "Beşiktaş");
        await invoices.BatchCreateFromRentalsAsync([k1, k2]);

        // Süzgeçsiz: 2.
        Assert.Equal(2, (await invoices.SearchAsync()).Count);
        // Cari.
        Assert.Single(await invoices.SearchAsync(new InvoiceFilter { CariId = a }));
        // Metin araması: cari adı, özel kod, plaka, sözleşme no.
        Assert.Single(await invoices.SearchAsync(new InvoiceFilter { Ara = "Beta" }));
        Assert.Single(await invoices.SearchAsync(new InvoiceFilter { Ara = "OZL-A" }));
        Assert.Single(await invoices.SearchAsync(new InvoiceFilter { Ara = "34 FL 02" }));
        // Ofis (kira üzerinden).
        Assert.Single(await invoices.SearchAsync(new InvoiceFilter { Ofis = "Kadıköy" }));
        // Döviz.
        Assert.Equal(2, (await invoices.SearchAsync(new InvoiceFilter { Doviz = "TRY" })).Count);
        Assert.Empty(await invoices.SearchAsync(new InvoiceFilter { Doviz = "EUR" }));
        // Tarih penceresi (bugün dahil).
        Assert.Equal(2, (await invoices.SearchAsync(new InvoiceFilter { Bas = Day(-1) })).Count);
        Assert.Empty(await invoices.SearchAsync(new InvoiceFilter { Bas = Day(1) }));
        // Boş süzgeç daraltmaz.
        Assert.Equal(2, (await invoices.SearchAsync(new InvoiceFilter { Ara = "", Ofis = "", Doviz = "" })).Count);
    }

    [Fact]
    public async Task Iptal_suzgeci_ve_kunye_kolonlari()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var account = await CustomerAsync(scope, "Künye A.Ş.", "OZL-K", taxNo: "1234567890");
        var rental = await RentalAsync(scope, account, "34 KN 01", office: "Merkez");
        await invoices.BatchCreateFromRentalsAsync([rental]);

        var row = Assert.Single(await invoices.SearchAsync());
        Assert.Equal("Künye A.Ş.", row.CariAd);
        Assert.Equal("OZL-K", row.CariOzelKod);
        Assert.Equal("Kadıköy", row.VergiDairesi);
        Assert.Equal("1234567890", row.VergiNo);
        Assert.Equal("34KN01", row.Plaka?.Replace(" ", ""));
        Assert.Matches(@"^\d{13,}$", row.SozlesmeNo);   // yeni desen: tamamı rakam
        Assert.Equal("Merkez", row.Ofis);

        // İptal hariç → 1; yalnız iptal → 0 (henüz iptal edilmiş fatura yok).
        Assert.Single(await invoices.SearchAsync(new InvoiceFilter { Iptal = false }));
        Assert.Empty(await invoices.SearchAsync(new InvoiceFilter { Iptal = true }));
    }

    /// <summary>
    /// Eski <c>Fatura_no_araligi_suzgeci</c>'nin yerini alır.
    ///
    /// <para>NoMin/NoMax aralık süzgeci KALDIRILDI: metin karşılaştırmasıydı ve "no'lar sabit
    /// genişlikte" varsayımına dayanıyordu. Fatura no'su artık GİB formatında
    /// (RNT2026000000001) ve eski FT-000042'lerle bir arada yaşıyor — karışık kümede "aralık"
    /// tanımsızdır, süzgeç sessizce yanlış sonuç verirdi. Yerini TAM/parça no araması
    /// (<c>Ara</c>) ve tarih aralığı aldı; bu test onların çalıştığını kilitler.</para>
    /// </summary>
    [Fact]
    public async Task Fatura_no_ARAMASI_ve_tarih_araligi_calisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var account = await CustomerAsync(scope, "NoAralik");
        var rentals = new List<Guid>();
        for (var i = 1; i <= 3; i++) rentals.Add(await RentalAsync(scope, account, $"34 NA 0{i}"));
        await invoices.BatchCreateFromRentalsAsync(rentals);

        var all = await invoices.SearchAsync();
        Assert.Equal(3, all.Count);

        // Tam numarayla arama → yalnız o fatura.
        var median = all.Select(x => x.Fatura.No).Order(StringComparer.Ordinal).ElementAt(1);
        Assert.Equal(median, Assert.Single(await invoices.SearchAsync(new InvoiceFilter { Ara = median })).Fatura.No);

        // Tarih aralığı: hepsi bugün kesildi → bugünü kapsayan aralık 3, dünle biten aralık 0.
        var today = DateTimeOffset.UtcNow;
        Assert.Equal(3, (await invoices.SearchAsync(new InvoiceFilter { Bas = today.AddDays(-1) })).Count);
        Assert.Empty(await invoices.SearchAsync(new InvoiceFilter { Bit = today.AddDays(-1) }));
    }
}
