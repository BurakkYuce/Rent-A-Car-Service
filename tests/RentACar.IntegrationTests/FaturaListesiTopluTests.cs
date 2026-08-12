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
    private static DateTimeOffset Gun(int fark)
    {
        var t = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(fark), DateTimeKind.Utc), TimeSpan.Zero);
        return t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));
    }

    private static int _vergiSayac = 1000000000;

    /// <summary>Vergi No tenant içinde BENZERSİZ (blind-index kısıtı) — her cariye farklı üretilir.</summary>
    private static Task<Guid> CariAsync(IServiceScope s, string ad, string? ozelKod = null, string? vergiNo = null)
        => s.ServiceProvider.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput
            {
                Tip = CariType.Kurumsal, Unvan = ad, OzelKod = ozelKod,
                VergiNo = vergiNo ?? Interlocked.Increment(ref _vergiSayac).ToString(),
                VergiDairesi = "Kadıköy"
            });

    /// <summary>3 günlük, günlük 100 → 300 brüt kira kurar.</summary>
    private static async Task<Guid> KiraAsync(IServiceScope s, Guid cari, string plaka, string? ofis = null)
    {
        var veh = await s.ServiceProvider.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plaka, Durum = VehicleStatus.Musait });
        return await s.ServiceProvider.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = cari, VehicleId = veh,
            BasTar = Gun(-5), BitTar = Gun(-2), GunlukUcret = 100m, CikisOfisi = ofis
        });
    }

    private static async Task<(int Adet, decimal Borc, decimal Alacak)> DefterAsync(TestHost host, Guid tenant)
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
        var cari = await CariAsync(scope, "Toplu A.Ş.");
        var k1 = await KiraAsync(scope, cari, "34 TF 01");
        var k2 = await KiraAsync(scope, cari, "34 TF 02");
        var k3 = await KiraAsync(scope, cari, "34 TF 03");

        var sonuc = await invoices.BatchCreateFromRentalsAsync([k1, k2, k3]);

        Assert.Equal(3, sonuc.Kesilen.Count);
        Assert.Empty(sonuc.Atlananlar);

        // Elle: 3 fatura × 300 brüt = 900. Her fatura Borç Cari 300 / Alacak Gelir 250 + Kdv 50.
        var (adet, borc, alacak) = await DefterAsync(host, tenant);
        Assert.Equal(9, adet);              // 3 fatura × 3 satır
        Assert.Equal(900m, borc);
        Assert.Equal(900m, alacak);         // denge
        var liste = await invoices.SearchAsync();
        Assert.Equal(3, liste.Count);
        Assert.All(liste, x => Assert.Equal(250m, x.Fatura.NetTutar));
        Assert.All(liste, x => Assert.Equal(50m, x.Fatura.KdvTutar));
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
        var cari = await CariAsync(scope, "Kismi");
        var k1 = await KiraAsync(scope, cari, "34 KB 01");
        var iptal = await KiraAsync(scope, cari, "34 KB 02");
        var k3 = await KiraAsync(scope, cari, "34 KB 03");
        await rentals.CancelAsync(iptal);   // iptal kiraya fatura kesilemez

        var sonuc = await invoices.BatchCreateFromRentalsAsync([k1, iptal, k3]);

        Assert.Equal(2, sonuc.Kesilen.Count);
        var atlanan = Assert.Single(sonuc.Atlananlar);
        Assert.Contains("İptal", atlanan);
        // Atlanan mesajı SÖZLEŞME NO taşır — çok seçimli kesimde hangisi olduğu ayırt edilebilmeli.
        Assert.Contains("KS-", atlanan);

        var (_, borc, alacak) = await DefterAsync(host, tenant);
        Assert.Equal(600m, borc);   // elle: 2 × 300
        Assert.Equal(borc, alacak);
    }

    [Fact]
    public async Task Ayni_secim_ikinci_kez_gonderilirse_yeni_belge_URETMEZ()
    {
        // Çift-submit: tekil yol "zaten tam faturalanmış" diye reddeder → atlananlara düşer.
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var cari = await CariAsync(scope, "CiftSubmit");
        var k1 = await KiraAsync(scope, cari, "34 CS 01");

        var ilk = await invoices.BatchCreateFromRentalsAsync([k1]);
        Assert.Single(ilk.Kesilen);
        var defterSonrasi = await DefterAsync(host, tenant);

        var ikinci = await invoices.BatchCreateFromRentalsAsync([k1]);
        Assert.Empty(ikinci.Kesilen);
        Assert.Single(ikinci.Atlananlar);
        Assert.Equal(defterSonrasi, await DefterAsync(host, tenant));   // defter DEĞİŞMEDİ
        Assert.Single(await invoices.SearchAsync());
    }

    [Fact]
    public async Task Bos_secim_ve_sinir_asimi_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();

        await Assert.ThrowsAsync<ValidationException>(() => invoices.BatchCreateFromRentalsAsync([]));
        var cok = Enumerable.Range(0, InvoiceService.TopluMaxSecim + 1).Select(_ => Guid.NewGuid()).ToList();
        await Assert.ThrowsAsync<ValidationException>(() => invoices.BatchCreateFromRentalsAsync(cok));
    }

    [Fact]
    public async Task Uydurma_kira_kimligi_atlanir_digerleri_kesilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var cari = await CariAsync(scope, "Uydurma");
        var k1 = await KiraAsync(scope, cari, "34 UY 01");

        var sonuc = await invoices.BatchCreateFromRentalsAsync([k1, Guid.NewGuid()]);
        Assert.Single(sonuc.Kesilen);
        Assert.Contains(sonuc.Atlananlar, a => a.Contains("bulunamadı"));
    }

    [Fact]
    public async Task Toplu_kesim_yetki_ve_tenant_izolasyonu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        Guid kira;
        using (var s1 = host.ScopeFor(t1))
        {
            var cari = await CariAsync(s1, "T1");
            kira = await KiraAsync(s1, cari, "34 TN 01");
        }

        // Başka tenant'ın kira kimliği: bulunamaz → atlanır, fatura YAZILMAZ.
        using (var s2 = host.ScopeFor(t2))
        {
            var sonuc = await s2.ServiceProvider.GetRequiredService<InvoiceService>()
                .BatchCreateFromRentalsAsync([kira]);
            Assert.Empty(sonuc.Kesilen);
            Assert.Single(sonuc.Atlananlar);
            Assert.Empty(await s2.ServiceProvider.GetRequiredService<InvoiceService>().SearchAsync());
        }

        // Operatör toplu fatura kesemez (FinanceWrite yok).
        using var op = host.ScopeFor(t1, role: UserRole.Operator);
        await Assert.ThrowsAsync<ValidationException>(() => op.ServiceProvider
            .GetRequiredService<InvoiceService>().BatchCreateFromRentalsAsync([kira]));
    }

    // ---------------------------------------------------------------- Süzgeçler

    [Fact]
    public async Task Fatura_listesi_suzgecleri_daraltir_boşken_daraltmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var a = await CariAsync(scope, "Alfa A.Ş.", "OZL-A");
        var b = await CariAsync(scope, "Beta Ltd.", "OZL-B");
        var k1 = await KiraAsync(scope, a, "34 FL 01", ofis: "Kadıköy");
        var k2 = await KiraAsync(scope, b, "34 FL 02", ofis: "Beşiktaş");
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
        Assert.Equal(2, (await invoices.SearchAsync(new InvoiceFilter { Bas = Gun(-1) })).Count);
        Assert.Empty(await invoices.SearchAsync(new InvoiceFilter { Bas = Gun(1) }));
        // Boş süzgeç daraltmaz.
        Assert.Equal(2, (await invoices.SearchAsync(new InvoiceFilter { Ara = "", Ofis = "", Doviz = "" })).Count);
    }

    [Fact]
    public async Task Iptal_suzgeci_ve_kunye_kolonlari()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var cari = await CariAsync(scope, "Künye A.Ş.", "OZL-K", vergiNo: "1234567890");
        var kira = await KiraAsync(scope, cari, "34 KN 01", ofis: "Merkez");
        await invoices.BatchCreateFromRentalsAsync([kira]);

        var satir = Assert.Single(await invoices.SearchAsync());
        Assert.Equal("Künye A.Ş.", satir.CariAd);
        Assert.Equal("OZL-K", satir.CariOzelKod);
        Assert.Equal("Kadıköy", satir.VergiDairesi);
        Assert.Equal("1234567890", satir.VergiNo);
        Assert.Equal("34KN01", satir.Plaka?.Replace(" ", ""));
        Assert.StartsWith("KS-", satir.SozlesmeNo);
        Assert.Equal("Merkez", satir.Ofis);

        // İptal hariç → 1; yalnız iptal → 0 (henüz iptal edilmiş fatura yok).
        Assert.Single(await invoices.SearchAsync(new InvoiceFilter { Iptal = false }));
        Assert.Empty(await invoices.SearchAsync(new InvoiceFilter { Iptal = true }));
    }

    [Fact]
    public async Task Fatura_no_araligi_suzgeci()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var cari = await CariAsync(scope, "NoAralik");
        var kiralar = new List<Guid>();
        for (var i = 1; i <= 3; i++) kiralar.Add(await KiraAsync(scope, cari, $"34 NA 0{i}"));
        await invoices.BatchCreateFromRentalsAsync(kiralar);

        var hepsi = (await invoices.SearchAsync()).OrderBy(x => x.Fatura.No).ToList();
        Assert.Equal(3, hepsi.Count);
        // Ortadaki no'dan itibaren → 2 kayıt (no'lar sabit genişlikte, metin karşılaştırması güvenli).
        var ortanca = hepsi[1].Fatura.No;
        Assert.Equal(2, (await invoices.SearchAsync(new InvoiceFilter { NoMin = ortanca })).Count);
        Assert.Equal(2, (await invoices.SearchAsync(new InvoiceFilter { NoMax = ortanca })).Count);
    }
}
