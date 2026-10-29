using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Hgs;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Import;

namespace RentACar.IntegrationTests;

/// <summary>
/// HGS geçişlerini dosyadan içe aktarma + kiraya eşleştirme + müşteriye yansıtma (PARA) — gerçek PostgreSQL,
/// <c>racar_app</c> (RLS) bağlantısı.
/// <para><b>BAĞIMSIZ ORACLE:</b> dosya elle kurulur; her satırın beklenen kararı (eşleşti / kira dışı / mükerrer /
/// hatalı) ve tutarlar elle hesaplanmıştır. İstanbul saati UTC+3 SABİTİYLE çevrilir (2016'dan beri yaz saati yok) —
/// üretimdeki saat dilimi kodu çağrılmaz. Yansıtma: 52,50 × 1,03 = 54,075 → 54,08; 995,00 × 1,03 = 1.024,85;
/// toplam 1.078,93. Tarihler bugünden geriye göreli (sabit tarih yok).</para>
/// </summary>
[Collection("postgres")]
public sealed class HgsImportTests(PostgresFixture fx)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly TimeSpan Istanbul = TimeSpan.FromHours(3);

    /// <summary>Kira günü D: İstanbul'da bugünden 5 gün önce.</summary>
    private static readonly DateOnly D = DateOnly.FromDateTime(DateTime.UtcNow.Add(Istanbul)).AddDays(-5);

    /// <summary>İstanbul duvar saati → UTC (oracle: sabit +03:00).</summary>
    internal static DateTimeOffset At(int dayOffset, int hour, int minute = 0, int second = 0)
        => new DateTimeOffset(D.AddDays(dayOffset).ToDateTime(new TimeOnly(hour, minute, second)), Istanbul).ToUniversalTime();

    /// <summary>Dosyada görünen İstanbul duvar saati metni.</summary>
    private static string Wall(int dayOffset, int hour, int minute = 0, int second = 0, string format = "dd.MM.yyyy HH:mm:ss")
        => D.AddDays(dayOffset).ToDateTime(new TimeOnly(hour, minute, second)).ToString(format, Inv);

    internal sealed record World(Guid Tenant, Guid CustomerA, Guid CustomerB, Guid V1, Guid V2, Guid R1, Guid R2, Guid R3);

    /// <summary>
    /// V1 (34 HGS 01, SubeA): R1 = müşteri A, D 00:00 → D 12:00 (kapandı); R2 = müşteri B, D+2 10:00 → AÇIK ve TESLİM
    /// EDİLMİŞ (çıkış KM 1000 — teslim edilmemiş açık kira Belirsiz üretir, #391 L3).
    /// V2 (06 HGS 02, SubeB): R3 = müşteri B, D-1 08:00 → D-1 20:00 (kapandı). İptal kira (gürültü) V1'de D+1.
    /// </summary>
    internal static async Task<World> SetUpAsync(TestHost host, Guid? tenantId = null)
    {
        var tenant = tenantId ?? Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var customers = sp.GetRequiredService<CustomerService>();
        var a = await customers.CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Hgs", Soyad = "Musteri A" });
        var b = await customers.CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Hgs", Soyad = "Musteri B" });
        var vehicles = sp.GetRequiredService<VehicleService>();
        var v1 = await vehicles.CreateAsync(new VehicleInput { Plaka = "34 HGS 01", Sube = "SubeA" });
        var v2 = await vehicles.CreateAsync(new VehicleInput { Plaka = "06 HGS 02", Sube = "SubeB" });
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var r1 = new RentalContract
        {
            SozlesmeNo = "KS-HGS-1", MusteriId = a, VehicleId = v1, Durum = RentalStatus.Tamamlandi, CikisOfisi = "SubeA",
            BasTar = At(0, 0), BitTar = At(0, 12), GercekDonusTar = At(0, 12), CreatedAtUtc = At(0, 0),
        };
        var r2 = new RentalContract
        {
            SozlesmeNo = "KS-HGS-2", MusteriId = b, VehicleId = v1, Durum = RentalStatus.Kirada, CikisOfisi = "SubeA",
            BasTar = At(2, 10), BitTar = At(3, 10), CreatedAtUtc = At(2, 10), CikisKm = 1000, CikisYakit = 4,
        };
        var r3 = new RentalContract
        {
            SozlesmeNo = "KS-HGS-3", MusteriId = b, VehicleId = v2, Durum = RentalStatus.Tamamlandi, CikisOfisi = "SubeB",
            BasTar = At(-1, 8), BitTar = At(-1, 20), GercekDonusTar = At(-1, 20), CreatedAtUtc = At(-1, 8),
        };
        var cancelled = new RentalContract
        {
            SozlesmeNo = "KS-HGS-IPT", MusteriId = a, VehicleId = v1, Durum = RentalStatus.Iptal, CikisOfisi = "SubeA",
            BasTar = At(1, 0), BitTar = At(1, 23), CreatedAtUtc = At(1, 0),
        };
        db.Rentals.AddRange(r1, r2, r3, cancelled);
        await db.SaveChangesAsync();
        return new World(tenant, a, b, v1, v2, r1.Id, r2.Id, r3.Id);
    }

    /// <summary>
    /// Elle kurulmuş döküm (noktalı virgül, Türkçe sayı/tarih). Satır no = dosya satırı (başlık 1).
    /// 2: D-1 23:30 → R1 başlangıcından (D 00:00) ÖNCE → kira dışı (UTC okunsaydı eşleşirdi).
    /// 3: D 00:00:00 tam teslim anı → R1 (başlangıç DAHİL).
    /// 4: D 11:00 → R1 (UTC okunsaydı dönüşten sonra kalırdı).
    /// 5: D 12:00 tam dönüş anı → kira dışı (bitiş HARİÇ).
    /// 6: D+2 10:00 açık kira R2'nin teslim anı → R2. Plaka tireli yazılmış.
    /// 7: D+3 15:45 → açık R2 (planlanan bitişten sonra da kira sürüyor).
    /// 8: D-1 19:59:59 → R3 (V2).
    /// 9: filoda olmayan plaka → kira dışı + araç bulunamadı.
    /// 10: "1.250" belirsiz → hatalı. 11: saatsiz tarih → hatalı. 12: 4. satırın aynısı → dosya içi mükerrer.
    /// D+1 (iptal kira günü) hiçbir satırda yok; iptal kira pencere üretmemeli (13. satır: D+1 12:00 → kira dışı).
    /// </summary>
    internal static string ReferenceCsv() => string.Join("\n",
        "Plaka;HGS Etiket No;Giriş İstasyonu;Çıkış İstasyonu;Geçiş Tarihi;Tutar",
        $"34 HGS 01;E1;Çamlıca;Gebze;{Wall(-1, 23, 30)};47,25",
        $"34HGS01;E1;Çamlıca;Gebze;{Wall(0, 0)};52,50",
        $"34 HGS 01;E1;İzmit;Bursa;{Wall(0, 11)};995,00",
        $"34 hgs 01;E1;Çamlıca;Gebze;{Wall(0, 12)};18,75",
        $"34-HGS-01;E1;Kınalı;Edirne;{Wall(2, 10)};30,00",
        $"34 HGS 01;E1;;Avrasya Tüneli;{Wall(3, 15, 45, 0, "dd.MM.yyyy HH:mm")};12,40",
        $"06 HGS 02;E2;Ankara;Polatlı;{Wall(-1, 19, 59, 59)};60,00",
        $"99 ZZ 999;E9;Çamlıca;Gebze;{Wall(0, 9)};15,00",
        $"34 HGS 01;E1;Çamlıca;Gebze;{Wall(0, 10)};1.250",
        $"34 HGS 01;E1;Çamlıca;Gebze;{D.ToString("dd.MM.yyyy", Inv)};20,00",
        $"34 HGS 01;E1;İzmit;Bursa;{Wall(0, 11)};995,00",
        $"34 HGS 01;E1;Çamlıca;Gebze;{Wall(1, 12)};9,10");

    private static IReadOnlyList<HgsRawRow> Rows(byte[] file, string name = "gecisler.csv",
        IReadOnlyDictionary<string, string?>? mapping = null)
    {
        var parsed = ImportService.ParseWithHeaders(new MemoryStream(file), name,
            new ImportParseOptions(InvariantCells: true, Windows1254Fallback: true));
        var (_, keys) = HgsImportColumns.Resolve(parsed.Headers, mapping);
        HgsImportColumns.RequireMandatory(keys);
        return HgsImportColumns.Extract(parsed.Rows, keys);
    }

    private static IReadOnlyList<HgsRawRow> Rows(string csv) => Rows(Encoding.UTF8.GetBytes(csv));

    private static HgsCrossingService Svc(IServiceScope s) => s.ServiceProvider.GetRequiredService<HgsCrossingService>();

    private static async Task<List<HgsCrossing>> AllAsync(IServiceScope s)
    {
        await using var db = await s.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await db.HgsGecisleri.AsNoTracking().OrderBy(c => c.GecisZamani).ToListAsync();
    }

    private static async Task<List<AccountLedgerEntry>> HgsLedgerAsync(IServiceScope s)
    {
        await using var db = await s.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await db.AccountLedgerEntries.AsNoTracking().Where(e => e.SourceType == "Hgs").ToListAsync();
    }

    private static HgsCrossing At(IEnumerable<HgsCrossing> rows, DateTimeOffset at, decimal amount)
        => Assert.Single(rows, c => c.GecisZamani == at && c.Tutar == amount);

    // ============================================================== içe aktarma + eşleştirme

    [Fact]
    public async Task Preview_writes_nothing_and_import_matches_the_hand_built_file()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);

        var preview = await Svc(s).PreviewAsync(Rows(ReferenceCsv()));
        Assert.Equal(12, preview.Toplam);
        Assert.Equal(5, preview.Eslesen);     // 3, 4, 6, 7, 8
        Assert.Equal(4, preview.KiraDisi);    // 2, 5, 9, 13
        Assert.Equal(1, preview.AracBulunamadi); // 9
        Assert.Equal(1, preview.Mukerrer);    // 12
        Assert.Equal(2, preview.Hatali);      // 10, 11
        Assert.Equal(0, preview.Belirsiz);
        Assert.Equal(0, preview.Eklenen);
        Assert.Equal(52.50m + 995.00m + 30.00m + 12.40m + 60.00m, preview.EslesenTutar); // 1.149,90
        Assert.Equal(47.25m + 18.75m + 15.00m + 9.10m, preview.KiraDisiTutar);           // 90,10
        Assert.Empty(await AllAsync(s));

        // Hatalı satırlar önce, mesajlı.
        var ambiguous = Assert.Single(preview.Satirlar, r => r.SatirNo == 10);
        Assert.Equal(HgsImportRowStatus.Hatali, ambiguous.Durum);
        Assert.Contains("belirsiz", ambiguous.Hata);
        Assert.Contains("saat", Assert.Single(preview.Satirlar, r => r.SatirNo == 11).Hata);
        Assert.Equal(HgsImportRowStatus.Mukerrer, Assert.Single(preview.Satirlar, r => r.SatirNo == 12).Durum);
        Assert.Equal("KS-HGS-1", Assert.Single(preview.Satirlar, r => r.SatirNo == 3).SozlesmeNo);

        var result = await Svc(s).ImportAsync(Rows(ReferenceCsv()), "gecisler.csv");
        Assert.Equal(9, result.Eklenen);
        Assert.NotNull(result.IceAktarimId);

        var all = await AllAsync(s);
        Assert.Equal(9, all.Count);
        // Gece yarısı: D-1 23:30 İstanbul = D-1 20:30Z < R1 teslimi (D-1 21:00Z) → kira dışı.
        var beforeMidnight = At(all, At(-1, 23, 30), 47.25m);
        Assert.Equal(HgsCrossingStatus.KiraDisi, beforeMidnight.Durum);
        Assert.Null(beforeMidnight.RentalId);
        Assert.Equal(w.V1, beforeMidnight.VehicleId);
        // Teslim anı dahil.
        Assert.Equal(w.R1, At(all, At(0, 0), 52.50m).RentalId);
        // Saat sınırı: D 11:00 İstanbul = D 08:00Z < dönüş (D 09:00Z) → R1.
        Assert.Equal(w.R1, At(all, At(0, 11), 995.00m).RentalId);
        // Dönüş anı hariç.
        Assert.Equal(HgsCrossingStatus.KiraDisi, At(all, At(0, 12), 18.75m).Durum);
        // Açık kira: teslim anı + planlanan bitişten sonra.
        Assert.Equal(w.R2, At(all, At(2, 10), 30.00m).RentalId);
        Assert.Equal(w.R2, At(all, At(3, 15, 45), 12.40m).RentalId);
        Assert.Equal(w.R3, At(all, At(-1, 19, 59, 59), 60.00m).RentalId);
        // İptal kira pencere üretmez.
        Assert.Equal(HgsCrossingStatus.KiraDisi, At(all, At(1, 12), 9.10m).Durum);
        var unknown = Assert.Single(all, c => c.Plaka == "99ZZ999");
        Assert.Null(unknown.VehicleId);
        Assert.Equal(HgsCrossingStatus.KiraDisi, unknown.Durum);
        // Plaka anahtarı tek kural: "34-HGS-01", "34 hgs 01", "34HGS01" aynı araç.
        Assert.All(all.Where(c => c.Plaka == "34HGS01"), c => Assert.Equal(w.V1, c.VehicleId));
        Assert.Equal("CAMLICA>GEBZE", beforeMidnight.IstasyonAnahtari);
        Assert.Equal("Çamlıca", beforeMidnight.GirisIstasyonu);
    }

    [Fact]
    public async Task Importing_the_same_file_twice_keeps_a_single_record_per_crossing()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);

        Assert.Equal(9, (await Svc(s).ImportAsync(Rows(ReferenceCsv()), "a.csv")).Eklenen);
        var again = await Svc(s).ImportAsync(Rows(ReferenceCsv()), "a.csv");
        Assert.Equal(0, again.Eklenen);
        Assert.Equal(10, again.Mukerrer); // 9 DB'de + 1 dosya içi
        Assert.Equal(2, again.Hatali);
        Assert.Null(again.IceAktarimId);
        Assert.Equal(9, (await AllAsync(s)).Count);

        // İstasyon yazımı farklı (büyük harf, Türkçe karakter) ama aynı geçiş → yine mükerrer.
        var variant = string.Join("\n", "PLAKA;GİRİŞ İSTASYONU;ÇIKIŞ İSTASYONU;GEÇİŞ TARİHİ;GEÇİŞ ÜCRETİ",
            $"34 HGS 01;ÇAMLICA;GEBZE;{Wall(-1, 23, 30)};47.25");
        var r = await Svc(s).ImportAsync(Rows(variant), "b.csv");
        Assert.Equal(0, r.Eklenen);
        Assert.Equal(1, r.Mukerrer);

        // DB son savunma: kilidi atlayan doğrudan ekleme unique index'e takılır.
        await using var db = await s.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var copy = (await AllAsync(s)).First();
        db.HgsGecisleri.Add(new HgsCrossing
        {
            Plaka = copy.Plaka, GecisZamani = copy.GecisZamani, IstasyonAnahtari = copy.IstasyonAnahtari, Tutar = copy.Tutar,
            Durum = HgsCrossingStatus.KiraDisi, IceAktarimId = Guid.NewGuid(), CreatedAtUtc = TestZaman.Now(),
        });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("IX_HgsGecisleri_DogalAnahtar", ex.InnerException!.Message);
    }

    [Fact]
    public async Task Concurrent_imports_of_the_same_file_insert_each_crossing_once()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await SetUpAsync(host);
        using var s1 = host.ScopeFor(w.Tenant);
        using var s2 = host.ScopeFor(w.Tenant);
        var rows = Rows(ReferenceCsv());
        var results = await Task.WhenAll(
            Task.Run(() => Svc(s1).ImportAsync(rows, "x.csv")),
            Task.Run(() => Svc(s2).ImportAsync(rows, "x.csv")));
        Assert.Equal(9, results.Sum(r => r.Eklenen));
        Assert.Equal(9, (await AllAsync(s1)).Count);
    }

    // ============================================================== yansıtma (PARA)

    [Fact]
    public async Task Reflect_posts_balanced_ledger_per_crossing_and_is_idempotent()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);
        await Svc(s).ImportAsync(Rows(ReferenceCsv()), "a.csv");
        var all = await AllAsync(s);
        var r1Rows = all.Where(c => c.RentalId == w.R1).Select(c => c.Id).ToList();
        Assert.Equal(2, r1Rows.Count);
        var outside = At(all, At(-1, 23, 30), 47.25m).Id;

        var result = await Svc(s).ReflectAsync([.. r1Rows, outside], 1.03m);
        Assert.Equal(2, result.Yansitilan);
        Assert.Equal(0, result.ZatenYansitilmis);
        Assert.Equal(1, result.Uygunsuz);          // kira dışı satır borçlandırılmaz
        Assert.Equal(1047.50m, result.ToplamGecis); // 52,50 + 995,00
        Assert.Equal(1078.93m, result.YansitilanTutar); // 54,08 + 1.024,85

        var ledger = await HgsLedgerAsync(s);
        Assert.Equal(4, ledger.Count);
        foreach (var set in ledger.GroupBy(e => e.SourceId))
        {
            Assert.Contains(set.Key, r1Rows);
            Assert.Equal(set.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase),
                set.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase));
            var debit = Assert.Single(set, e => e.Direction == LedgerDirection.Debit);
            Assert.Equal(LedgerAccountType.Cari, debit.AccountType);
            Assert.Equal(w.CustomerA, debit.AccountRef);
            Assert.Equal(LedgerAccountType.Gelir, Assert.Single(set, e => e.Direction == LedgerDirection.Credit).AccountType);
        }
        Assert.Contains(ledger, e => e.Amount.Amount == 54.08m);
        Assert.Contains(ledger, e => e.Amount.Amount == 1024.85m);
        var cash = s.ServiceProvider.GetRequiredService<CashService>();
        Assert.Equal(1078.93m, await cash.GetAccountBalanceAsync(w.CustomerA));

        var reflected = (await AllAsync(s)).Where(c => r1Rows.Contains(c.Id)).ToList();
        Assert.All(reflected, c =>
        {
            Assert.Equal(HgsCrossingStatus.Yansitildi, c.Durum);
            Assert.Equal(1.03m, c.HizmetOrani);
            Assert.Equal(w.CustomerA, c.YansitilanCariId);
        });

        // Tekrar (aynı ya da farklı oranla): yeni para YOK.
        var again = await Svc(s).ReflectAsync(r1Rows, 1.10m);
        Assert.Equal(0, again.Yansitilan);
        Assert.Equal(2, again.ZatenYansitilmis);
        Assert.Equal(4, (await HgsLedgerAsync(s)).Count);
        Assert.Equal(1078.93m, await cash.GetAccountBalanceAsync(w.CustomerA));

        // Yansıtılmış satır DB'de değişmez (trigger) — tutarı/durumu elle oynanamaz.
        await using var db = await s.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        await Assert.ThrowsAnyAsync<Exception>(() =>
            db.HgsGecisleri.Where(c => c.Id == r1Rows[0]).ExecuteUpdateAsync(u => u.SetProperty(c => c.Durum, HgsCrossingStatus.Eslesti)));
        await Assert.ThrowsAnyAsync<Exception>(() => db.HgsGecisleri.Where(c => c.Id == r1Rows[0]).ExecuteDeleteAsync());
    }

    [Fact]
    public async Task Concurrent_reflection_of_the_same_crossings_posts_once()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await SetUpAsync(host);
        using var s1 = host.ScopeFor(w.Tenant);
        using var s2 = host.ScopeFor(w.Tenant);
        await Svc(s1).ImportAsync(Rows(ReferenceCsv()), "a.csv");
        var r2Rows = (await AllAsync(s1)).Where(c => c.RentalId == w.R2).Select(c => c.Id).ToList();
        Assert.Equal(2, r2Rows.Count);

        var results = await Task.WhenAll(
            Task.Run(() => Svc(s1).ReflectAsync(r2Rows, 1.00m)),
            Task.Run(() => Svc(s2).ReflectAsync(r2Rows, 1.00m)));
        Assert.Equal(2, results.Sum(r => r.Yansitilan));
        Assert.Equal(2, results.Sum(r => r.ZatenYansitilmis));
        Assert.Equal(4, (await HgsLedgerAsync(s1)).Count);
        // 30,00 + 12,40 (oran 1,00)
        Assert.Equal(42.40m, await s1.ServiceProvider.GetRequiredService<CashService>().GetAccountBalanceAsync(w.CustomerB));
    }

    [Fact]
    public async Task Reflect_revalidates_the_rental_under_lock()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);
        await Svc(s).ImportAsync(Rows(ReferenceCsv()), "a.csv");
        var all = await AllAsync(s);
        var r2Rows = all.Where(c => c.RentalId == w.R2).Select(c => c.Id).ToList();
        var r1Late = At(all, At(0, 11), 995.00m).Id;

        await using (var db = await s.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            // R2 sonradan iptal edildi; R1'in dönüşü 10:00'a düzeltildi (11:00 geçişi artık aralık dışında).
            await db.Rentals.Where(r => r.Id == w.R2).ExecuteUpdateAsync(u => u.SetProperty(r => r.Durum, RentalStatus.Iptal));
            await db.Rentals.Where(r => r.Id == w.R1).ExecuteUpdateAsync(u => u.SetProperty(r => r.GercekDonusTar, At(0, 10)));
        }
        var result = await Svc(s).ReflectAsync([.. r2Rows, r1Late], 1.03m);
        Assert.Equal(0, result.Yansitilan);
        Assert.Equal(3, result.Uygunsuz);
        Assert.Empty(await HgsLedgerAsync(s));

        // Yeniden eşleştirme güncel kira verisiyle durumları düzeltir.
        var changed = await Svc(s).RematchAsync();
        Assert.Equal(3, changed);
        var after = await AllAsync(s);
        Assert.All(after.Where(c => r2Rows.Contains(c.Id) || c.Id == r1Late), c => Assert.Equal(HgsCrossingStatus.KiraDisi, c.Durum));
    }

    [Fact]
    public async Task Rematch_links_crossings_once_the_vehicle_and_rental_exist_and_never_touches_reflected_rows()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);
        await Svc(s).ImportAsync(Rows(ReferenceCsv()), "a.csv");
        var r3Row = Assert.Single(await AllAsync(s), c => c.RentalId == w.R3).Id;
        await Svc(s).ReflectAsync([r3Row], 1.00m);

        var vehicle = await s.ServiceProvider.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "99 ZZ 999", Sube = "SubeA" });
        await using (var db = await s.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            db.Rentals.Add(new RentalContract
            {
                SozlesmeNo = "KS-HGS-4", MusteriId = w.CustomerA, VehicleId = vehicle, Durum = RentalStatus.Tamamlandi,
                CikisOfisi = "SubeA", BasTar = At(0, 8), BitTar = At(0, 18), GercekDonusTar = At(0, 18), CreatedAtUtc = At(0, 8),
            });
            await db.SaveChangesAsync();
        }
        Assert.Equal(1, await Svc(s).RematchAsync());
        var linked = Assert.Single(await AllAsync(s), c => c.Plaka == "99ZZ999");
        Assert.Equal(HgsCrossingStatus.Eslesti, linked.Durum);
        Assert.Equal(vehicle, linked.VehicleId);
        Assert.Equal(HgsCrossingStatus.Yansitildi, Assert.Single(await AllAsync(s), c => c.Id == r3Row).Durum);
        Assert.Equal(0, await Svc(s).RematchAsync()); // değişiklik yoksa sıfır
    }

    // ============================================================== yetki + izolasyon

    [Fact]
    public async Task Permissions_branch_restricted_users_cannot_import_and_operators_cannot_reflect()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await SetUpAsync(host);
        using (var admin = host.ScopeFor(w.Tenant)) await Svc(admin).ImportAsync(Rows(ReferenceCsv()), "a.csv");

        using var op = host.ScopeFor(w.Tenant, role: UserRole.Operator, assignedBranch: "SubeA");
        await Assert.ThrowsAsync<NoPermissionException>(() => Svc(op).PreviewAsync(Rows(ReferenceCsv())));
        await Assert.ThrowsAsync<NoPermissionException>(() => Svc(op).ImportAsync(Rows(ReferenceCsv()), "a.csv"));
        await Assert.ThrowsAsync<NoPermissionException>(() => Svc(op).RematchAsync());
        var any = (await AllAsync(op)).Select(c => c.Id).Take(1).ToList();
        await Assert.ThrowsAsync<NoPermissionException>(() => Svc(op).ReflectAsync(any, 1m));

        // Muhasebe (FinanceWrite, şubesiz) içe aktarabilir ve yansıtabilir.
        using var accounting = host.ScopeFor(w.Tenant, role: UserRole.Muhasebe);
        Assert.Equal(0, (await Svc(accounting).PreviewAsync(Rows(ReferenceCsv()))).Eslesen); // hepsi zaten içeride
        await Assert.ThrowsAsync<ValidationException>(() => Svc(accounting).ReflectAsync(any, 2.5m)); // oran sınırı
        await Assert.ThrowsAsync<ValidationException>(() => Svc(accounting).ReflectAsync(any, 0.99m));
    }

    [Fact]
    public async Task Tenant_isolation_crossings_and_ledger_are_invisible_to_another_tenant()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await SetUpAsync(host);
        using (var s = host.ScopeFor(w.Tenant)) await Svc(s).ImportAsync(Rows(ReferenceCsv()), "a.csv");
        List<Guid> ids;
        using (var s = host.ScopeFor(w.Tenant)) ids = (await AllAsync(s)).Select(c => c.Id).ToList();

        var other = await SetUpAsync(host); // aynı plakalar, ayrı kiracı
        using var o = host.ScopeFor(other.Tenant);
        Assert.Empty(await Svc(o).ListAsync(new HgsCrossingFilter()));
        Assert.Empty(await Svc(o).FindManyAsync(ids));
        await using (var db = await o.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
            Assert.Equal(0, await db.HgsGecisleri.IgnoreQueryFilters().CountAsync()); // RLS (racar_app)

        // Başka kiracının kimlikleriyle yansıtma hiçbir şey yazmaz.
        var r = await Svc(o).ReflectAsync(ids.Take(3).ToList(), 1m);
        Assert.Equal(0, r.Yansitilan);
        Assert.Equal(3, r.Uygunsuz);
        Assert.Empty(await HgsLedgerAsync(o));

        // Aynı dosya diğer kiracıda bağımsız içe aktarılır (doğal anahtar kiracı başına).
        Assert.Equal(9, (await Svc(o).ImportAsync(Rows(ReferenceCsv()), "a.csv")).Eklenen);

        // Katalog: ENABLE + FORCE RLS; kiracı GUC'u yokken racar_app hiçbir satır görmez (varsayılan ret).
        await using var conn = new Npgsql.NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();
        await using (var cmd = new Npgsql.NpgsqlCommand(
            "select relrowsecurity, relforcerowsecurity from pg_class where relname = 'HgsGecisleri'", conn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.GetBoolean(0), "ENABLE ROW LEVEL SECURITY yok");
            Assert.True(reader.GetBoolean(1), "FORCE ROW LEVEL SECURITY yok");
        }
        await using (var count = new Npgsql.NpgsqlCommand("select count(*) from \"HgsGecisleri\"", conn))
            Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);

        // Doğal anahtar ihlali "mükerrer GÖNDERİM" (409) sınıfına girmez — depo kilit altında eler.
        Assert.False(RentACar.Infrastructure.Persistence.IdempotencyConstraint.IsDuplicateConstraint("IX_HgsGecisleri_DogalAnahtar"));
    }

    // ============================================================== dosya biçimleri

    [Fact]
    public async Task Xlsx_with_typed_date_time_and_number_cells_is_read_culture_independently()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Geçişler");
        ws.Cell(1, 1).Value = "Araç Plakası";
        ws.Cell(1, 2).Value = "Geçiş Tarihi"; // yalın "Tarih" otomatik eşlenmez (#407 Q6)
        ws.Cell(1, 3).Value = "Saat";
        ws.Cell(1, 4).Value = "Geçiş Ücreti";
        ws.Cell(1, 5).Value = "Çıkış Noktası";
        ws.Cell(2, 1).Value = "34 HGS 01";
        ws.Cell(2, 2).Value = D.ToDateTime(TimeOnly.MinValue);   // tarih hücresi (saat 00:00)
        ws.Cell(2, 3).Value = new TimeSpan(11, 0, 0);            // saat hücresi
        ws.Cell(2, 4).Value = 47.25;                             // sayı hücresi
        ws.Cell(2, 5).Value = "Gebze";
        ws.Cell(3, 1).Value = "34 HGS 01";
        ws.Cell(3, 2).Value = D.AddDays(-1).ToDateTime(new TimeOnly(23, 30)); // tarih-saat tek hücre
        ws.Cell(3, 4).Value = 1250;                              // tam sayı: belirsizlik yok
        ws.Cell(3, 5).Value = "Gebze";
        using var ms = new MemoryStream();
        wb.SaveAs(ms);

        var preview = await Svc(s).PreviewAsync(Rows(ms.ToArray(), "gecisler.xlsx"));
        Assert.Equal(0, preview.Hatali);
        var matched = Assert.Single(preview.Satirlar, r => r.Durum == HgsImportRowStatus.Eslesti);
        Assert.Equal(At(0, 11), matched.GecisZamani);
        Assert.Equal(47.25m, matched.Tutar);
        Assert.Equal(w.R1, matched.RentalId);
        var outside = Assert.Single(preview.Satirlar, r => r.Durum == HgsImportRowStatus.KiraDisi);
        Assert.Equal(At(-1, 23, 30), outside.GecisZamani);
        Assert.Equal(1250m, outside.Tutar);
    }

    [Fact]
    public async Task Windows1254_csv_and_user_column_mapping()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);

        // Türkçe Excel'in "CSV" kaydı Windows-1254; başlıklar takma adlarda YOK → kullanıcı eşler.
        var csv = string.Join("\r\n", "Araç;Ödeme Zamanı;Gişe Adı;Kesilen",
            $"34 HGS 01;{Wall(0, 11, 0, 0, "dd/MM/yyyy HH:mm")};Osmangazi Köprüsü;1.250,00");
        var bytes = CodePagesEncoding().GetBytes(csv);
        Assert.Throws<ValidationException>(() => Rows(bytes)); // öneri zorunlu alanları bulamaz

        var mapping = new Dictionary<string, string?>
        {
            ["plaka"] = "Araç", ["gecisZamani"] = "Ödeme Zamanı", ["cikisIstasyonu"] = "Gişe Adı", ["tutar"] = "Kesilen",
        };
        var preview = await Svc(s).PreviewAsync(Rows(bytes, mapping: mapping));
        var row = Assert.Single(preview.Satirlar);
        Assert.Equal(HgsImportRowStatus.Eslesti, row.Durum);
        Assert.Equal(1250.00m, row.Tutar);
        Assert.Equal("Osmangazi Köprüsü", row.CikisIstasyonu);

        // Dosyada olmayan sütun ve aynı sütunu iki alana vermek reddedilir.
        Assert.Throws<ValidationException>(() => Rows(bytes, mapping: new Dictionary<string, string?> { ["plaka"] = "Yok" }));
        Assert.Throws<ValidationException>(() => Rows(bytes, mapping: new Dictionary<string, string?>
        {
            ["plaka"] = "Araç", ["etiketNo"] = "Araç", ["gecisZamani"] = "Ödeme Zamanı", ["tutar"] = "Kesilen",
        }));
    }

    private static Encoding CodePagesEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1254);
    }

    // ============================================================== saf kurallar

    [Theory]
    [InlineData("47,25", "47.25")]
    [InlineData("47.25", "47.25")]
    [InlineData("1.250,00", "1250")]
    [InlineData("1250", "1250")]
    [InlineData("1,250.50", "1250.50")]
    [InlineData("₺47,25", "47.25")]
    [InlineData("47,25 TL", "47.25")]
    [InlineData("125.000,50", "125000.50")]
    [InlineData("47.2500", "47.25")]
    public void Amount_parsing_turkish_and_invariant(string raw, string expected)
        => Assert.Equal(decimal.Parse(expected, Inv), HgsValueParser.ParseAmount(raw));

    [Theory]
    [InlineData("1.250")]   // binlik mi ondalık mı belirsiz
    [InlineData("1,250")]
    [InlineData("12,345")]
    [InlineData("0")]
    [InlineData("-12,50")]
    [InlineData("12,3456")] // kuruştan küçük hane
    [InlineData("abc")]
    [InlineData("1.250.000,00")] // tek geçiş için makul sınır
    public void Amount_parsing_rejects_ambiguous_and_invalid(string raw)
        => Assert.Throws<ValidationException>(() => HgsValueParser.ParseAmount(raw));

    [Fact]
    public void Matcher_is_half_open_and_flags_overlaps()
    {
        var start = TestZaman.Now().AddDays(-3);
        var end = start.AddHours(10);
        var a = new HgsRentalWindow(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "A", start, end);
        var next = new HgsRentalWindow(Guid.NewGuid(), a.VehicleId, Guid.NewGuid(), "B", end, null);
        Assert.Equal(a.RentalId, HgsCrossingMatcher.Match(start, [a, next]).Rental?.RentalId);
        Assert.Equal(next.RentalId, HgsCrossingMatcher.Match(end, [a, next]).Rental?.RentalId); // art arda: sonraki
        Assert.Equal(HgsCrossingStatus.KiraDisi, HgsCrossingMatcher.Match(start.AddSeconds(-1), [a]).Status);
        var overlap = a with { RentalId = Guid.NewGuid(), Start = start.AddHours(1) };
        Assert.Equal(HgsCrossingStatus.Belirsiz, HgsCrossingMatcher.Match(start.AddHours(2), [a, overlap]).Status);
    }

    [Fact]
    public void Future_and_dateless_instants_are_rejected()
    {
        var now = TestZaman.Now();
        var tomorrow = new DateTimeOffset(now.UtcDateTime.AddDays(1), TimeSpan.Zero).ToOffset(Istanbul);
        Assert.Throws<ValidationException>(() =>
            HgsValueParser.ParseInstant(tomorrow.ToString("dd.MM.yyyy HH:mm", Inv), null, now));
        Assert.Throws<ValidationException>(() => HgsValueParser.ParseInstant(Wall(0, 0, 0, 0, "dd.MM.yyyy"), null, now));
        // Ayrı saat sütunu tarihle birleşir; açık ofsetli ISO değer ofsetiyle okunur.
        Assert.Equal(At(0, 8, 15), HgsValueParser.ParseInstant(Wall(0, 0, 0, 0, "dd.MM.yyyy"), "08:15", now));
        Assert.Equal(At(0, 8, 15), HgsValueParser.ParseInstant(At(0, 8, 15).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", Inv), null, now));
    }
}
