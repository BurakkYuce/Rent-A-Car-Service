using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Branches;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.GelenEFaturalar;
using RentACar.Application.Locations;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-53 — fatura dönem raporu derinliği ("faturalanmamış kira" sekmesi) + KDV GENİŞ format
/// (satır=belge, sütun=oran) + ALIŞ (gelen e-Fatura) KDV'si.
///
/// <para><b>Bağımsız oracle:</b> her senaryodaki beklenen sayı/tutar elle kurulan kurgudan gelir
/// (3 kira → 1'i faturasız; %20 100/20 + %10 50/5 gibi sabitler), rapor kodundan türetilmez.</para>
///
/// <para><b>Spec'ten SAPMA (bilinçli, kayıt altında):</b> FAZ-53 spec'i 4. maddede
/// <c>GetKdvListesiAsync</c>'e <c>dahilAlis</c> parametresi eklemeyi öneriyordu. Uygulanmadı:
/// mevcut pivot Net/KDV/Brüt kolonlarına alış KDV'si eklenirse HESAPLANAN (borç) ile İNDİRİLECEK
/// (alacak) KDV tek toplamda erir ve beyanname için anlamsız bir sayı çıkar. Alış tarafı geniş
/// görünümde AYRI toplam alır; <c>NetKdv = Satış − Alış</c>. Pivot satış-only kaldı (regresyon
/// sıfır — <see cref="Genis_satis_toplami_pivot_toplamiyla_AYNI"/> bunu kilitler).</para>
/// </summary>
[Collection("postgres")]
public sealed class FaturaDonemKdvGenisTests(PostgresFixture fx)
{
    private static DateTimeOffset D(int y, int m, int d) => new(y, m, d, 0, 0, 0, TimeSpan.Zero);

