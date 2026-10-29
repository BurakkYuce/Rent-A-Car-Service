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
/// anları) bir VERİ sorunudur — <c>fix/blazor-saat-kaymasi</c> PR'ında; HGS canlıda o düzeltme olmadan açılmaz.
/// Tur 2 L1 (R1/R1b/R1c): aynı geçiş farklı zaman anlamıyla (çıkış ↔ banka işlem anı) → aynı istasyonla 6 sa
/// içinde olası mükerrer; "İşlem Tarihi" otomatik eşlenmez. #407 (Q1–Q3, F1–F3, C1, C2, T, P, M1d): geniş kural
/// yalnız farklı dosyalar + aynı kira + bire bir; çıkış istasyonu ve normalize yakınlık anahtarı; elle onay yolu.</para>
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
            ws.Cell(1, 1).Value = "Plaka"; ws.Cell(1, 2).Value = "Geçiş Tarihi"; ws.Cell(1, 3).Value = "Saat"; ws.Cell(1, 4).Value = "Tutar";
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

    // ===================================================================== R1 (tur 2, Low) — farklı zaman anlamı

    /// <summary>
    /// AYNI geçiş iki dökümde FARKLI zaman anlamıyla: PTT dökümünde "Çıkış Zamanı" 10:55, banka dökümünde kullanıcının
    /// AÇIKÇA geçiş zamanına eşlediği "İşlem Tarihi" 10:30 (Δt 25 dk &gt; 60 sn). Aynı plaka + tutar + İzmit→Bursa →
    /// ikinci satır olası mükerrer (Belirsiz). Oracle: ikisini birden yansıtmak bile A'ya tek borç 995,00 (oran 1,00).
    /// </summary>
    [Fact]
    public async Task R1_same_crossing_with_exit_vs_transaction_time_is_charged_once()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);
        await Svc(s).ImportAsync(Csv("Plaka;Giriş İstasyonu;Çıkış İstasyonu;Çıkış Zamanı;Tutar",
            $"34 HGS 01;İzmit;Bursa;{Wall(0, 10, 55)};995,00"), "ptt.csv");
        var bank = Encoding.UTF8.GetBytes(string.Join("\n", "Plaka;Giriş İstasyonu;Çıkış İstasyonu;İşlem Tarihi;Tutar",
            $"34 HGS 01;İzmit;Bursa;{Wall(0, 10, 30)};995,00"));
        var mapping = new Dictionary<string, string?>
        {
            [HgsImportColumns.Plate] = "Plaka", [HgsImportColumns.EntryStation] = "Giriş İstasyonu",
            [HgsImportColumns.ExitStation] = "Çıkış İstasyonu", [HgsImportColumns.CrossingTime] = "İşlem Tarihi",
            [HgsImportColumns.Amount] = "Tutar",
        };
        var second = await Svc(s).ImportAsync(Rows(bank, "banka.csv", mapping), "banka.csv");
        Assert.Equal(1, second.Eklenen);
        Assert.Equal(1, second.OlasiMukerrer);

        var all = await AllAsync(s);
        Assert.Equal(1, all.Count(c => c.Durum == HgsCrossingStatus.Eslesti));
        var result = await Svc(s).ReflectAsync(all.Select(c => c.Id).ToList(), 1.00m);
        Assert.Equal(1, result.Yansitilan);
        Assert.Equal(995.00m, await s.ServiceProvider.GetRequiredService<CashService>().GetAccountBalanceAsync(w.CustomerA));
        await Svc(s).RematchAsync();
        Assert.Single(await AllAsync(s), c => c.Durum == HgsCrossingStatus.Belirsiz);
    }

    /// <summary>"İşlem Tarihi" (bankanın kayıt anı olabilir) geçiş zamanına OTOMATİK eşlenmez: başka zaman sütunu yoksa
    /// zorunlu alan eksik (errors[eslesme]) — kullanıcı önizlemede açıkça seçmeli.</summary>
    [Fact]
    public void R1b_transaction_date_column_is_not_auto_mapped_to_crossing_time()
    {
        var (labels, keys) = HgsImportColumns.Resolve(["Plaka", "İşlem Tarihi", "İşlem Saati", "Tutar"], null);
        Assert.Null(labels[HgsImportColumns.CrossingTime]);
        Assert.Null(labels[HgsImportColumns.CrossingClock]);
        var ex = Assert.Throws<ValidationException>(() => HgsImportColumns.RequireMandatory(keys));
        Assert.Equal("eslesme", ex.Alan);
        // #407 Q6: yalın "Tarih" (banka ekstresinde çoğu kez kayıt tarihi) da otomatik eşlenmez.
        Assert.Null(HgsImportColumns.Resolve(["Plaka", "Tarih", "Geçiş Yeri", "Tutar"], null).Labels[HgsImportColumns.CrossingTime]);
    }

    /// <summary>Kontrol (aşırı genişleme yok): aynı istasyon + tutar ama 6 saatten uzak iki geçiş (R1 içinde 04:00 ve
    /// 10:55) gerçek iki geçiştir → ikisi de borçlanır: 2 × 995,00 = 1.990,00.</summary>
    [Fact]
    public async Task R1c_control_same_station_crossings_beyond_window_are_both_charged()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);
        await Svc(s).ImportAsync(Csv("Plaka;Giriş İstasyonu;Çıkış İstasyonu;Çıkış Zamanı;Tutar",
            $"34 HGS 01;İzmit;Bursa;{Wall(0, 10, 55)};995,00"), "a.csv");
        var second = await Svc(s).ImportAsync(Csv("Plaka;Giriş İstasyonu;Çıkış İstasyonu;Çıkış Zamanı;Tutar",
            $"34 HGS 01;İzmit;Bursa;{Wall(0, 4, 0)};995,00"), "b.csv");
        Assert.Equal(0, second.OlasiMukerrer);
        var all = await AllAsync(s);
        Assert.All(all, c => Assert.Equal(HgsCrossingStatus.Eslesti, c.Durum));
        Assert.Equal(2, (await Svc(s).ReflectAsync(all.Select(c => c.Id).ToList(), 1.00m)).Yansitilan);
        Assert.Equal(1990.00m, await s.ServiceProvider.GetRequiredService<CashService>().GetAccountBalanceAsync(w.CustomerA));
    }

    // ===================================================================== #407 adversarial (M1 + L1/L2/L4)

    private static Task<decimal> BalanceAsync(IServiceScope s, Guid customer)
        => s.ServiceProvider.GetRequiredService<CashService>().GetAccountBalanceAsync(customer);

    private static async Task<HgsReflectResult> ReflectAllAsync(IServiceScope s)
        => await Svc(s).ReflectAsync((await AllAsync(s)).Select(c => c.Id).ToList(), 1.00m);

    /// <summary>Tek istasyon sütunlu banka dökümü ("Geçiş Yeri"), kullanıcı "İşlem Tarihi"ni elle eşler.</summary>
    private static IReadOnlyList<HgsRawRow> Bank(string station, string when, string amount, string plate = "34 HGS 01")
        => Rows(Encoding.UTF8.GetBytes(string.Join("\n", "Plaka;Geçiş Yeri;İşlem Tarihi;Tutar", $"{plate};{station};{when};{amount}")),
            "banka.csv", new Dictionary<string, string?>
            {
                [HgsImportColumns.Plate] = "Plaka", [HgsImportColumns.ExitStation] = "Geçiş Yeri",
                [HgsImportColumns.CrossingTime] = "İşlem Tarihi", [HgsImportColumns.Amount] = "Tutar",
            });

    private static IReadOnlyList<HgsRawRow> Exits(params (string Station, string When, string Amount)[] rows)
        => Csv([.. new[] { "Plaka;Çıkış İstasyonu;Çıkış Zamanı;Tutar" }.Concat(rows.Select(r => $"34 HGS 01;{r.Station};{r.When};{r.Amount}"))]);

    /// <summary>Q1 (L1): PTT "İzmit→Bursa" 10:55 ↔ banka tek sütun "Geçiş Yeri: Bursa" 10:30 (elle eşlenen İşlem
    /// Tarihi). Bir tarafta giriş yok → yalnız çıkış karşılaştırılır. Oracle: A'ya tek borç 995,00.</summary>
    [Fact]
    public async Task Q1_bank_single_station_column_is_compared_by_exit_and_charged_once()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);
        await Svc(s).ImportAsync(Csv("Plaka;Giriş İstasyonu;Çıkış İstasyonu;Çıkış Zamanı;Tutar",
            $"34 HGS 01;İzmit;Bursa;{Wall(0, 10, 55)};995,00"), "ptt.csv");
        Assert.Equal(1, (await Svc(s).ImportAsync(Bank("Bursa", Wall(0, 10, 30), "995,00"), "banka.csv")).OlasiMukerrer);
        Assert.Equal(1, (await ReflectAllAsync(s)).Yansitilan);
        Assert.Equal(995.00m, await BalanceAsync(s, w.CustomerA));
    }

    /// <summary>Q2 (L2): istasyon adı farklı yazılmış (Δt 25 dk, ikisi de yalnız çıkışlı). Oracle: tek borç 12,40.</summary>
    [Theory]
    [InlineData("Avrasya Tüneli", "AVRASYA TÜNELİ")]
    [InlineData("Avrasya Tüneli", "AVRASYA TUNELI")]
    [InlineData("Avrasya Tüneli", "  Avrasya   tüneli ")]
    [InlineData("Kınalı-Gişe", "KINALI GİŞE")]
    [InlineData("Avrasya Tüneli", "Avrasya Tüneli.")]
    [InlineData("Çamlıca", "Çamlıca")] // NFD: C + birleşik çengel
    public async Task Q2_station_spelling_variants_are_charged_once(string ptt, string bank)
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);
        await Svc(s).ImportAsync(Exits((ptt, Wall(0, 10, 55), "12,40")), "ptt.csv");
        await Svc(s).ImportAsync(Bank(bank, Wall(0, 10, 30), "12.40"), "banka.csv");
        await ReflectAllAsync(s);
        Assert.Equal(12.40m, await BalanceAsync(s, w.CustomerA));
    }

    /// <summary>Q3 (kontrol): üç döküm, tutar biçimleri farklı (995,00 / ₺995 / 995.00 TL), aynı istasyon, 10:55 / 10:30 /
    /// 10:40. Her döküm diğer her dökümde en çok bir satırla eşleşir → üçüncü kopya da yakalanır. Oracle: 995,00.</summary>
    [Fact]
    public async Task Q3_three_dumps_of_the_same_crossing_are_charged_once()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);
        await Svc(s).ImportAsync(Exits(("Bursa", Wall(0, 10, 55), "995,00")), "ptt.csv");
        await Svc(s).ImportAsync(Bank("BURSA", Wall(0, 10, 30), "₺995"), "banka1.csv");
        await Svc(s).ImportAsync(Bank("bursa", Wall(0, 10, 40), "995.00 TL"), "banka2.csv");
        await Svc(s).RematchAsync();
        await ReflectAllAsync(s);
        Assert.Equal(995.00m, await BalanceAsync(s, w.CustomerA));
    }

    /// <summary>F1 (M1-a): TEK dosyada aynı tünelden iki GERÇEK geçiş (D+3 09:00 ve 14:00, 12,40, açık R2). Aynı dosya =
    /// aynı kaynak ve zaman anlamı → geniş kural uygulanmaz. Oracle: B'ye 24,80.</summary>
    [Fact]
    public async Task F1_same_file_genuine_round_trip_through_same_station_is_charged_twice()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);
        var imp = await Svc(s).ImportAsync(Exits(("Avrasya Tüneli", Wall(3, 9), "12,40"), ("Avrasya Tüneli", Wall(3, 14), "12,40")), "ptt.csv");
        Assert.Equal(0, imp.OlasiMukerrer);
        await Svc(s).RematchAsync();
        Assert.Equal(2, (await ReflectAllAsync(s)).Yansitilan);
        Assert.Equal(24.80m, await BalanceAsync(s, w.CustomerB));
    }

    /// <summary>F2 (M1-a/b): tek dosyada 6 gerçek geçiş, 4 saat arayla (D+3 00:00 … 20:00), aynı istasyon + tutar.
    /// Oracle: 6 × 12,40 = 74,40.</summary>
    [Fact]
    public async Task F2_chain_of_genuine_crossings_four_hours_apart_are_all_charged()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);
        var imp = await Svc(s).ImportAsync(Exits([.. Enumerable.Range(0, 6).Select(i => ("Avrasya Tüneli", Wall(3, i * 4), "12,40"))]), "ptt.csv");
        Assert.Equal(0, imp.OlasiMukerrer);
        Assert.Equal(6, (await ReflectAllAsync(s)).Yansitilan);
        Assert.Equal(74.40m, await BalanceAsync(s, w.CustomerB));
    }

    /// <summary>F2b (M1-b, zincirleme yok): ikinci dökümde aynı 3 geçiş 20 dk kaymayla (banka işlem anı). Her satır diğer
    /// dökümde EN ÇOK bir satırla eşleşir → 3 ikincil, 3 asıl. Oracle: 3 × 12,40 = 37,20.</summary>
    [Fact]
    public async Task F2b_two_dumps_of_three_crossings_pair_one_to_one()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);
        await Svc(s).ImportAsync(Exits([.. new[] { 9, 13, 17 }.Select(h => ("Avrasya Tüneli", Wall(3, h), "12,40"))]), "ptt.csv");
        // İşlem Tarihi önerilmediği için elle eşleme gerekir.
        var raw = Rows(Encoding.UTF8.GetBytes(string.Join("\n", new[] { "Plaka;Geçiş Yeri;İşlem Tarihi;Tutar" }
                .Concat(new[] { 9, 13, 17 }.Select(h => $"34 HGS 01;Avrasya Tüneli;{Wall(3, h, 20)};12,40")))), "banka.csv",
            new Dictionary<string, string?>
            {
                [HgsImportColumns.Plate] = "Plaka", [HgsImportColumns.ExitStation] = "Geçiş Yeri",
                [HgsImportColumns.CrossingTime] = "İşlem Tarihi", [HgsImportColumns.Amount] = "Tutar",
            });
        Assert.Equal(3, (await Svc(s).ImportAsync(raw, "banka.csv")).OlasiMukerrer);
        await Svc(s).RematchAsync();
        Assert.Equal(3, (await ReflectAllAsync(s)).Yansitilan);
        Assert.Equal(37.20m, await BalanceAsync(s, w.CustomerB));
    }

    /// <summary>F3 (M1-c): FARKLI kira/müşteri. A'nın R1'deki D 10:00 Avrasya geçişi yansıtıldı; araç D 13:00'te B'ye
    /// (R4) çıktı, B D 15:00'te aynı tünelden aynı tutarla geçti (ayrı dosya). Oracle: A 12,40, B 12,40.</summary>
    [Fact]
    public async Task F3_next_customer_same_station_within_six_hours_is_charged()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);
        await using (var db = await s.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            db.Rentals.Add(new RentalContract
            {
                SozlesmeNo = "KS-HGS-4", MusteriId = w.CustomerB, VehicleId = w.V1, Durum = RentalStatus.Tamamlandi, CikisOfisi = "SubeA",
                BasTar = HgsImportTests.At(0, 13), BitTar = HgsImportTests.At(0, 20), GercekDonusTar = HgsImportTests.At(0, 20),
                CreatedAtUtc = HgsImportTests.At(0, 13),
            });
            await db.SaveChangesAsync();
        }
        await Svc(s).ImportAsync(Exits(("Avrasya Tüneli", Wall(0, 10), "12,40")), "gun1.csv");
        await ReflectAllAsync(s);
        Assert.Equal(0, (await Svc(s).ImportAsync(Exits(("Avrasya Tüneli", Wall(0, 15), "12,40")), "gun2.csv")).OlasiMukerrer);
        await ReflectAllAsync(s);
        Assert.Equal(12.40m, await BalanceAsync(s, w.CustomerA));
        Assert.Equal(12.40m, await BalanceAsync(s, w.CustomerB));
    }

    /// <summary>C1 (kontrol): iki döküm PARALEL içe aktarılır (8 tur); kilit altında yeniden karar → tek Eslesti, ters
    /// öncelik sırasıyla tek tek yansıtılınca A'ya 995,00.</summary>
    [Fact]
    public async Task C1_parallel_import_of_two_dumps_yields_single_charge()
    {
        using var host = new TestHost(fx.AppConnectionString);
        for (var round = 0; round < 8; round++)
        {
            var w = await HgsImportTests.SetUpAsync(host);
            using var s1 = host.ScopeFor(w.Tenant);
            using var s2 = host.ScopeFor(w.Tenant);
            var ptt = Csv("Plaka;Giriş İstasyonu;Çıkış İstasyonu;Çıkış Zamanı;Tutar", $"34 HGS 01;İzmit;Bursa;{Wall(0, 10, 55)};995,00");
            var bank = Bank("Bursa", Wall(0, 10, 30), "995,00");
            await Task.WhenAll(Task.Run(() => Svc(s1).ImportAsync(ptt, "ptt.csv")), Task.Run(() => Svc(s2).ImportAsync(bank, "banka.csv")));
            var all = await AllAsync(s1);
            Assert.Equal(2, all.Count);
            Assert.Equal(1, all.Count(c => c.Durum == HgsCrossingStatus.Eslesti));
            foreach (var c in all.OrderByDescending(c => c.CreatedAtUtc)) await Svc(s1).ReflectAsync([c.Id], 1.00m);
            Assert.Equal(995.00m, await BalanceAsync(s1, w.CustomerA));
        }
    }

    /// <summary>C2 (kontrol): yansıtma sonrası ikincil satır gelir; yeniden eşleştirme ×2 + tekrar yansıtma yeni para
    /// yazmaz, defter dengeli.</summary>
    [Fact]
    public async Task C2_rematch_and_rereflect_preserve_state_and_ledger_balance()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);
        await Svc(s).ImportAsync(Bank("Bursa", Wall(0, 10, 30), "995,00"), "banka.csv");
        Assert.Equal(1, (await ReflectAllAsync(s)).Yansitilan);
        await Svc(s).ImportAsync(Exits(("Bursa", Wall(0, 10, 55), "995,00")), "ptt.csv");
        Assert.Equal(0, await Svc(s).RematchAsync());
        await Svc(s).RematchAsync();
        var again = await ReflectAllAsync(s);
        Assert.Equal((0, 1, 1), (again.Yansitilan, again.ZatenYansitilmis, again.Uygunsuz));
        var ledger = await HgsLedgerAsync(s);
        Assert.Equal(2, ledger.Count);
        Assert.Equal(ledger.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase),
            ledger.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase));
        Assert.Equal(995.00m, await BalanceAsync(s, w.CustomerA));
    }

    /// <summary>T (kontrol, RLS racar_app): başka kiracının aynı plaka + istasyon + tutar geçişi olası mükerrer yapmaz.</summary>
    [Fact]
    public async Task T_other_tenant_crossing_does_not_make_near_duplicate()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w1 = await HgsImportTests.SetUpAsync(host);
        var w2 = await HgsImportTests.SetUpAsync(host);
        using (var s1 = host.ScopeFor(w1.Tenant))
        {
            await Svc(s1).ImportAsync(Exits(("Bursa", Wall(0, 10, 55), "995,00")), "t1.csv");
            await ReflectAllAsync(s1);
        }
        using var s2 = host.ScopeFor(w2.Tenant);
        Assert.Equal(0, (await Svc(s2).ImportAsync(Bank("Bursa", Wall(0, 10, 30), "995,00"), "t2.csv")).OlasiMukerrer);
        Assert.Equal(1, (await ReflectAllAsync(s2)).Yansitilan);
        Assert.Equal(995.00m, await BalanceAsync(s2, w2.CustomerA));
    }

    /// <summary>P (L4): 20.000 satır, TEK plaka, ~5,5 saat içinde, farklı tutarlar, aynı istasyon. Önizleme / içe aktarma /
    /// yeniden eşleştirme makul sürede (dizin plaka + tutar gruplu).</summary>
    [Fact]
    public async Task P_dense_single_plate_file_stays_fast()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);
        var start = D.AddDays(-20).ToDateTime(new TimeOnly(6, 0, 1));
        var rows = Csv([.. new[] { "Plaka;Çıkış İstasyonu;Çıkış Zamanı;Tutar" }.Concat(Enumerable.Range(0, 20_000).Select(i =>
            $"34 HGS 01;Gişe;{start.AddSeconds(i).ToString("dd.MM.yyyy HH:mm:ss", Inv)};{(1m + i * 0.01m).ToString("0.00", Inv)}"))]);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Svc(s).PreviewAsync(rows);
        var preview = sw.Elapsed; sw.Restart();
        Assert.Equal(20_000, (await Svc(s).ImportAsync(rows, "yogun.csv")).Eklenen);
        var import = sw.Elapsed; sw.Restart();
        await Svc(s).RematchAsync();
        var rematch = sw.Elapsed;
        Assert.True(preview < TimeSpan.FromSeconds(15) && import < TimeSpan.FromSeconds(30) && rematch < TimeSpan.FromSeconds(30),
            $"önizleme {preview.TotalSeconds:0.0} sn, içe aktarma {import.TotalSeconds:0.0} sn, yeniden eşleştirme {rematch.TotalSeconds:0.0} sn");
    }

    // ----------------------------------------------------------------- M1-d: "ayrı geçiş olarak onayla"

    /// <summary>İki ayrı dökümde aynı tünelden 3 saat arayla iki GERÇEK geçiş (D+3 09:00 / 12:00, R2) → ikincisi olası
    /// mükerrer. FinanceWrite onaylar → Eslesti, yeniden eşleştirme geri almaz, ikisi de borçlanır. Oracle: B'ye 24,80.</summary>
    [Fact]
    public async Task M1d_confirmed_near_duplicate_is_charged_and_survives_rematch()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using var s = host.ScopeFor(w.Tenant);
        await Svc(s).ImportAsync(Exits(("Avrasya Tüneli", Wall(3, 9), "12,40")), "gun1.csv");
        Assert.Equal(1, (await Svc(s).ImportAsync(Exits(("Avrasya Tüneli", Wall(3, 12), "12,40")), "gun2.csv")).OlasiMukerrer);
        var pending = Assert.Single(await AllAsync(s), c => c.Durum == HgsCrossingStatus.Belirsiz);
        var eslesti = Assert.Single(await AllAsync(s), c => c.Durum == HgsCrossingStatus.Eslesti);

        // Olası mükerrer olmayan satır onaylanamaz; onay para yazmaz.
        Assert.Equal(new HgsConfirmResult(0, 1), await Svc(s).ConfirmAsync([eslesti.Id]));
        Assert.Equal(new HgsConfirmResult(1, 0), await Svc(s).ConfirmAsync([pending.Id]));
        Assert.Equal(new HgsConfirmResult(0, 1), await Svc(s).ConfirmAsync([pending.Id])); // tekrar: artık Eslesti
        Assert.Empty(await HgsLedgerAsync(s));
        var confirmed = Assert.Single(await AllAsync(s), c => c.Id == pending.Id);
        Assert.Equal(HgsCrossingStatus.Eslesti, confirmed.Durum);
        Assert.NotNull(confirmed.OnayZamani);

        await Svc(s).RematchAsync();
        Assert.All(await AllAsync(s), c => Assert.Equal(HgsCrossingStatus.Eslesti, c.Durum));
        Assert.Equal(2, (await ReflectAllAsync(s)).Yansitilan);
        Assert.Equal(24.80m, await BalanceAsync(s, w.CustomerB));
    }

    /// <summary>Onay FinanceWrite ister; şubeli kullanıcı başka şubenin kirasına bağlı satırı onaylayamaz (kilit altında 403).</summary>
    [Fact]
    public async Task M1d_confirm_requires_finance_write_and_branch_scope()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var w = await HgsImportTests.SetUpAsync(host);
        using (var s = host.ScopeFor(w.Tenant))
        {
            // V2 (06 HGS 02) R3 = SubeB, D-1 08:00 → 20:00.
            await Svc(s).ImportAsync(Csv("Plaka;Çıkış İstasyonu;Çıkış Zamanı;Tutar", $"06 HGS 02;Polatlı;{Wall(-1, 10)};60,00"), "a.csv");
            await Svc(s).ImportAsync(Csv("Plaka;Çıkış İstasyonu;Çıkış Zamanı;Tutar", $"06 HGS 02;Polatlı;{Wall(-1, 11)};60,00"), "b.csv");
        }
        Guid id;
        using (var s = host.ScopeFor(w.Tenant)) id = Assert.Single(await AllAsync(s), c => c.Durum == HgsCrossingStatus.Belirsiz).Id;

        using (var op = host.ScopeFor(w.Tenant, role: UserRole.Operator, assignedBranch: "SubeB"))
            await Assert.ThrowsAsync<NoPermissionException>(() => Svc(op).ConfirmAsync([id]));
        using (var other = host.ScopeFor(w.Tenant, role: UserRole.Operator, assignedBranch: "SubeA"))
        {
            other.ServiceProvider.GetRequiredService<TestIdentity>().EkIzinler = ["FinanceWrite"];
            await Assert.ThrowsAsync<NoPermissionException>(() => Svc(other).ConfirmAsync([id]));
        }
        using var check = host.ScopeFor(w.Tenant);
        Assert.Equal(HgsCrossingStatus.Belirsiz, Assert.Single(await AllAsync(check), c => c.Id == id).Durum);
    }
}
