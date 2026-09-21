using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.FinancialAccounts;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-67 — bağımsız nakit işlem ekranı + süzgeçli işlem listesi.
///
/// <para><b>Fazın asıl sözleşmesi:</b> yeni giriş yüzeyi para mantığını YENİDEN YAZMAZ; formu
/// mevcut <c>/finans/tahsilat</c>–<c>/finans/odeme</c> uçlarına gider, onlar da
/// <c>CollectAsync</c>/<c>PayAsync</c> çağırır. "İki yüzey, tek mantık" bir varsayım olarak
/// bırakılmaz — burada AYNI parametrelerle iki kez tahsilat yapılıp defter etkisi karşılaştırılır.</para>
///
/// <para><b>Bağımsız oracle:</b> beklenen tutar/satır sayıları elle kurulan senaryodan gelir.</para>
/// </summary>
[Collection("postgres")]
public sealed class NakitIslemTests(PostgresFixture fx)
{
    private static DateTimeOffset Gun(int fark)
    {
        var t = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(fark), DateTimeKind.Utc), TimeSpan.Zero);
        return t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));
    }

    private static Task<Guid> CariAsync(IServiceScope s, string ad, string? ozelKod = null)
        => s.ServiceProvider.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = ad, Soyad = "Test", OzelKod = ozelKod });

    private static Task<Guid> HesapAsync(IServiceScope s, string kod, string ad, string tur)
        => s.ServiceProvider.GetRequiredService<FinancialAccountService>()
            .CreateAsync(new FinancialAccountInput { Kod = kod, Ad = ad, Tur = tur });

    /// <summary>Bir tahsilatın ürettiği defter satırları (tür + yön + baz tutar), karşılaştırılabilir biçimde.</summary>
    private static async Task<List<(LedgerAccountType Tur, Guid? Ref, LedgerDirection Yon, decimal Baz)>>
        DefterAsync(TestHost host, Guid tenant, Guid txId)
    {
        using var scope = host.ScopeFor(tenant);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.SourceId == txId)
            .Select(e => new { e.AccountType, e.AccountRef, e.Direction, A = e.Amount.Amount, R = e.Amount.Rate })
            .ToListAsync();
        return [.. rows.Select(r => (r.AccountType, r.AccountRef, r.Direction, r.A * r.R))
            .OrderBy(x => x.Item1).ThenBy(x => x.Item3)];
    }

    // ---------------------------------------------------------------- İki yüzey, tek mantık

    [Fact]
    public async Task Bagimsiz_ekran_ile_ekstre_formu_AYNI_defter_etkisini_uretir()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = await CariAsync(scope, "AyniMantik");
        var kasa = await HesapAsync(scope, "MRK", "Merkez Kasa", "Kasa");

        // Her iki yüzey de AYNI servis çağrısına iner; testte bunu birebir aynı girdiyle iki kez
        // çağırarak kanıtlıyoruz (yüzeyler farklı, mantık tek).
        var girdi = () => new CashInput
        {
            CariId = cari, Tutar = 250m, Hesap = LedgerAccountType.Kasa, HesapId = kasa,
            Doviz = "TRY", Aciklama = "Test"
        };
        var ekstreden = await cash.CollectAsync(girdi());
        var bagimsizdan = await cash.CollectAsync(girdi());

        var a = await DefterAsync(host, tenant, ekstreden);
        var b = await DefterAsync(host, tenant, bagimsizdan);

        // Satır sayısı, hesap türleri, referanslar, yönler ve tutarlar BİREBİR aynı.
        Assert.Equal(2, a.Count);
        Assert.Equal(a, b);
        // Elle: 250 Kasa Borç / 250 Cari Alacak.
        Assert.Contains(a, x => x.Tur == LedgerAccountType.Kasa && x.Yon == LedgerDirection.Debit
                                && x.Baz == 250m && x.Ref == kasa);
        Assert.Contains(a, x => x.Tur == LedgerAccountType.Cari && x.Yon == LedgerDirection.Credit
                                && x.Baz == 250m && x.Ref == cari);
        // Cari bakiye: 2 tahsilat × 250 → −500 (müşteri alacaklı).
        Assert.Equal(-500m, await cash.GetCariBalanceAsync(cari));
    }

    [Fact]
    public async Task Islem_tarihi_formdan_verilebilir_gelecek_tarih_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = await CariAsync(scope, "Tarih");
        var kasa = await HesapAsync(scope, "MRK", "Merkez Kasa", "Kasa");

        // Geçmiş tarih kabul (yeni ekranın "Tarih" alanı).
        var gecmis = Gun(-3);
        var id = await cash.CollectAsync(new CashInput
        { CariId = cari, Tutar = 100m, Tarih = gecmis, Hesap = LedgerAccountType.Kasa, HesapId = kasa });
        Assert.Equal(gecmis, (await cash.GetAsync(id))!.Tarih);

        // Gelecek tarih REDDEDİLİR — TarihPolitikasi para yolunda geçerli, yeni yüzey onu delmiyor.
        await Assert.ThrowsAsync<ValidationException>(() => cash.CollectAsync(new CashInput
        { CariId = cari, Tutar = 100m, Tarih = Gun(3), Hesap = LedgerAccountType.Kasa, HesapId = kasa }));
    }

    // ---------------------------------------------------------------- Süzgeçler

    [Fact]
    public async Task Islem_listesi_suzgecleri_daraltir_boşken_daraltmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var ahmet = await CariAsync(scope, "Ahmet", "OZL-A");
        var mehmet = await CariAsync(scope, "Mehmet", "OZL-M");
        var kasa = await HesapAsync(scope, "MRK", "Merkez Kasa", "Kasa");
        var banka = await HesapAsync(scope, "ZR", "Ziraat", "Banka");

        // ELLE: Ahmet'e 2 (kasa tahsilat bugün + banka ödeme 10 gün önce), Mehmet'e 1 (kasa tahsilat).
        await cash.CollectAsync(new CashInput
        { CariId = ahmet, Tutar = 300m, Hesap = LedgerAccountType.Kasa, HesapId = kasa, Kanal = "Mobil" });
        await cash.PayAsync(new CashInput
        { CariId = ahmet, Tutar = 50m, Tarih = Gun(-10), Hesap = LedgerAccountType.Banka, HesapId = banka });
        await cash.CollectAsync(new CashInput
        { CariId = mehmet, Tutar = 120m, Hesap = LedgerAccountType.Kasa, HesapId = kasa });

        // Süzgeçsiz: 3.
        Assert.Equal(3, (await cash.SearchIslemlerAsync()).Count);
        // Cari adı araması.
        Assert.Equal(2, (await cash.SearchIslemlerAsync(new CashFilter { Ara = "Ahmet" })).Count);
        // Özel kod araması.
        Assert.Single(await cash.SearchIslemlerAsync(new CashFilter { Ara = "OZL-M" }));
        // Tip.
        Assert.Single(await cash.SearchIslemlerAsync(new CashFilter { Tip = CashTransactionType.Odeme }));
        // Hesap türü.
        Assert.Equal(2, (await cash.SearchIslemlerAsync(new CashFilter { Hesap = LedgerAccountType.Kasa })).Count);
        // Spesifik hesap.
        Assert.Single(await cash.SearchIslemlerAsync(new CashFilter { HesapId = banka }));
        // Kanal.
        Assert.Single(await cash.SearchIslemlerAsync(new CashFilter { Kanal = "Mobil" }));
        // Tarih penceresi: son 7 gün → 10 gün önceki ödeme düşer.
        Assert.Equal(2, (await cash.SearchIslemlerAsync(new CashFilter { Bas = Gun(-7) })).Count);
        // Boş süzgeç daraltmaz.
        Assert.Equal(3, (await cash.SearchIslemlerAsync(new CashFilter { Ara = "", Kanal = "" })).Count);
    }

    [Fact]
    public async Task Cari_adi_ve_ozel_kodu_satirda_cozuluyor()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = await CariAsync(scope, "Zeynep", "OZL-Z");
        var kasa = await HesapAsync(scope, "MRK", "Merkez Kasa", "Kasa");
        await cash.CollectAsync(new CashInput
        { CariId = cari, Tutar = 90m, Hesap = LedgerAccountType.Kasa, HesapId = kasa });

        var satir = Assert.Single(await cash.SearchIslemlerAsync());
        Assert.Equal("Zeynep Test", satir.CariAd);
        Assert.Equal("OZL-Z", satir.CariKod);
        Assert.Equal(90m, satir.Islem.Amount.Amount);
    }

    [Fact]
    public async Task Suzgec_para_toplamlarini_DEGISTIRMEZ()
    {
        // Kırılgan regresyon: liste süzgeci yalnız GÖSTERİMİ daraltır; kasa/banka özeti sabit kalır.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var reports = scope.ServiceProvider.GetRequiredService<RentACar.Application.Reporting.ReportService>();
        var cari = await CariAsync(scope, "Toplam");
        var kasa = await HesapAsync(scope, "MRK", "Merkez Kasa", "Kasa");

        await cash.CollectAsync(new CashInput
        { CariId = cari, Tutar = 400m, Hesap = LedgerAccountType.Kasa, HesapId = kasa });
        await cash.PayAsync(new CashInput
        { CariId = cari, Tutar = 150m, Hesap = LedgerAccountType.Kasa, HesapId = kasa });

        // Elle: 400 − 150 = 250.
        Assert.Equal(250m, (await reports.GetKasaBankaSummaryAsync()).KasaBakiye);
        Assert.Single(await cash.SearchIslemlerAsync(new CashFilter { Tip = CashTransactionType.Odeme }));
        Assert.Equal(250m, (await reports.GetKasaBankaSummaryAsync()).KasaBakiye);
    }

    // ---------------------------------------------------------------- İzolasyon / yetki

    [Fact]
    public async Task Islem_listesi_tenant_izolasyonu_ve_yetki()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
        {
            var cash = s1.ServiceProvider.GetRequiredService<CashService>();
            var cari = await CariAsync(s1, "T1 Gizli", "OZL-T1");
            var kasa = await HesapAsync(s1, "MRK", "Merkez Kasa", "Kasa");
            await cash.CollectAsync(new CashInput
            { CariId = cari, Tutar = 500m, Hesap = LedgerAccountType.Kasa, HesapId = kasa });
        }

        // racar_app ile bağlanan T2 bağlamı T1'in işlemini ve cari adını GÖRMEZ.
        using var s2 = host.ScopeFor(t2);
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<CashService>().SearchIslemlerAsync());
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<CashService>()
            .SearchIslemlerAsync(new CashFilter { Ara = "T1 Gizli" }));

        // Operatör listeyi göremez (ViewReports yok).
        using var op = host.ScopeFor(t1, role: UserRole.Operator);
        await Assert.ThrowsAsync<YetkiYokException>(
            () => op.ServiceProvider.GetRequiredService<CashService>().SearchIslemlerAsync());
    }
}
