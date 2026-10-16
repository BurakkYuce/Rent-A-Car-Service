using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Application.Hgs;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Import;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR #391 BAĞIMSIZ ADVERSARIAL PROBE'LARI (HGS içe aktarma + eşleştirme + yansıtma) — bulgular düzeltildi, probe'lar
/// KALICI kilit. Beklenen değerler elle kurulmuş senaryodan; İstanbul = sabit UTC+3.
/// <para>M1 (P1/P1b): biçimi farklı iki dökümdeki aynı geçiş → ikinci satır "olası mükerrer" (Belirsiz), tek borç.
/// M2 (P2a/P2b): xlsx'te saatsiz tarih hücresi 00:00 kabul edilmez. M3 (P3/P3b): yansıtılmış HGS'li kira iptal
/// edilemez, yansıtılmış geçişten önceye dönüş girilemez. L1 (P6): eski dönem-MD5 yolu silindi. L2 (P7): bozuk
/// binlik gruplama red. L3 (P8): teslim edilmemiş açık kira Belirsiz. P4 (Blazor dönemi duvar saati = UTC kira
/// anları) bir VERİ sorunudur — <c>fix/blazor-saat-kaymasi</c> PR'ında; HGS canlıda o düzeltme olmadan açılmaz.</para>
/// </summary>
[Collection("postgres")]
public sealed class HgsAdversarialProbeTests(PostgresFixture fx)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly TimeSpan Istanbul = TimeSpan.FromHours(3);
    private static readonly DateOnly D = DateOnly.FromDateTime(DateTime.UtcNow.Add(Istanbul)).AddDays(-5);

    private static string Wall(int day, int h, int m = 0, int s = 0, string format = "dd.MM.yyyy HH:mm:ss")
        => D.AddDays(day).ToDateTime(new TimeOnly(h, m, s)).ToString(format, Inv);

    private static IReadOnlyList<HgsRawRow> Rows(byte[] file, string name = "g.csv",
        IReadOnlyDictionary<string, string?>? mapping = null)
    {
        var parsed = ImportService.ParseWithHeaders(new MemoryStream(file), name,
            new ImportParseOptions(InvariantCells: true, Windows1254Fallback: true));
        var (_, keys) = HgsImportColumns.Resolve(parsed.Headers, mapping);
        HgsImportColumns.RequireMandatory(keys);
        return HgsImportColumns.Extract(parsed.Rows, keys);
    }

    private static IReadOnlyList<HgsRawRow> Csv(params string[] lines) => Rows(Encoding.UTF8.GetBytes(string.Join("\n", lines)));

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

    private static byte[] Xlsx(Action<IXLWorksheet> fill)
    {
        using var wb = new XLWorkbook();
        fill(wb.AddWorksheet("G"));
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ===================================================================== P1 — mükerrer: biçim farkı

    /// <summary>
    /// AYNI fiziksel geçiş: bankanın xlsx dökümünde saniyeli (D 11:00:37, tarih-saat hücresi), aynı dökümün Excel'de
    /// "CSV olarak kaydet" çıktısında görüntü biçimiyle saniyesiz ("dd.MM.yyyy HH:mm"). Doğal anahtar saniyeye bağlı →
    /// ikinci içe aktarma YENİ satır ekler, ikisi de R1'e "Eslesti" → ikisi de yansıtılır → müşteri A İKİ KEZ borçlanır.
    /// Beklenen (oracle): ikinci içe aktarma mükerrer sayar (ya da en azından otomatik yansıtılabilir olmaz);
    /// A'nın HGS borcu tek geçiş: 995,00 × 1,03 = 1.024,85.
    /// </summary>
    [Fact]
    public async Task P1_same_crossing_with_and_without_seconds_is_charged_once()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);

        var xlsx = Xlsx(ws =>
        {
            ws.Cell(1, 1).Value = "Plaka"; ws.Cell(1, 2).Value = "Geçiş Tarihi"; ws.Cell(1, 3).Value = "Tutar";
            ws.Cell(1, 4).Value = "Çıkış İstasyonu";
            ws.Cell(2, 1).Value = "34 HGS 01"; ws.Cell(2, 2).Value = D.ToDateTime(new TimeOnly(11, 0, 37));
            ws.Cell(2, 3).Value = 995.00; ws.Cell(2, 4).Value = "Bursa";
        });
        Assert.Equal(1, (await Svc(s).ImportAsync(Rows(xlsx, "banka.xlsx"), "banka.xlsx")).Eklenen);

        var second = await Svc(s).ImportAsync(Csv("Plaka;Geçiş Tarihi;Tutar;Çıkış İstasyonu",
            $"34 HGS 01;{Wall(0, 11, 0, 0, "dd.MM.yyyy HH:mm")};995,00;Bursa"), "banka.csv");

        // M1 kararı: ikinci satır saklanır ama "olası mükerrer" (Belirsiz) — otomatik yansıtılamaz.
        Assert.Equal(1, second.OlasiMukerrer);
        var all = await AllAsync(s);
        Assert.Equal(1, all.Count(c => c.Durum == HgsCrossingStatus.Eslesti));
        Assert.Equal(HgsResolution.NearDuplicateNote, Assert.Single(all, c => c.Durum == HgsCrossingStatus.Belirsiz).EslesmeNotu);

        // İkisini birden yansıtmak bile tek borç üretir (kilit altındaki aynı denetim).
        var result = await Svc(s).ReflectAsync(all.Select(c => c.Id).ToList(), 1.03m);
        var balance = await s.ServiceProvider.GetRequiredService<CashService>().GetAccountBalanceAsync(w.CustomerA);
        Assert.Equal(1, result.Yansitilan);
        Assert.Equal(1, result.Uygunsuz);
        Assert.True(balance == 1024.85m,
            $"İkinci dosya eklenen={second.Eklenen}, olası mükerrer={second.OlasiMukerrer}; tabloda {all.Count} satır → " +
            $"müşteri A HGS borcu {balance} (beklenen 1024,85).");

        // Yeniden eşleştirme ikincil satırı Eslesti'ye döndürmez (yansıtılmış kopyası var).
        await Svc(s).RematchAsync();
        Assert.Equal(HgsCrossingStatus.Belirsiz, Assert.Single(await AllAsync(s), c => c.Durum != HgsCrossingStatus.Yansitildi).Durum);
    }

    /// <summary>
    /// M1 — kilit altındaki ikinci savunma: olası mükerrer kopya, durum alanı bir şekilde "Eslesti" olsa bile (ör.
    /// kilidi almayan gelecekteki API yazıcısı), yansıtılmış kopyası olduğu için borçlandırılmaz.
    /// </summary>
    [Fact]
    public async Task P1d_near_duplicate_forced_to_matched_is_still_not_charged_under_lock()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);
        await Svc(s).ImportAsync(Csv("Plaka;Çıkış İstasyonu;Geçiş Tarihi;Tutar", $"34 HGS 01;Bursa;{Wall(0, 11, 0, 37)};995,00"), "a.csv");
        var first = Assert.Single(await AllAsync(s)).Id;
        Assert.Equal(1, (await Svc(s).ReflectAsync([first], 1.00m)).Yansitilan);
        await Svc(s).ImportAsync(Csv("Plaka;Giriş İstasyonu;Çıkış İstasyonu;Geçiş Tarihi;Tutar",
            $"34 HGS 01;İzmit;Bursa;{Wall(0, 11, 0, 0, "dd.MM.yyyy HH:mm")};995,00"), "b.csv");
        var copy = Assert.Single(await AllAsync(s), c => c.Id != first);
        Assert.Equal(HgsCrossingStatus.Belirsiz, copy.Durum);

        await using (var db = await s.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
            await db.HgsGecisleri.Where(c => c.Id == copy.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.Durum, HgsCrossingStatus.Eslesti));
        var r = await Svc(s).ReflectAsync([copy.Id], 1.00m);
        Assert.Equal(0, r.Yansitilan);
        Assert.Equal(1, r.Uygunsuz);
        Assert.Equal(995.00m, await s.ServiceProvider.GetRequiredService<CashService>().GetAccountBalanceAsync(w.CustomerA));
    }

    /// <summary>
    /// AYNI geçiş: ilk yüklemede kullanıcı giriş + çıkış istasyonunu eşledi, ikincisinde (başka döküm/eşleme) yalnız
    /// çıkışı. İstasyon anahtarı "IZMIT&gt;BURSA" ≠ "&gt;BURSA" → iki satır, ikisi de yansıtılabilir.
    /// </summary>
    [Fact]
    public async Task P1b_same_crossing_with_and_without_entry_station_is_matched_once()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);

        await Svc(s).ImportAsync(Csv("Plaka;Giriş İstasyonu;Çıkış İstasyonu;Geçiş Tarihi;Tutar",
            $"34 HGS 01;İzmit;Bursa;{Wall(0, 11)};995,00"), "a.csv");
        var second = await Svc(s).ImportAsync(Csv("Plaka;Çıkış İstasyonu;Geçiş Tarihi;Tutar",
            $"34 HGS 01;Bursa;{Wall(0, 11)};995,00"), "b.csv");

        // M1 kararı: ikinci satır saklanır ama olası mükerrer (Belirsiz) — R1'e yansıtılabilir TEK satır kalır.
        var eslesti = (await AllAsync(s)).Count(c => c.Durum == HgsCrossingStatus.Eslesti);
        Assert.Equal(1, second.OlasiMukerrer);
        Assert.True(eslesti == 1,
            $"İkinci dosya eklenen={second.Eklenen}; R1'e eşleşmiş yansıtılabilir satır={eslesti} (beklenen 1).");
    }

    /// <summary>Kontrol (yeşil beklenir): tutar biçimi ("995,00" / "995.00" / sayı hücresi) ve açık ofsetli ISO aynı anahtarı üretir.</summary>
    [Fact]
    public async Task P1c_control_amount_format_and_offset_iso_are_deduplicated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);

        await Svc(s).ImportAsync(Csv("Plaka;Çıkış İstasyonu;Geçiş Tarihi;Tutar", $"34 HGS 01;Bursa;{Wall(0, 11)};995,00"), "a.csv");
        var iso = HgsImportTests.At(0, 11).ToOffset(Istanbul).ToString("yyyy-MM-dd'T'HH:mm:sszzz", Inv);
        var r = await Svc(s).ImportAsync(Csv("Plaka;Çıkış İstasyonu;Geçiş Tarihi;Tutar",
            $"34hgs01;BURSA;{iso};995.00",
            $"34-HGS-01; bursa ;{Wall(0, 11, 0, 0, "yyyy-MM-dd HH:mm")};₺995"), "b.csv");
        Assert.Equal(0, r.Eklenen);
        Assert.Single(await AllAsync(s));
    }

    // ===================================================================== P2 — saatsiz tarih (xlsx)

    /// <summary>
    /// Kodun KENDİ kuralı (HgsValueParser.ParseInstant XML dokümanı): "Saat hiç yoksa (yalnız gün) satır reddedilir:
    /// ... gece yarısı varsaymak yanlış müşteriyi borçlandırır". CSV metninde ("15.10.2026") tutuyor; ama xlsx TARİH
    /// hücresi InvariantText'te "yyyy-MM-dd 00:00:00" olur ve saatli biçimle okunur → 00:00 kabul edilir. Tek satırlı
    /// dosyada "tüm zamanlar 00:00" uyarısı da çıkmaz (parsed.Count ≥ 2 şartı). Beklenen: satır HATALI.
    /// </summary>
    [Fact]
    public async Task P2a_xlsx_date_only_cell_without_time_column_is_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);

        var xlsx = Xlsx(ws =>
        {
            ws.Cell(1, 1).Value = "Plaka"; ws.Cell(1, 2).Value = "Geçiş Tarihi"; ws.Cell(1, 3).Value = "Tutar";
            ws.Cell(2, 1).Value = "34 HGS 01"; ws.Cell(2, 2).Value = D.ToDateTime(TimeOnly.MinValue); ws.Cell(2, 3).Value = 47.25;
        });
        var p = await Svc(s).PreviewAsync(Rows(xlsx, "g.xlsx"));
        var row = Assert.Single(p.Satirlar);
        Assert.True(row.Durum == HgsImportRowStatus.Hatali,
            $"Saatsiz xlsx tarih hücresi kabul edildi: durum={row.Durum}, an={row.GecisZamani:O}, kira={row.SozlesmeNo}, " +
            $"uyarılar=[{string.Join(" | ", p.Uyarilar)}]");
    }

    /// <summary>
    /// Tarih (xlsx tarih hücresi) + ayrı Saat sütunu; bir satırın Saat hücresi BOŞ. O satır sessizce 00:00 olur
    /// (diğer satır saatli olduğu için "tüm zamanlar 00:00" uyarısı da çıkmaz). Beklenen: o satır HATALI.
    /// </summary>
    [Fact]
    public async Task P2b_xlsx_empty_time_cell_on_one_row_is_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);

        var xlsx = Xlsx(ws =>
        {
            ws.Cell(1, 1).Value = "Plaka"; ws.Cell(1, 2).Value = "Tarih"; ws.Cell(1, 3).Value = "Saat"; ws.Cell(1, 4).Value = "Tutar";
            ws.Cell(2, 1).Value = "34 HGS 01"; ws.Cell(2, 2).Value = D.ToDateTime(TimeOnly.MinValue);
            ws.Cell(2, 3).Value = new TimeSpan(11, 0, 0); ws.Cell(2, 4).Value = 10.00;
            ws.Cell(3, 1).Value = "34 HGS 01"; ws.Cell(3, 2).Value = D.ToDateTime(TimeOnly.MinValue);
            /* Saat boş */ ws.Cell(3, 4).Value = 20.00;
        });
        var p = await Svc(s).PreviewAsync(Rows(xlsx, "g.xlsx"));
        var row = Assert.Single(p.Satirlar, r => r.SatirNo == 3);
        Assert.True(row.Durum == HgsImportRowStatus.Hatali,
            $"Saati boş satır kabul edildi: durum={row.Durum}, an={row.GecisZamani:O}, kira={row.SozlesmeNo}, uyarı sayısı={p.Uyarilar.Count}");
    }

    // ===================================================================== P3 — yansıtmadan sonra kira iptali

    /// <summary>
    /// R2 (açık, müşteri B) geçişleri yansıtıldı (B'ye 42,40 borç). Sonra kira İPTAL ediliyor. RentalService.CancelAsync
    /// fatura ve tahsilatı "defter ↔ sözleşme ıraksar" diye engelliyor ama HGS yansıtmasını bilmiyor → iptal geçer,
    /// B'nin cari borcu iptal edilmiş kira için kalır, geçiş satırı değişmez (trigger), modülde ters kayıt yolu yok,
    /// defterdeki 'Hgs' kısmi unique index'i aynı SourceId ile ters küme yazmayı da yapısal olarak engeller.
    /// Beklenen: iptal reddedilir (ya da yansıtma ters kayıtla geri alınır).
    /// </summary>
    [Fact]
    public async Task P3_rental_with_reflected_crossings_cannot_be_cancelled()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);
        await Svc(s).ImportAsync(Csv("Plaka;Çıkış İstasyonu;Geçiş Tarihi;Tutar",
            $"34 HGS 01;Kınalı;{Wall(2, 10)};30,00", $"34 HGS 01;Avrasya;{Wall(3, 15, 45)};12,40"), "a.csv");
        var r2 = (await AllAsync(s)).Where(c => c.RentalId == w.R2).Select(c => c.Id).ToList();
        Assert.Equal(2, r2.Count);
        Assert.Equal(2, (await Svc(s).ReflectAsync(r2, 1.00m)).Yansitilan);

        Exception? refused = null;
        try { await s.ServiceProvider.GetRequiredService<RentalService>().CancelAsync(w.R2); }
        catch (ValidationException ex) { refused = ex; }

        await using var db = await s.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var state = await db.Rentals.AsNoTracking().Where(r => r.Id == w.R2).Select(r => r.Durum).SingleAsync();
        var balance = await s.ServiceProvider.GetRequiredService<CashService>().GetAccountBalanceAsync(w.CustomerB);
        Assert.True(refused is not null,
            $"Yansıtılmış HGS'li kira iptal edildi: kira durumu={state}, müşteri B cari bakiyesi={balance} (iptal kiradan HGS borcu), " +
            $"geçişler={string.Join(",", (await AllAsync(s)).Where(c => r2.Contains(c.Id)).Select(c => c.Durum))}");
    }

    /// <summary>
    /// Açık kira (R2, müşteri B) teslim edildi; günlük döküm içe aktarılıp D+3 15:45 geçişi yansıtıldı. Operatör dönüşü
    /// geç giriyor: gerçek dönüş D+3 12:00. Dönüş KABUL edilir; 15:45 geçişi artık kira aralığının DIŞINDA ama B'de borç
    /// olarak kalır (satır değişmez, ters kayıt yolu yok). Beklenen: dönüş reddedilir ya da geçiş işaretlenir/ters kayıtla düzeltilir.
    /// </summary>
    [Fact]
    public async Task P3b_return_before_a_reflected_crossing_is_refused()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);
        var rentals = s.ServiceProvider.GetRequiredService<RentalService>(); // R2 SetUpAsync'te teslim edilmiş (KM 1000)
        await Svc(s).ImportAsync(Csv("Plaka;Çıkış İstasyonu;Geçiş Tarihi;Tutar", $"34 HGS 01;Avrasya;{Wall(3, 15, 45)};12,40"), "a.csv");
        var id = Assert.Single(await AllAsync(s)).Id;
        Assert.Equal(1, (await Svc(s).ReflectAsync([id], 1.00m)).Yansitilan);

        Exception? refused = null;
        try { await rentals.ReturnAsync(w.R2, 1200, 4, HgsImportTests.At(3, 12)); }
        catch (ValidationException ex) { refused = ex; }

        var row = Assert.Single(await AllAsync(s));
        var balance = await s.ServiceProvider.GetRequiredService<CashService>().GetAccountBalanceAsync(w.CustomerB);
        Assert.True(refused is not null || row.Durum != HgsCrossingStatus.Yansitildi,
            $"Dönüş D+3 12:00 kabul edildi; 15:45 geçişi {row.Durum}, B bakiyesi {balance} (kira dışı geçiş borcu).");
        // Geçiş ANINDA dönüş de red (yarı açık aralık: dönüş anı hariç); geçişten sonraki dönüş serbest.
        await Assert.ThrowsAsync<ValidationException>(() => rentals.ReturnAsync(w.R2, 1200, 4, HgsImportTests.At(3, 15, 45)));
        Assert.True(await rentals.ReturnAsync(w.R2, 1200, 4, HgsImportTests.At(3, 16)));
    }

    // P4 (Blazor dönemi: İstanbul duvar saatiyle girilmiş kira anları UTC etiketiyle saklanmış → eşleştirme 3 saat kayık)
    // bir VERİ sorunudur; kod değil. Kalıcı testi ve düzeltmesi fix/blazor-saat-kaymasi PR'ında (tek seferlik, geri
    // alınabilir veri migration'ı). HGS o düzeltme canlıya çıkmadan açılmaz (#391 PR açıklaması).

    // ===================================================================== P5 — eşzamanlılık (yeşil beklenir)

    /// <summary>8 paralel yansıtma + 2 yeniden eşleştirme + 2 içe aktarma aynı geçişler üzerinde → her geçiş tek küme.</summary>
    [Fact]
    public async Task P5_stress_parallel_reflect_rematch_import_posts_each_crossing_once()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using (var s0 = host.ScopeFor(w.Tenant)) await Svc(s0).ImportAsync(Rows(Encoding.UTF8.GetBytes(HgsImportTests.ReferenceCsv())), "a.csv");
        List<Guid> ids;
        using (var s0 = host.ScopeFor(w.Tenant)) ids = (await AllAsync(s0)).Select(c => c.Id).ToList();

        var scopes = Enumerable.Range(0, 12).Select(_ => host.ScopeFor(w.Tenant)).ToList();
        try
        {
            var tasks = new List<Task>();
            for (var i = 0; i < 8; i++) { var sc = scopes[i]; tasks.Add(Task.Run(() => Svc(sc).ReflectAsync(ids, 1.03m))); }
            tasks.Add(Task.Run(() => Svc(scopes[8]).RematchAsync()));
            tasks.Add(Task.Run(() => Svc(scopes[9]).RematchAsync()));
            tasks.Add(Task.Run(() => Svc(scopes[10]).ImportAsync(Rows(Encoding.UTF8.GetBytes(HgsImportTests.ReferenceCsv())), "b.csv")));
            tasks.Add(Task.Run(() => Svc(scopes[11]).ImportAsync(Rows(Encoding.UTF8.GetBytes(HgsImportTests.ReferenceCsv())), "c.csv")));
            await Task.WhenAll(tasks);

            var ledger = await HgsLedgerAsync(scopes[0]);
            Assert.Equal(10, ledger.Count); // 5 eşleşen geçiş × 2 bacak
            Assert.All(ledger.GroupBy(e => e.SourceId), g =>
            {
                Assert.Single(g, e => e.Direction == LedgerDirection.Debit);
                Assert.Single(g, e => e.Direction == LedgerDirection.Credit);
            });
            var cash = scopes[0].ServiceProvider.GetRequiredService<CashService>();
            // A: 52,50→54,08 + 995,00→1.024,85 = 1.078,93; B: 30,00→30,90 + 12,40→12,77 + 60,00→61,80 = 105,47
            Assert.Equal(1078.93m, await cash.GetAccountBalanceAsync(w.CustomerA));
            Assert.Equal(105.47m, await cash.GetAccountBalanceAsync(w.CustomerB));
            Assert.Equal(9, (await AllAsync(scopes[0])).Count);
        }
        finally { foreach (var sc in scopes) sc.Dispose(); }
    }

    // ===================================================================== P6 — eski dönem-MD5 yolu SİLİNDİ (L1)

    /// <summary>
    /// Eski <c>HgsReflectionService</c> (SourceId = dönem MD5) aynı geçişi yeni yoldan (SourceId = geçiş) ayrıca
    /// borçlandırabilirdi; defter index'i SourceId farklı olduğu için yakalayamazdı. Sınıf DI dahil silindi — geri
    /// gelirse bu çit kırılır. HGS'yi deftere yazan TEK yol <see cref="HgsCrossingService.ReflectAsync"/>.
    /// </summary>
    [Fact]
    public void P6_old_period_md5_reflection_path_no_longer_exists()
    {
        var application = typeof(HgsCrossingService).Assembly;
        Assert.Null(application.GetType("RentACar.Application.Hgs.HgsReflectionService"));
        Assert.Null(application.GetType("RentACar.Application.Hgs.HgsReflectionResult"));
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        Assert.DoesNotContain(application.GetTypes(), t => t.Namespace == "RentACar.Application.Hgs"
            && t.Name.Contains("Reflection", StringComparison.Ordinal) && t.Name != nameof(HgsReflectionPlan)
            && s.ServiceProvider.GetService(t) is not null);
    }

    // ===================================================================== P7 — bozuk binlik gruplama

    /// <summary>Gruplaması bozuk tutarlar (3'lü olmayan grup) sessizce binlik sayılıyor; beklenen: red.</summary>
    [Theory]
    [InlineData("1,250,50")]   // → 125050 kabul
    [InlineData("1.25.0")]     // → 1250 kabul
    [InlineData("12,50.00")]   // → 1250 kabul
    [InlineData("1.2.3,45")]   // → 123,45 kabul
    public void P7_malformed_thousands_grouping_is_rejected(string raw)
    {
        decimal? v = null;
        try { v = HgsValueParser.ParseAmount(raw); } catch (ValidationException) { }
        Assert.True(v is null, $"'{raw}' → {v} olarak kabul edildi.");
    }

    // ===================================================================== P9/P10 — dosya güvenliği / boyut

    /// <summary>Formüllü xlsx: ClosedXML <c>cell.Value</c> formülü HESAPLAR. 200 hücre × 1M satırlık dizi formülü.
    /// Eşik: 10 sn (tek istek). Eski yol (GetString) da ölçülür — fark yeni koda mı ait?</summary>
    [Fact]
    public void P9_formula_cells_in_xlsx_cpu_cost()
    {
        var xlsx = Xlsx(ws =>
        {
            ws.Cell(1, 1).Value = "Plaka"; ws.Cell(1, 2).Value = "Geçiş Tarihi"; ws.Cell(1, 3).Value = "Tutar";
            for (var r = 2; r <= 201; r++)
            {
                ws.Cell(r, 1).Value = "34 HGS 01";
                ws.Cell(r, 2).Value = Wall(0, 1, 0, r % 60);
                ws.Cell(r, 3).FormulaA1 = "SUMPRODUCT(ROW(A1:A1048576)*0)+47.25";
            }
        });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string? err = null;
        try { _ = Rows(xlsx, "f.xlsx"); } catch (Exception ex) { err = ex.GetType().Name + ": " + ex.Message; }
        var invariant = sw.Elapsed;
        sw.Restart();
        try { _ = ImportService.ParseWithHeaders(new MemoryStream(xlsx), "f.xlsx"); } catch (Exception) { }
        var legacy = sw.Elapsed;
        Assert.True(invariant < TimeSpan.FromSeconds(10),
            $"HGS ayrıştırma {invariant.TotalSeconds:0.0} sn (eski yol {legacy.TotalSeconds:0.0} sn), hata={err}");
    }

    /// <summary>20.000 satır (ImportLimits.MaxRows) önizleme süresi — eşik 15 sn.</summary>
    [Fact]
    public async Task P10_max_rows_preview_time()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);
        var lines = new List<string> { "Plaka;Çıkış İstasyonu;Geçiş Tarihi;Tutar" };
        var start = D.AddDays(-20).ToDateTime(TimeOnly.MinValue);
        for (var i = 0; i < 20_000; i++)
            lines.Add($"{(i % 2 == 0 ? "34 HGS 01" : "06 HGS 02")};Gişe {i % 7};{start.AddMinutes(i).ToString("dd.MM.yyyy HH:mm:ss", Inv)};{1 + i % 90},50");
        var rows = Csv([.. lines]);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var p = await Svc(s).PreviewAsync(rows);
        var previewTime = sw.Elapsed;
        sw.Restart();
        var imp = await Svc(s).ImportAsync(rows, "b.csv");
        var importTime = sw.Elapsed;
        sw.Restart();
        await Svc(s).RematchAsync();
        var rematchTime = sw.Elapsed;
        Assert.Equal(20_000, p.Toplam);
        Assert.True(previewTime < TimeSpan.FromSeconds(15) && importTime < TimeSpan.FromSeconds(30) && rematchTime < TimeSpan.FromSeconds(30),
            $"önizleme {previewTime.TotalSeconds:0.0} sn, içe aktarma {importTime.TotalSeconds:0.0} sn ({imp.Eklenen} satır), yeniden eşleştirme {rematchTime.TotalSeconds:0.0} sn");
    }

    /// <summary>Kontrol (yeşil beklenir): dönem bugüne kadar kapalıyken yansıtma reddedilir, defter ve satır değişmez.</summary>
    [Fact]
    public async Task P11_control_closed_period_blocks_reflection()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);
        await Svc(s).ImportAsync(Csv("Plaka;Çıkış İstasyonu;Geçiş Tarihi;Tutar", $"34 HGS 01;Gebze;{Wall(0, 11)};47,25"), "a.csv");
        var id = Assert.Single(await AllAsync(s)).Id;
        await s.ServiceProvider.GetRequiredService<RentACar.Application.Periods.PeriodLockService>().LockAsync(DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<ValidationException>(() => Svc(s).ReflectAsync([id], 1.03m));
        Assert.Empty(await HgsLedgerAsync(s));
        Assert.Equal(HgsCrossingStatus.Eslesti, Assert.Single(await AllAsync(s)).Durum);
    }

    // ===================================================================== P8 — teslim edilmemiş açık kira

    /// <summary>
    /// Kirada ama TESLİM EDİLMEMİŞ (CikisKm null) kira BasTar'dan itibaren sınırsız pencere üretiyordu; araç hiç çıkmadan
    /// (ör. personel teslimata götürürken) geçen geçiş müşteriye eşleşiyordu. L3 kararı: Belirsiz (teslim notuyla).
    /// </summary>
    [Fact]
    public async Task P8_undelivered_open_rental_is_ambiguous_until_delivered()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);
        // R2'yi teslim edilmemiş hâle getir (SetUpAsync teslim edilmiş kurar): Kirada + çıkış KM yok.
        await using (var db = await s.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
            await db.Rentals.Where(r => r.Id == w.R2).ExecuteUpdateAsync(u => u
                .SetProperty(r => r.CikisKm, (int?)null).SetProperty(r => r.CikisYakit, (int?)null));
        var csv = Csv("Plaka;Çıkış İstasyonu;Geçiş Tarihi;Tutar", $"34 HGS 01;Gebze;{Wall(2, 11)};5,00");
        var p = await Svc(s).PreviewAsync(csv);
        var row = Assert.Single(p.Satirlar);
        Assert.True(row.Durum != HgsImportRowStatus.Eslesti,
            $"Teslim edilmemiş kiraya eşleşti: {row.SozlesmeNo} (durum {row.Durum}).");
        Assert.Equal(HgsImportRowStatus.Belirsiz, row.Durum);
        Assert.Equal(HgsCrossingMatcher.UndeliveredNote, row.Hata);

        // İçe aktarılır (Belirsiz, yansıtılamaz); teslim girilip yeniden eşleştirilince kiraya bağlanır.
        await Svc(s).ImportAsync(csv, "a.csv");
        var id = Assert.Single(await AllAsync(s)).Id;
        Assert.Equal(0, (await Svc(s).ReflectAsync([id], 1.00m)).Yansitilan);
        Assert.True(await s.ServiceProvider.GetRequiredService<RentalService>().DeliverAsync(w.R2, 1000, 4));
        Assert.Equal(1, await Svc(s).RematchAsync());
        Assert.Equal(HgsCrossingStatus.Eslesti, Assert.Single(await AllAsync(s)).Durum);
    }
}