    /// <summary>CI-vs-lokal tick farkı: tarih tabanı TAM SANİYEYE hizalı (PG timestamptz µs).</summary>
    private static DateTimeOffset Day(int difference)
    {
        var t = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(difference), DateTimeKind.Utc), TimeSpan.Zero);
        return t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));
    }

    private static int _taxCounter = 1400000000;

    private static Task<Guid> CustomerAsync(IServiceScope s, string name)
        => s.ServiceProvider.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput
            {
                Tip = CustomerType.Kurumsal, Unvan = name,
                VergiNo = Interlocked.Increment(ref _taxCounter).ToString(), VergiDairesi = "Kadıköy"
            });

    /// <summary>Günlük 100 TL'lik kira kurar; gün sayısı çağırandan gelir (brüt = gün × 100).</summary>
    private static async Task<Guid> RentalAsync(
        IServiceScope s, Guid account, string plate, int startOffset, int endOffset, string? office = null)
    {
        var veh = await s.ServiceProvider.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plate, Durum = VehicleStatus.Musait });
        return await s.ServiceProvider.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = account, VehicleId = veh,
            BasTar = Day(startOffset), BitTar = Day(endOffset), GunlukUcret = 100m, CikisOfisi = office
        });
    }

    private static Invoice Inv(Guid customerId, string no, DateTimeOffset date, InvoiceStatus status,
        decimal exchangeRate, bool refund = false, params (decimal oran, decimal net, decimal kdv)[] lines)
    {
        var inv = new Invoice
        {
            No = no, Durum = status, CariId = customerId, Tarih = date, IadeMi = refund,
            Currency = exchangeRate == 1m ? "TRY" : "USD", Kur = exchangeRate,
            NetTutar = lines.Sum(l => l.net), KdvTutar = lines.Sum(l => l.kdv),
            GenelToplam = lines.Sum(l => l.net + l.kdv)
        };
        foreach (var (rate, net, vat) in lines)
            inv.Lines.Add(new InvoiceLine
            {
                InvoiceId = inv.Id, Aciklama = "kalem", Miktar = 1m, BirimNetFiyat = net,
                KdvOrani = rate, SatirNet = net, SatirKdv = vat, SatirToplam = net + vat
            });
        return inv;
    }

    // ================================================================ (1) faturalanmamış kira

    /// <summary>
    /// Spec senaryosu: 3 kira — biri base faturalı, biri YALNIZ fark faturalı (<c>KaynakKiraId</c>),
    /// biri faturasız. "Faturalanmamış" süzgeci SADECE üçüncüyü döndürmeli (beklenen adet = 1, sabit).
    ///
    /// <para>Fark faturası gerçek akışta base faturadan SONRA doğar; <c>KaynakKiraId</c> bacağının
    /// tek başına da "faturalanmış" saydığını izole sınamak için o satır doğrudan yazılır
    /// (rapor salt-okunur bir yol — defter dengesi bu testin konusu değil).</para>
    /// </summary>
    [Fact]
    public async Task Faturalanmamis_kira_yalniz_hic_faturasi_olmayani_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var account = await CustomerAsync(scope, "Dönem A.Ş.");

        var kBase = await RentalAsync(scope, account, "34 FD 01", -10, -7);
        var kDiff = await RentalAsync(scope, account, "34 FD 02", -10, -7);
        var kNone = await RentalAsync(scope, account, "34 FD 03", -10, -7);

        await scope.ServiceProvider.GetRequiredService<InvoiceService>().CreateFromRentalAsync(kBase);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var difference = Inv(account, "FT-FARK-1", Day(-6), InvoiceStatus.Kesildi, 1m, false, (0.20m, 100m, 20m));
            difference.KaynakKiraId = kDiff;   // base YOK, yalnız fark bacağı
            difference.KaynakKiraFarkSira = 1;
            db.Invoices.Add(difference);
            await db.SaveChangesAsync();
        }

        var report = scope.ServiceProvider.GetRequiredService<ReportService>();
        var none = await report.GetRentalInvoiceStatusAsync(Day(-15), Day(0),
            new KiraFaturaDurumFilter { Faturalanan = false });

        Assert.Single(none);                               // elle: 3 kiradan 1'i faturasız
        Assert.Equal(kNone, none[0].RentalId);
        Assert.False(none[0].Faturalanan);
        Assert.Equal(0, none[0].FaturaAdet);
        Assert.Equal(0m, none[0].FaturalananTutar);

        var var_ = await report.GetRentalInvoiceStatusAsync(Day(-15), Day(0),
            new KiraFaturaDurumFilter { Faturalanan = true });
        Assert.Equal(2, var_.Count);                      // base'li + fark'lı
        Assert.Contains(var_, r => r.RentalId == kBase);
        Assert.Contains(var_, r => r.RentalId == kDiff);
        Assert.Equal(120m, var_.Single(r => r.RentalId == kDiff).FaturalananTutar); // 100 + 20 KDV
    }

    /// <summary>Süzgeçsiz çağrı HEPSİNİ döndürür ve faturalanmamışlar BAŞTA sıralanır (raporun amacı).</summary>
    [Fact]
    public async Task Suzgecsiz_hepsini_dondurur_faturalanmamis_basta()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var account = await CustomerAsync(scope, "Sıra A.Ş.");
        var k1 = await RentalAsync(scope, account, "34 FS 01", -10, -7);
        await RentalAsync(scope, account, "34 FS 02", -10, -7);
        await scope.ServiceProvider.GetRequiredService<InvoiceService>().CreateFromRentalAsync(k1);

        var all = await scope.ServiceProvider.GetRequiredService<ReportService>()
            .GetRentalInvoiceStatusAsync(Day(-15), Day(0));

        Assert.Equal(2, all.Count);
        Assert.False(all[0].Faturalanan);   // faturalanmamış önce
        Assert.True(all[1].Faturalanan);
    }

    /// <summary>
    /// Dönem kuralı KESİŞİM: başlangıcı dönemden ÖNCE olan bir kira, bitişi dönem içindeyse
    /// listelenir. Başlangıç-tarihine bakan bir filtre onu düşürür ve fatura kaçağını gizlerdi.
    /// </summary>
    [Fact]
    public async Task Doneme_SARKAN_kira_kesisim_kuraliyla_gorunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var account = await CustomerAsync(scope, "Sarkan A.Ş.");
        // Kira: -20. günde başlar, -3. günde biter. Dönem: -10 .. 0 → başlangıç dönem DIŞI.
        var k = await RentalAsync(scope, account, "34 SK 01", -20, -3);

        var report = scope.ServiceProvider.GetRequiredService<ReportService>();
        var inside = await report.GetRentalInvoiceStatusAsync(Day(-10), Day(0));
        Assert.Single(inside);
        Assert.Equal(k, inside[0].RentalId);

        // Kirayla HİÇ kesişmeyen dönem (-40 .. -30) → boş.
        var outside = await report.GetRentalInvoiceStatusAsync(Day(-40), Day(-30));
        Assert.Empty(outside);
    }

    /// <summary>
    /// İPTAL fatura kirayı "faturalanmış" saymaz — kural <c>OrtakSorgular.FarkStateAsync</c> ile
    /// birebir aynıdır. Aksi halde iptal edilen bir belge kirayı listeden sessizce düşürürdü.
    /// </summary>
    [Fact]
    public async Task Iptal_fatura_kirayi_faturalanmis_saymaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var account = await CustomerAsync(scope, "İptal A.Ş.");
        var k = await RentalAsync(scope, account, "34 IP 01", -10, -7);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var inv = Inv(account, "FT-IPT-1", Day(-6), InvoiceStatus.Iptal, 1m, false, (0.20m, 100m, 20m));
            inv.RentalId = k;
            db.Invoices.Add(inv);
            await db.SaveChangesAsync();
        }

        var none = await scope.ServiceProvider.GetRequiredService<ReportService>()
            .GetRentalInvoiceStatusAsync(Day(-15), Day(0), new KiraFaturaDurumFilter { Faturalanan = false });
        Assert.Single(none);
        Assert.Equal(k, none[0].RentalId);
    }

    /// <summary>
    /// Faturalanan TUTAR iade-netlidir: 300 brüt kesilip 120 iade edilirse 180 kalır. Adet
    /// netlenmez (1 fatura kesilmiştir) — "kaç belge var" ile "ne kadarı ayakta" ayrı sorulardır.
    /// </summary>
    [Fact]
    public async Task Faturalanan_tutar_iade_netlidir_adet_netlenmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var account = await CustomerAsync(scope, "İade A.Ş.");
        var k = await RentalAsync(scope, account, "34 IA 01", -10, -7);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var inv = Inv(account, "FT-IAD-1", Day(-6), InvoiceStatus.Kesildi, 1m, false, (0.20m, 250m, 50m)); // 300
            inv.RentalId = k;
            db.Invoices.Add(inv);
            var refund = Inv(account, "FT-IAD-1-I", Day(-5), InvoiceStatus.Kesildi, 1m, true, (0.20m, 100m, 20m)); // 120
            refund.KaynakFaturaId = inv.Id;
            db.Invoices.Add(refund);
            await db.SaveChangesAsync();
        }

        var row = (await scope.ServiceProvider.GetRequiredService<ReportService>()
            .GetRentalInvoiceStatusAsync(Day(-15), Day(0))).Single();
        Assert.True(row.Faturalanan);
        Assert.Equal(1, row.FaturaAdet);        // iade AYRI belge, kesilen adedi değiştirmez
        Assert.Equal(180m, row.FaturalananTutar); // elle: 300 − 120
    }

    /// <summary>
    /// İşlem Şube süzgeci türetilmiş <c>CikisSubeId</c> FK'sı üzerinden çalışır: aynı şubenin
    /// FARKLI ofislerindeki kiralar tek seçimle gelir (metin eşleşmesi bunu yapamazdı).
    /// </summary>
    [Fact]
    public async Task Sube_suzgeci_subenin_TUM_ofislerini_getirir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var branches = scope.ServiceProvider.GetRequiredService<BranchService>();
        var branchA = await branches.CreateAsync(new BranchInput { Kod = "SA", Ad = "Şube A" });
        var branchB = await branches.CreateAsync(new BranchInput { Kod = "SB", Ad = "Şube B" });

        // Location.SubeId'yi BranchFkInterceptor Sube METNİNDEN çözer (master şube adı birebir).
        var loc = scope.ServiceProvider.GetRequiredService<LocationService>();
        await loc.CreateAsync(new LocationInput { Kod = "IST", Ad = "Atatürk Havalimanı", Sube = "Şube A" });
        await loc.CreateAsync(new LocationInput { Kod = "KDK", Ad = "Kadıköy Ofis", Sube = "Şube A" });
        await loc.CreateAsync(new LocationInput { Kod = "IZM", Ad = "İzmir Ofis", Sube = "Şube B" });

        var account = await CustomerAsync(scope, "Şube A.Ş.");
        await RentalAsync(scope, account, "34 SB 01", -10, -7, "Atatürk Havalimanı");
        await RentalAsync(scope, account, "34 SB 02", -10, -7, "Kadıköy Ofis");
        await RentalAsync(scope, account, "34 SB 03", -10, -7, "İzmir Ofis");

        var report = scope.ServiceProvider.GetRequiredService<ReportService>();
        var a = await report.GetRentalInvoiceStatusAsync(Day(-15), Day(0), new KiraFaturaDurumFilter { SubeId = branchA });
        Assert.Equal(2, a.Count);   // elle: A şubesinin iki ofisi
        Assert.All(a, r => Assert.Contains(r.Ofis, new[] { "Atatürk Havalimanı", "Kadıköy Ofis" }));

        var b = await report.GetRentalInvoiceStatusAsync(Day(-15), Day(0), new KiraFaturaDurumFilter { SubeId = branchB });
        Assert.Single(b);
        Assert.Equal("İzmir Ofis", b[0].Ofis);
    }

    /// <summary>Serbest metin araması plaka / sözleşme no / cari adında çalışır (büyük-küçük harf duyarsız).</summary>
    [Fact]
    public async Task Serbest_metin_plaka_ve_cari_uzerinde_calisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var accountX = await CustomerAsync(scope, "Zebra Turizm");
        var accountY = await CustomerAsync(scope, "Papatya Lojistik");
        await RentalAsync(scope, accountX, "34 ZB 99", -10, -7);
        await RentalAsync(scope, accountY, "06 PP 11", -10, -7);

        var report = scope.ServiceProvider.GetRequiredService<ReportService>();
        Assert.Single(await report.GetRentalInvoiceStatusAsync(Day(-15), Day(0), new KiraFaturaDurumFilter { Q = "zebra" }));
        Assert.Single(await report.GetRentalInvoiceStatusAsync(Day(-15), Day(0), new KiraFaturaDurumFilter { Q = "06 PP" }));
        Assert.Empty(await report.GetRentalInvoiceStatusAsync(Day(-15), Day(0), new KiraFaturaDurumFilter { Q = "yokboyle" }));
    }

    // ================================================================ (2) KDV geniş format

    /// <summary>
    /// Spec senaryosu: 2 fatura (%20 ve %10) → her belge KENDİ satırında, tutarlar DOĞRU oran
    /// sütununda. Beklenen değerler elle: A %20 (100/20), B %10 (50/5).
    /// </summary>
    [Fact]
    public async Task Genis_format_belgeyi_dogru_oran_sutununa_dagitir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var c = new Customer { Tip = CustomerType.Kurumsal, Unvan = "Geniş A.Ş." };
            db.Customers.Add(c);
            db.Invoices.Add(Inv(c.Id, "FT-G01", D(2026, 6, 10), InvoiceStatus.Kesildi, 1m, false, (0.20m, 100m, 20m)));
            db.Invoices.Add(Inv(c.Id, "FT-G02", D(2026, 6, 11), InvoiceStatus.Kesildi, 1m, false, (0.10m, 50m, 5m)));
            db.Invoices.Add(Inv(c.Id, "FT-G03", D(2026, 6, 12), InvoiceStatus.Iptal, 1m, false, (0.20m, 999m, 199.8m)));
            await db.SaveChangesAsync();
        }

        var g = await scope.ServiceProvider.GetRequiredService<ReportService>()
            .GetVatExtendedAsync(D(2026, 6, 1), D(2026, 6, 30).AddDays(1).AddTicks(-1));

        Assert.Equal(2, g.Satirlar.Count);   // İptal HARİÇ
        var a = g.Satirlar.Single(r => r.No == "FT-G01");
        Assert.Equal(100m, a.Net20); Assert.Equal(20m, a.Kdv20);
        Assert.Equal(0m, a.Net10); Assert.Equal(0m, a.Kdv10);
        Assert.Equal(120m, a.ToplamBrut);

        var b = g.Satirlar.Single(r => r.No == "FT-G02");
        Assert.Equal(50m, b.Net10); Assert.Equal(5m, b.Kdv10);
        Assert.Equal(0m, b.Net20);

        Assert.Equal(150m, g.SatisNet);   // elle: 100 + 50
        Assert.Equal(25m, g.SatisKdv);    // elle: 20 + 5
        Assert.Equal(0m, g.AlisKdv);      // alış dahil edilmedi
        Assert.Equal(25m, g.NetKdv);
    }

    /// <summary>
    /// KADEME-DIŞI oran (geçmiş %18) sessizce DÜŞMEZ, "Diğer" kovasında toplanır ve satır toplamı
    /// sütunların toplamına EŞİT kalır. Bu kilit olmadan rapor yalancı olurdu.
    /// </summary>
    [Fact]
    public async Task Kademe_disi_oran_Diger_kovasinda_toplanir_ve_toplam_TUTAR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var c = new Customer { Tip = CustomerType.Kurumsal, Unvan = "Eski Oran A.Ş." };
            db.Customers.Add(c);
            // %20 (100/20) + %18 (200/36) + %0 (30/0) tek belgede.
            db.Invoices.Add(Inv(c.Id, "FT-D01", D(2026, 6, 10), InvoiceStatus.Kesildi, 1m, false,
                (0.20m, 100m, 20m), (0.18m, 200m, 36m), (0m, 30m, 0m)));
            await db.SaveChangesAsync();
        }

        var g = await scope.ServiceProvider.GetRequiredService<ReportService>()
            .GetVatExtendedAsync(D(2026, 6, 1), D(2026, 6, 30).AddDays(1).AddTicks(-1));

        var r = Assert.Single(g.Satirlar);
        Assert.Equal(100m, r.Net20); Assert.Equal(20m, r.Kdv20);
        Assert.Equal(200m, r.DigerNet); Assert.Equal(36m, r.DigerKdv);
        Assert.Equal(30m, r.Net0);
        Assert.Equal(330m, r.ToplamNet);   // elle: 100 + 200 + 30
        Assert.Equal(56m, r.ToplamKdv);    // elle: 20 + 36
        // Kilit: sütunlar toplamı == satır toplamı (hiçbir tutar kaybolmaz).
        Assert.Equal(r.ToplamNet, r.Net20 + r.Net10 + r.Net1 + r.Net0 + r.DigerNet);
        Assert.Equal(r.ToplamKdv, r.Kdv20 + r.Kdv10 + r.Kdv1 + r.DigerKdv);
    }

    /// <summary>
    /// REGRESYON KİLİDİ: geniş görünümün SATIŞ toplamı, mevcut oran-pivotunun toplamıyla BİREBİR
    /// aynı olmalı. İki görünüm ayrışırsa biri yalan söylüyordur. Çok-döviz (Kur) ve iade (negatif)
    /// dahil edilerek sınanır — iki yolun da aynı işaret/kur sözleşmesini kullandığı kanıtlanır.
    /// </summary>
    [Fact]
    public async Task Genis_satis_toplami_pivot_toplamiyla_AYNI()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var c = new Customer { Tip = CustomerType.Kurumsal, Unvan = "Parite A.Ş." };
            db.Customers.Add(c);
            db.Invoices.Add(Inv(c.Id, "FT-P01", D(2026, 6, 10), InvoiceStatus.Kesildi, 1m, false, (0.20m, 100m, 20m)));
            db.Invoices.Add(Inv(c.Id, "FT-P02", D(2026, 6, 11), InvoiceStatus.Kesildi, 30m, false, (0.20m, 10m, 2m)));
            db.Invoices.Add(Inv(c.Id, "FT-P03", D(2026, 6, 12), InvoiceStatus.Kesildi, 1m, true, (0.10m, 50m, 5m)));
            await db.SaveChangesAsync();
        }

        var svc = scope.ServiceProvider.GetRequiredService<ReportService>();
        var from = D(2026, 6, 1); var to = D(2026, 6, 30).AddDays(1).AddTicks(-1);
        var pivot = await svc.GetVatListAsync(from, to);
        var wide = await svc.GetVatExtendedAsync(from, to, includePurchases: true);

        Assert.Equal(pivot.ToplamNet, wide.SatisNet);
        Assert.Equal(pivot.ToplamKdv, wide.SatisKdv);
        // Elle: %20 → 100 + (10 × 30) = 400 net, 20 + 60 = 80 KDV; %10 iade → −50 net, −5 KDV.
        Assert.Equal(350m, wide.SatisNet);
        Assert.Equal(75m, wide.SatisKdv);
    }

    // ================================================================ (3) alış (gelen e-Fatura) KDV

    private static GelenEFaturaInput Incoming(string ettn, decimal net, decimal vat, DateTimeOffset date, string currency = "TRY")
        => new()
        {
            Ettn = ettn, GonderenVkn = "1234567890", GonderenUnvan = "Tedarikçi A.Ş.",
            Tarih = date, NetTutar = net, KdvTutar = vat, GenelToplam = net + vat, Currency = currency
        };

    /// <summary>
    /// Spec senaryosu: 1 gelen e-Fatura (oran kırılımlı) + 1 satış faturası. Satış ve alış AYRI
    /// toplanır; Net KDV = satış − alış. Elle: satış KDV 20, alış KDV 200 → net −180 (devreden).
    /// </summary>
    [Fact]
    public async Task Alis_KDV_dahil_edilir_ve_satistan_CIKARILIR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var date = D(2026, 6, 15);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var c = new Customer { Tip = CustomerType.Kurumsal, Unvan = "Alış A.Ş." };
            db.Customers.Add(c);
            db.Invoices.Add(Inv(c.Id, "FT-A01", D(2026, 6, 10), InvoiceStatus.Kesildi, 1m, false, (0.20m, 100m, 20m)));
            await db.SaveChangesAsync();
        }

        var incoming = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();
        var gid = await incoming.CreateManualAsync(Incoming("ETTN-A1", 1000m, 200m, date));
        Assert.True(await incoming.LinkAsync(new GelenEFaturaBaglamaInput { Id = gid, Kdv20Matrah = 1000m, Kdv20 = 200m }));

        var svc = scope.ServiceProvider.GetRequiredService<ReportService>();
        var from = D(2026, 6, 1); var to = D(2026, 6, 30).AddDays(1).AddTicks(-1);

        var withoutPurchase = await svc.GetVatExtendedAsync(from, to, includePurchases: false);
        Assert.Single(withoutPurchase.Satirlar);
        Assert.Equal(0m, withoutPurchase.AlisKdv);

        var withPurchase = await svc.GetVatExtendedAsync(from, to, includePurchases: true);
        Assert.Equal(2, withPurchase.Satirlar.Count);
        Assert.Equal(20m, withPurchase.SatisKdv);      // elle
        Assert.Equal(200m, withPurchase.AlisKdv);      // elle
        Assert.Equal(1000m, withPurchase.AlisNet);
        Assert.Equal(-180m, withPurchase.NetKdv);      // elle: 20 − 200 → devreden
        Assert.Equal(0, withPurchase.AtlananDovizliAlis);

        var purchaseLine = withPurchase.Satirlar.Single(r => r.AlisMi);
        Assert.Equal("ETTN-A1", purchaseLine.No);
        Assert.Equal(1000m, purchaseLine.Net20);
        Assert.Equal(200m, purchaseLine.Kdv20);
    }

    /// <summary>
    /// Alış tarafı elemesi: REDDEDİLEN belge (KDV'si indirilemez) ve KIRILIMI GİRİLMEMİŞ belge
    /// (kademesi bilinmiyor — "hepsi %20" varsaymak uydurma beyandır) rapora GİRMEZ.
    /// </summary>
    [Fact]
    public async Task Reddedilen_ve_kirilimsiz_alis_rapora_GIRMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var date = D(2026, 6, 15);
        var incoming = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();

        // A: kırılımlı + Beklemede → GİRER
        var a = await incoming.CreateManualAsync(Incoming("ETTN-R1", 100m, 20m, date));
        await incoming.LinkAsync(new GelenEFaturaBaglamaInput { Id = a, Kdv20Matrah = 100m, Kdv20 = 20m });
        // B: kırılımlı ama REDDEDİLDİ → GİRMEZ
        var b = await incoming.CreateManualAsync(Incoming("ETTN-R2", 500m, 100m, date));
        await incoming.LinkAsync(new GelenEFaturaBaglamaInput { Id = b, Kdv20Matrah = 500m, Kdv20 = 100m });
        await incoming.RejectAsync(b, "Yanlış firma");
        // C: kırılım GİRİLMEMİŞ → GİRMEZ
        await incoming.CreateManualAsync(Incoming("ETTN-R3", 900m, 180m, date));

        var g = await scope.ServiceProvider.GetRequiredService<ReportService>()
            .GetVatExtendedAsync(D(2026, 6, 1), D(2026, 6, 30).AddDays(1).AddTicks(-1), includePurchases: true);

        var purchase = g.Satirlar.Where(r => r.AlisMi).ToList();
        Assert.Single(purchase);                 // elle: 3 belgeden yalnız A
        Assert.Equal("ETTN-R1", purchase[0].No);
        Assert.Equal(20m, g.AlisKdv);
    }

    /// <summary>
    /// DÖVİZLİ gelen e-Fatura baz paraya çevrilemez (belgede kur alanı YOK) — sessizce yanlış
    /// toplanmak yerine DIŞARIDA bırakılır ve sayısı çağırana bildirilir.
    /// </summary>
    [Fact]
    public async Task Dovizli_alis_disarida_birakilir_ve_SAYISI_bildirilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var date = D(2026, 6, 15);
        var incoming = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();

        var tl = await incoming.CreateManualAsync(Incoming("ETTN-D1", 100m, 20m, date));
        await incoming.LinkAsync(new GelenEFaturaBaglamaInput { Id = tl, Kdv20Matrah = 100m, Kdv20 = 20m });
        var usd = await incoming.CreateManualAsync(Incoming("ETTN-D2", 1000m, 200m, date, "USD"));
        await incoming.LinkAsync(new GelenEFaturaBaglamaInput { Id = usd, Kdv20Matrah = 1000m, Kdv20 = 200m });

        var g = await scope.ServiceProvider.GetRequiredService<ReportService>()
            .GetVatExtendedAsync(D(2026, 6, 1), D(2026, 6, 30).AddDays(1).AddTicks(-1), includePurchases: true);

        Assert.Equal(1, g.AtlananDovizliAlis);
        Assert.Equal(20m, g.AlisKdv);        // yalnız TL belge; 200 USD KDV toplama KARIŞMADI
        Assert.Single(g.Satirlar, r => r.AlisMi);
    }

    /// <summary>
    /// TENANT İZOLASYONU: başka tenant'ın kirası/faturası hiçbir görünümde sızmaz (racar_app + RLS).
    /// </summary>
    [Fact]
    public async Task Tenant_izolasyonu_iki_gorunumde_de_gecerli()
    {
        var t1 = Guid.NewGuid(); var t2 = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);

        using (var s1 = host.ScopeFor(t1))
        {
            var c = await CustomerAsync(s1, "T1 A.Ş.");
            await RentalAsync(s1, c, "34 T1 01", -10, -7);
            var f = s1.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await f.CreateDbContextAsync();
            db.Invoices.Add(Inv(c, "FT-T1", D(2026, 6, 10), InvoiceStatus.Kesildi, 1m, false, (0.20m, 100m, 20m)));
            await db.SaveChangesAsync();
        }

        using var s2 = host.ScopeFor(t2);
        var report = s2.ServiceProvider.GetRequiredService<ReportService>();
        Assert.Empty(await report.GetRentalInvoiceStatusAsync(Day(-15), Day(0)));
        var g = await report.GetVatExtendedAsync(D(2026, 6, 1), D(2026, 6, 30).AddDays(1).AddTicks(-1), includePurchases: true);
        Assert.Empty(g.Satirlar);
        Assert.Equal(0m, g.SatisKdv);
    }
}
