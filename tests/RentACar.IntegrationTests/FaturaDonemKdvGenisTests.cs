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
    private static DateTimeOffset Gun(int fark)
    {
        var t = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(fark), DateTimeKind.Utc), TimeSpan.Zero);
        return t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));
    }

    private static int _vergiSayac = 1400000000;

    private static Task<Guid> CariAsync(IServiceScope s, string ad)
        => s.ServiceProvider.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput
            {
                Tip = CustomerType.Kurumsal, Unvan = ad,
                VergiNo = Interlocked.Increment(ref _vergiSayac).ToString(), VergiDairesi = "Kadıköy"
            });

    /// <summary>Günlük 100 TL'lik kira kurar; gün sayısı çağırandan gelir (brüt = gün × 100).</summary>
    private static async Task<Guid> KiraAsync(
        IServiceScope s, Guid cari, string plaka, int basFark, int bitFark, string? ofis = null)
    {
        var veh = await s.ServiceProvider.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plaka, Durum = VehicleStatus.Musait });
        return await s.ServiceProvider.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = cari, VehicleId = veh,
            BasTar = Gun(basFark), BitTar = Gun(bitFark), GunlukUcret = 100m, CikisOfisi = ofis
        });
    }

    private static Invoice Inv(Guid cariId, string no, DateTimeOffset tarih, InvoiceStatus durum,
        decimal kur, bool iade = false, params (decimal oran, decimal net, decimal kdv)[] lines)
    {
        var inv = new Invoice
        {
            No = no, Durum = durum, CariId = cariId, Tarih = tarih, IadeMi = iade,
            Currency = kur == 1m ? "TRY" : "USD", Kur = kur,
            NetTutar = lines.Sum(l => l.net), KdvTutar = lines.Sum(l => l.kdv),
            GenelToplam = lines.Sum(l => l.net + l.kdv)
        };
        foreach (var (oran, net, kdv) in lines)
            inv.Lines.Add(new InvoiceLine
            {
                InvoiceId = inv.Id, Aciklama = "kalem", Miktar = 1m, BirimNetFiyat = net,
                KdvOrani = oran, SatirNet = net, SatirKdv = kdv, SatirToplam = net + kdv
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
        var cari = await CariAsync(scope, "Dönem A.Ş.");

        var kBase = await KiraAsync(scope, cari, "34 FD 01", -10, -7);
        var kFark = await KiraAsync(scope, cari, "34 FD 02", -10, -7);
        var kYok = await KiraAsync(scope, cari, "34 FD 03", -10, -7);

        await scope.ServiceProvider.GetRequiredService<InvoiceService>().CreateFromRentalAsync(kBase);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var fark = Inv(cari, "FT-FARK-1", Gun(-6), InvoiceStatus.Kesildi, 1m, false, (0.20m, 100m, 20m));
            fark.KaynakKiraId = kFark;   // base YOK, yalnız fark bacağı
            fark.KaynakKiraFarkSira = 1;
            db.Invoices.Add(fark);
            await db.SaveChangesAsync();
        }

        var rapor = scope.ServiceProvider.GetRequiredService<ReportService>();
        var yok = await rapor.GetRentalInvoiceStatusAsync(Gun(-15), Gun(0),
            new KiraFaturaDurumFilter { Faturalanan = false });

        Assert.Single(yok);                               // elle: 3 kiradan 1'i faturasız
        Assert.Equal(kYok, yok[0].RentalId);
        Assert.False(yok[0].Faturalanan);
        Assert.Equal(0, yok[0].FaturaAdet);
        Assert.Equal(0m, yok[0].FaturalananTutar);

        var var_ = await rapor.GetRentalInvoiceStatusAsync(Gun(-15), Gun(0),
            new KiraFaturaDurumFilter { Faturalanan = true });
        Assert.Equal(2, var_.Count);                      // base'li + fark'lı
        Assert.Contains(var_, r => r.RentalId == kBase);
        Assert.Contains(var_, r => r.RentalId == kFark);
        Assert.Equal(120m, var_.Single(r => r.RentalId == kFark).FaturalananTutar); // 100 + 20 KDV
    }

    /// <summary>Süzgeçsiz çağrı HEPSİNİ döndürür ve faturalanmamışlar BAŞTA sıralanır (raporun amacı).</summary>
    [Fact]
    public async Task Suzgecsiz_hepsini_dondurur_faturalanmamis_basta()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cari = await CariAsync(scope, "Sıra A.Ş.");
        var k1 = await KiraAsync(scope, cari, "34 FS 01", -10, -7);
        await KiraAsync(scope, cari, "34 FS 02", -10, -7);
        await scope.ServiceProvider.GetRequiredService<InvoiceService>().CreateFromRentalAsync(k1);

        var hepsi = await scope.ServiceProvider.GetRequiredService<ReportService>()
            .GetRentalInvoiceStatusAsync(Gun(-15), Gun(0));

        Assert.Equal(2, hepsi.Count);
        Assert.False(hepsi[0].Faturalanan);   // faturalanmamış önce
        Assert.True(hepsi[1].Faturalanan);
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
        var cari = await CariAsync(scope, "Sarkan A.Ş.");
        // Kira: -20. günde başlar, -3. günde biter. Dönem: -10 .. 0 → başlangıç dönem DIŞI.
        var k = await KiraAsync(scope, cari, "34 SK 01", -20, -3);

        var rapor = scope.ServiceProvider.GetRequiredService<ReportService>();
        var icinde = await rapor.GetRentalInvoiceStatusAsync(Gun(-10), Gun(0));
        Assert.Single(icinde);
        Assert.Equal(k, icinde[0].RentalId);

        // Kirayla HİÇ kesişmeyen dönem (-40 .. -30) → boş.
        var disinda = await rapor.GetRentalInvoiceStatusAsync(Gun(-40), Gun(-30));
        Assert.Empty(disinda);
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
        var cari = await CariAsync(scope, "İptal A.Ş.");
        var k = await KiraAsync(scope, cari, "34 IP 01", -10, -7);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var inv = Inv(cari, "FT-IPT-1", Gun(-6), InvoiceStatus.Iptal, 1m, false, (0.20m, 100m, 20m));
            inv.RentalId = k;
            db.Invoices.Add(inv);
            await db.SaveChangesAsync();
        }

        var yok = await scope.ServiceProvider.GetRequiredService<ReportService>()
            .GetRentalInvoiceStatusAsync(Gun(-15), Gun(0), new KiraFaturaDurumFilter { Faturalanan = false });
        Assert.Single(yok);
        Assert.Equal(k, yok[0].RentalId);
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
        var cari = await CariAsync(scope, "İade A.Ş.");
        var k = await KiraAsync(scope, cari, "34 IA 01", -10, -7);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var inv = Inv(cari, "FT-IAD-1", Gun(-6), InvoiceStatus.Kesildi, 1m, false, (0.20m, 250m, 50m)); // 300
            inv.RentalId = k;
            db.Invoices.Add(inv);
            var iade = Inv(cari, "FT-IAD-1-I", Gun(-5), InvoiceStatus.Kesildi, 1m, true, (0.20m, 100m, 20m)); // 120
            iade.KaynakFaturaId = inv.Id;
            db.Invoices.Add(iade);
            await db.SaveChangesAsync();
        }

        var satir = (await scope.ServiceProvider.GetRequiredService<ReportService>()
            .GetRentalInvoiceStatusAsync(Gun(-15), Gun(0))).Single();
        Assert.True(satir.Faturalanan);
        Assert.Equal(1, satir.FaturaAdet);        // iade AYRI belge, kesilen adedi değiştirmez
        Assert.Equal(180m, satir.FaturalananTutar); // elle: 300 − 120
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
        var subeler = scope.ServiceProvider.GetRequiredService<BranchService>();
        var subeA = await subeler.CreateAsync(new BranchInput { Kod = "SA", Ad = "Şube A" });
        var subeB = await subeler.CreateAsync(new BranchInput { Kod = "SB", Ad = "Şube B" });

        // Location.SubeId'yi BranchFkInterceptor Sube METNİNDEN çözer (master şube adı birebir).
        var loc = scope.ServiceProvider.GetRequiredService<LocationService>();
        await loc.CreateAsync(new LocationInput { Kod = "IST", Ad = "Atatürk Havalimanı", Sube = "Şube A" });
        await loc.CreateAsync(new LocationInput { Kod = "KDK", Ad = "Kadıköy Ofis", Sube = "Şube A" });
        await loc.CreateAsync(new LocationInput { Kod = "IZM", Ad = "İzmir Ofis", Sube = "Şube B" });

        var cari = await CariAsync(scope, "Şube A.Ş.");
        await KiraAsync(scope, cari, "34 SB 01", -10, -7, "Atatürk Havalimanı");
        await KiraAsync(scope, cari, "34 SB 02", -10, -7, "Kadıköy Ofis");
        await KiraAsync(scope, cari, "34 SB 03", -10, -7, "İzmir Ofis");

        var rapor = scope.ServiceProvider.GetRequiredService<ReportService>();
        var a = await rapor.GetRentalInvoiceStatusAsync(Gun(-15), Gun(0), new KiraFaturaDurumFilter { SubeId = subeA });
        Assert.Equal(2, a.Count);   // elle: A şubesinin iki ofisi
        Assert.All(a, r => Assert.Contains(r.Ofis, new[] { "Atatürk Havalimanı", "Kadıköy Ofis" }));

        var b = await rapor.GetRentalInvoiceStatusAsync(Gun(-15), Gun(0), new KiraFaturaDurumFilter { SubeId = subeB });
        Assert.Single(b);
        Assert.Equal("İzmir Ofis", b[0].Ofis);
    }

    /// <summary>Serbest metin araması plaka / sözleşme no / cari adında çalışır (büyük-küçük harf duyarsız).</summary>
    [Fact]
    public async Task Serbest_metin_plaka_ve_cari_uzerinde_calisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cariX = await CariAsync(scope, "Zebra Turizm");
        var cariY = await CariAsync(scope, "Papatya Lojistik");
        await KiraAsync(scope, cariX, "34 ZB 99", -10, -7);
        await KiraAsync(scope, cariY, "06 PP 11", -10, -7);

        var rapor = scope.ServiceProvider.GetRequiredService<ReportService>();
        Assert.Single(await rapor.GetRentalInvoiceStatusAsync(Gun(-15), Gun(0), new KiraFaturaDurumFilter { Q = "zebra" }));
        Assert.Single(await rapor.GetRentalInvoiceStatusAsync(Gun(-15), Gun(0), new KiraFaturaDurumFilter { Q = "06 PP" }));
        Assert.Empty(await rapor.GetRentalInvoiceStatusAsync(Gun(-15), Gun(0), new KiraFaturaDurumFilter { Q = "yokboyle" }));
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
        var genis = await svc.GetVatExtendedAsync(from, to, includePurchases: true);

        Assert.Equal(pivot.ToplamNet, genis.SatisNet);
        Assert.Equal(pivot.ToplamKdv, genis.SatisKdv);
        // Elle: %20 → 100 + (10 × 30) = 400 net, 20 + 60 = 80 KDV; %10 iade → −50 net, −5 KDV.
        Assert.Equal(350m, genis.SatisNet);
        Assert.Equal(75m, genis.SatisKdv);
    }

    // ================================================================ (3) alış (gelen e-Fatura) KDV

    private static GelenEFaturaInput Gelen(string ettn, decimal net, decimal kdv, DateTimeOffset tarih, string doviz = "TRY")
        => new()
        {
            Ettn = ettn, GonderenVkn = "1234567890", GonderenUnvan = "Tedarikçi A.Ş.",
            Tarih = tarih, NetTutar = net, KdvTutar = kdv, GenelToplam = net + kdv, Currency = doviz
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
        var tarih = D(2026, 6, 15);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var c = new Customer { Tip = CustomerType.Kurumsal, Unvan = "Alış A.Ş." };
            db.Customers.Add(c);
            db.Invoices.Add(Inv(c.Id, "FT-A01", D(2026, 6, 10), InvoiceStatus.Kesildi, 1m, false, (0.20m, 100m, 20m)));
            await db.SaveChangesAsync();
        }

        var gelen = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();
        var gid = await gelen.CreateManualAsync(Gelen("ETTN-A1", 1000m, 200m, tarih));
        Assert.True(await gelen.LinkAsync(new GelenEFaturaBaglamaInput { Id = gid, Kdv20Matrah = 1000m, Kdv20 = 200m }));

        var svc = scope.ServiceProvider.GetRequiredService<ReportService>();
        var from = D(2026, 6, 1); var to = D(2026, 6, 30).AddDays(1).AddTicks(-1);

        var alissiz = await svc.GetVatExtendedAsync(from, to, includePurchases: false);
        Assert.Single(alissiz.Satirlar);
        Assert.Equal(0m, alissiz.AlisKdv);

        var alisli = await svc.GetVatExtendedAsync(from, to, includePurchases: true);
        Assert.Equal(2, alisli.Satirlar.Count);
        Assert.Equal(20m, alisli.SatisKdv);      // elle
        Assert.Equal(200m, alisli.AlisKdv);      // elle
        Assert.Equal(1000m, alisli.AlisNet);
        Assert.Equal(-180m, alisli.NetKdv);      // elle: 20 − 200 → devreden
        Assert.Equal(0, alisli.AtlananDovizliAlis);

        var alisSatir = alisli.Satirlar.Single(r => r.AlisMi);
        Assert.Equal("ETTN-A1", alisSatir.No);
        Assert.Equal(1000m, alisSatir.Net20);
        Assert.Equal(200m, alisSatir.Kdv20);
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
        var tarih = D(2026, 6, 15);
        var gelen = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();

        // A: kırılımlı + Beklemede → GİRER
        var a = await gelen.CreateManualAsync(Gelen("ETTN-R1", 100m, 20m, tarih));
        await gelen.LinkAsync(new GelenEFaturaBaglamaInput { Id = a, Kdv20Matrah = 100m, Kdv20 = 20m });
        // B: kırılımlı ama REDDEDİLDİ → GİRMEZ
        var b = await gelen.CreateManualAsync(Gelen("ETTN-R2", 500m, 100m, tarih));
        await gelen.LinkAsync(new GelenEFaturaBaglamaInput { Id = b, Kdv20Matrah = 500m, Kdv20 = 100m });
        await gelen.RejectAsync(b, "Yanlış firma");
        // C: kırılım GİRİLMEMİŞ → GİRMEZ
        await gelen.CreateManualAsync(Gelen("ETTN-R3", 900m, 180m, tarih));

        var g = await scope.ServiceProvider.GetRequiredService<ReportService>()
            .GetVatExtendedAsync(D(2026, 6, 1), D(2026, 6, 30).AddDays(1).AddTicks(-1), includePurchases: true);

        var alis = g.Satirlar.Where(r => r.AlisMi).ToList();
        Assert.Single(alis);                 // elle: 3 belgeden yalnız A
        Assert.Equal("ETTN-R1", alis[0].No);
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
        var tarih = D(2026, 6, 15);
        var gelen = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();

        var tl = await gelen.CreateManualAsync(Gelen("ETTN-D1", 100m, 20m, tarih));
        await gelen.LinkAsync(new GelenEFaturaBaglamaInput { Id = tl, Kdv20Matrah = 100m, Kdv20 = 20m });
        var usd = await gelen.CreateManualAsync(Gelen("ETTN-D2", 1000m, 200m, tarih, "USD"));
        await gelen.LinkAsync(new GelenEFaturaBaglamaInput { Id = usd, Kdv20Matrah = 1000m, Kdv20 = 200m });

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
            var c = await CariAsync(s1, "T1 A.Ş.");
            await KiraAsync(s1, c, "34 T1 01", -10, -7);
            var f = s1.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await f.CreateDbContextAsync();
            db.Invoices.Add(Inv(c, "FT-T1", D(2026, 6, 10), InvoiceStatus.Kesildi, 1m, false, (0.20m, 100m, 20m)));
            await db.SaveChangesAsync();
        }

        using var s2 = host.ScopeFor(t2);
        var rapor = s2.ServiceProvider.GetRequiredService<ReportService>();
        Assert.Empty(await rapor.GetRentalInvoiceStatusAsync(Gun(-15), Gun(0)));
        var g = await rapor.GetVatExtendedAsync(D(2026, 6, 1), D(2026, 6, 30).AddDays(1).AddTicks(-1), includePurchases: true);
        Assert.Empty(g.Satirlar);
        Assert.Equal(0m, g.SatisKdv);
    }
}
