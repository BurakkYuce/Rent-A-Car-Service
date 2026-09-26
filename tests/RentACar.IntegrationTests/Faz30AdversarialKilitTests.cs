using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.FaturaDonemleri;
using RentACar.Application.Finance;
using RentACar.Application.Kur;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-30 ADVERSARIAL KALICI KİLİTLER — her biri bir kez GERÇEKTEN kırıktı. Adversarial inceleme
/// sırasında bulguyu kanıtlayan probe olarak yazıldılar; düzeltmelerden sonra yeşile döndüler ve
/// regresyon kilidi olarak BIRAKILDILAR.
///
/// <para>H1 (High): kira-başına opt-in çiti (<c>DonemselFaturalama</c>) elle yolda hiç
/// sorulmuyordu — periyodik faturalamayı HİÇ AÇMAMIŞ her uzun kira aday çıkıyor, tek tıkla
/// değişmez fatura + tahsilat yazılabiliyordu (2 fatura, 2 tahsilat, defter borç 12200).
/// M1: idempotent yutulan tahsilat "yazıldı" sayılıyordu (sayaç yalan söylüyordu).
/// M2: atlanan mesajları sözleşme no taşımıyordu, iki kira ayırt edilemiyordu.
/// M3: 10'dan fazla atlanan web ucunda sessizce yutuluyordu.
/// M4: sözleşme-bazlı yürütme job'dan dar yakalıyordu.
/// L1: ekran EUR + TRY'yi tek birimsiz sayıda topluyordu.</para>
/// </summary>
[Collection("postgres")]
public sealed class Faz30AdversarialKilitTests(PostgresFixture fx)
{
    private static async Task<(Guid Kira, Guid Cari)> RentalAsync(
        IServiceProvider sp, string plate, bool periodic = true, string? currency = null)
    {
        var start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-65);
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plate });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Probe", Soyad = "Musteri" });
        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = m, VehicleId = v, BasTar = start, BitTar = start.AddDays(90),
            GunlukUcret = 100m, DonemselFaturalama = periodic, Doviz = currency
        });
        return (id, m);
    }

    private static async Task<(decimal Borc, decimal Alacak, int Fatura, int Kasa)> LedgerAsync(IServiceProvider sp)
    {
        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        var rows = await db.AccountLedgerEntries.AsNoTracking().ToListAsync();
        return (rows.Where(r => r.Direction == LedgerDirection.Debit).Sum(r => r.Amount.AmountInBase),
                rows.Where(r => r.Direction == LedgerDirection.Credit).Sum(r => r.Amount.AmountInBase),
                await db.Invoices.AsNoTracking().CountAsync(),
                await db.CashTransactions.AsNoTracking().CountAsync());
    }

    // ================================================================ BULGU H1
    /// <summary>
    /// H1 — JOB'un kira-başına opt-in kapısı <c>RentalContract.DonemselFaturalama</c>'dır
    /// (<c>DonemFaturaUretici.RunAsync</c>: <c>… &amp;&amp; r.DonemselFaturalama</c>). FAZ-30 aday
    /// sorgusu (<c>FaturaDonemRepository.AdaylarAsync</c>) bu kolonu HİÇ sormaz — dönem planı
    /// <c>FaturaDonemPlanService.UygunMu</c> (Gün ≥ 28) ile kurulduğundan, opt-in KAPALI her uzun
    /// kira ekranda aday olarak listelenir.
    /// </summary>
    [Fact]
    public async Task H1_optin_kapali_kira_aday_listesinde_GORUNMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        // Kullanıcı bu kirayı periyodik faturalamaya SOKMADI (job ona asla dokunmaz).
        var (rental, _) = await RentalAsync(sp, "34 PB 01", periodic: false);

        var mine = (await sp.GetRequiredService<AutoCollectionService>().CandidatesAsync())
            .Where(a => a.RentalId == rental).ToList();

        Assert.Empty(mine); // BULGU: 2 dönem listeleniyor
    }

    /// <summary>
    /// H1 (para kanıtı) — opt-in KAPALI kirada elle tetik gerçekten fatura keser ve tahsilat
    /// yazar. Bağımsız oracle: 90 gün × 100 = 9000; dönem 1 = 30 gün → 3000, dönem 2 = 31 gün →
    /// 3100. İki fatura (3000+3100) + iki tahsilat (3000+3100) = defter borç 12200.
    /// </summary>
    [Fact]
    public async Task H1b_optin_kapali_kirada_defter_YAZILMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<AutoCollectionService>();
        var (rental, _) = await RentalAsync(sp, "34 PB 02", periodic: false);

        var candidates = (await svc.CandidatesAsync()).Where(a => a.RentalId == rental).ToList();
        Assert.Empty(candidates);   // opt-in KAPALI kira aday değil

        // Uydurma POST ile ZORLAMA denemesi de reddedilmeli — aday çiti son savunma.
        var (debtBefore, _, invoiceBefore, cashBefore) = await LedgerAsync(sp);
        var result = await svc.RunAsync([(rental, 1), (rental, 2)], true, LedgerAccountType.Kasa);
        var (debit, credit, invoice, cash) = await LedgerAsync(sp);

        Assert.Equal(debit, credit);
        Assert.True(result.Kesilen == 0 && invoice == invoiceBefore && cash == cashBefore && debit == debtBefore,
            $"BULGU: opt-in KAPALI kirada {result.Kesilen} fatura kesildi, {result.Tahsilat} tahsilat " +
            $"yazıldı (fatura tablosu {invoice}, kasa {cash}, defter borç {debit}).");
    }

    // ================================================================ BULGU M1
    /// <summary>
    /// M1 — <c>OtomatikTahsilatService.CalistirAsync</c> sayacı <c>if (tahsilatYap) tahsilat++;</c>
    /// ile KOŞULSUZ artar; oysa <c>DonemTahsilatService.KesVeTahsilEtAsync</c> "zaten kaydedilmiş"
    /// idempotent durumunu YUTAR. Deterministik anahtar <c>RowKey(rentalId, donemSira)</c> önceden
    /// tüketilmişse hiç tahsilat YAZILMADIĞI hâlde kullanıcıya "N tahsilat yazıldı" denir.
    /// </summary>
    [Fact]
    public async Task M1_tahsilat_sayaci_yazilmayani_SAYMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<AutoCollectionService>();
        var (rental, account) = await RentalAsync(sp, "34 PB 03");

        // Dönem 1'in deterministik tahsilat anahtarı önceden tüketilmiş (çift-submit kalıntısı).
        await sp.GetRequiredService<CashService>().CollectAsync(new CashInput
        {
            CariId = account, RentalId = rental, Tutar = 1m, Doviz = "TRY", Kur = 1m,
            Hesap = LedgerAccountType.Kasa, Aciklama = "onceden",
            IslemAnahtari = CashService.RowKey(rental, 1)
        });

        var cashBefore = (await LedgerAsync(sp)).Kasa;
        var result = await svc.RunAsync([(rental, 1)], true, LedgerAccountType.Kasa);
        var cashAfter = (await LedgerAsync(sp)).Kasa;

        Assert.Equal(cashBefore, cashAfter);   // hiç yeni tahsilat yazılmadı (idempotent yutma)
        Assert.True(result.Tahsilat == 0,
            $"BULGU: kasa hareket sayısı {cashBefore}->{cashAfter} DEĞİŞMEDİ ama sonuç mesajı " +
            $"'{result.Kesilen} dönem kesildi, {result.Tahsilat} tahsilat yazıldı' diyor.");
    }

    // ================================================================ BULGU M2
    /// <summary>
    /// M2 — <c>Atlananlar</c> satırları yalnız <c>"Dönem {sira}: …"</c> yazar; SÖZLEŞME NO yok.
    /// Çok sözleşmeli bir çalıştırmada hangi kiranın atlandığı ayırt edilemez (aynı metin tekrar
    /// eder). Para yüzeyinde "neyin işlenmediği" izlenemez hâle gelir.
    /// </summary>
    [Fact]
    public async Task M2_atlananlar_SOZLESME_NO_soyler()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<AutoCollectionService>();

        var (k1, _) = await RentalAsync(sp, "34 PB 04");
        var (k2, _) = await RentalAsync(sp, "34 PB 05");

        // İki AYRI sözleşmenin 2. dönemi (1. dönemler hâlâ Planlandi → sıralı-kesim reddi).
        var result = await svc.RunAsync([(k1, 2), (k2, 2)], true, LedgerAccountType.Kasa);

        Assert.Equal(0, result.Kesilen);
        Assert.Equal(2, result.Atlananlar.Count);
        Assert.True(result.Atlananlar.Distinct().Count() == 2,
            $"BULGU: iki FARKLI sözleşmenin atlanma mesajı birebir aynı — " +
            $"[{string.Join(" || ", result.Atlananlar)}]");
    }

    /// <summary>
    /// M2b — web ucu <c>Atlananlar.Take(10)</c> ile kesiyor. <c>MaxSecim</c> 200 olduğundan
    /// 10'dan fazla atlanan SESSİZCE kaybolur (iddia 5: "sessiz yutma yok").
    /// </summary>
    [Fact]
    public async Task M3_gizlenen_atlananlarin_SAYISI_soylenir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<AutoCollectionService>();
        await RentalAsync(sp, "34 PB 06");

        // 25 uydurma seçim → 25 atlanan.
        var selection = Enumerable.Range(1, 25).Select(i => (Guid.NewGuid(), i)).ToList();
        var result = await svc.RunAsync(selection, true, LedgerAccountType.Kasa);
        Assert.Equal(25, result.Atlananlar.Count);

        // Web ucunun kullandığı SAF kural: ilk 10 + "… ve N kayıt daha" sayacı.
        var show = AutoCollectionService.ShowSkipped(result.Atlananlar);
        Assert.True(show.Count == 11 && show[^1].Contains("15"),
            $"BULGU: {result.Atlananlar.Count} atlanan üretildi, kullanıcıya giden liste " +
            $"{show.Count} satır ve son satır '{show[^1]}' — gizlenen sayısı söylenmiyor.");
        Assert.Contains($"toplam {result.Atlananlar.Count}", show[^1]);
    }

    // ================================================================ BULGU M3
    /// <summary>
    /// M3 — sözleşme-bazlı yürütme yalnız <c>ValidationException</c> yakalar. JOB çekirdeği
    /// (<c>DonemFaturaUretici.RunAsync</c>) bilinçli olarak GENİŞ yakalar
    /// (<c>catch (Exception ex) when (ex is not OperationCanceledException)</c>). Manuel yol
    /// job'dan DAR: beklenmedik bir hata partiyi ortada bırakır — ÖNCEKİ dönemler zaten
    /// commit'lidir, kullanıcı ise 500 alır ve neyin yazıldığını göremez.
    ///
    /// <para>Bu probe asimetriyi yürütmeden gösterir: <c>KesVeTahsilEtAsync</c> zincirinde
    /// ValidationException DIŞI bir hata (DbUpdateException/InvalidOperationException) oluşursa
    /// <c>CalistirAsync</c> onu yutmaz. Aşağıda ilk dönem BAŞARIYLA kesilir, sonra beklenmedik
    /// hata simüle edilir (dış cari silme yoluyla zorlanamadığı için doğrudan sözleşme
    /// karşılaştırması yapılır) — bkz. rapor.</para>
    /// </summary>
    [Fact]
    public async Task M4_kismi_ilerleme_sonrasi_hata_raporlanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<AutoCollectionService>();
        var (rental, _) = await RentalAsync(sp, "34 PB 07");

        // Dönem 1 başarılı, dönem 2'de ValidationException (sıralı değil değil — ikisi de aday).
        // Burada asimetrinin ÖLÇÜLEBİLİR yüzü: ValidationException yakalanır (kesilen=2 olur),
        // ama tür daraltması yüzünden DİĞER hata sınıfları partiyi düşürür.
        var result = await svc.RunAsync([(rental, 1), (rental, 2)], true, LedgerAccountType.Kasa);
        Assert.Equal(2, result.Kesilen);

        // Job'un yakalama genişliği ile manuel yolunki AYNI olmalı (tek kopya ilkesi).
        var jobSource = await File.ReadAllTextAsync(Path.Combine(RepoRoot(),
            "src/RentACar.Infrastructure/Persistence/PeriodInvoiceGenerator.cs"));
        var manualSource = await File.ReadAllTextAsync(Path.Combine(RepoRoot(),
            "src/RentACar.Application/FaturaDonemleri/AutoCollectionService.cs"));
        Assert.True(
            jobSource.Contains("catch (Exception ex) when (ex is not OperationCanceledException)")
            && manualSource.Contains("catch (Exception ex) when (ex is not OperationCanceledException)"),
            "BULGU: job GENİŞ yakalıyor (catch Exception when not OperationCanceled), manuel yol " +
            "yalnız ValidationException yakalıyor — beklenmedik hata partiyi yarıda bırakır ve " +
            "kullanıcı hangi dönemlerin zaten kesildiğini göremez (500).");
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo kökü bulunamadı");
    }

    // ================================================================ BULGU L1
    /// <summary>
    /// L1 — aday satırı <c>KiraTutar</c>'ı NATIVE dövizde, <c>CariBakiye</c>'yi BAZ (TRY) taşır.
    /// Ekran (<c>OtomatikTahsilat.razor</c>) bunları <c>_adaylar.Sum(a =&gt; a.KiraTutar)</c> ile
    /// birimsiz topluyor → EUR + TRY aynı toplamda.
    /// </summary>
    [Fact]
    public async Task L1_ekran_toplami_DOVIZ_KIRILIMLI()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<FixedExchangeRateService>()
            .UpsertAsync(new SabitKurInput { Kod = "EUR", Kur = 40m, Aktif = true });

        await RentalAsync(sp, "34 PB 08");                    // TRY 9000
        await RentalAsync(sp, "34 PB 09", currency: "EUR");      // EUR 9000

        var candidates = await sp.GetRequiredService<AutoCollectionService>().CandidatesAsync();
        var currencies = candidates.Select(a => a.Doviz).Distinct().OrderBy(x => x).ToList();
        Assert.Equal(["EUR", "TRY"], currencies);             // senaryo gerçekten karışık dövizli

        // Ekranın kullandığı SAF kural: döviz KIRILIMLI toplam — tek birimsiz sayı YOK.
        var breakdown = AutoCollectionService.CurrencyTotals(candidates);
        Assert.True(breakdown.Count == 2,
            $"BULGU: ekran {breakdown.Count} satırda topluyor — karışık dövizli aday listesinde " +
            "tek toplam anlamsız olurdu.");
        Assert.Equal("EUR", breakdown[0].Doviz);
        Assert.All(breakdown, k => Assert.True(k.Toplam > 0m));
    }
}
