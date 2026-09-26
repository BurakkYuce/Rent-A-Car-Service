using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Legal;
using RentACar.Application.Reporting;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap C2 — Hukuk dosyası master. BAĞIMSIZ ORACLE: CRUD; DosyaNo benzersizliği; tenant izolasyon;
/// yetki (OperationsWrite olmayan rol reddedilir).
/// FAZ-41: alan derinliği (fatura no + 2 avukat iletişimi + Tahsilat/Kalan), arama süzgeci ve
/// <b>"Tahsilat deftere yazmaz"</b> kırılgan regresyon kilidi.
/// </summary>
[Collection("postgres")]
public sealed class HukukTests(PostgresFixture fx)
{
    private static HukukDosyaInput Input(string no) => new()
    {
        DosyaNo = no, Tur = LegalType.Icra, Avukat = "Av. Demir", Tutar = 15000m,
        Durum = LegalStatus.Acik, Tarih = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), Aciklama = "İcra takibi"
    };

    [Fact]
    public async Task Crud_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<LegalCaseService>();

        var id = await svc.CreateAsync(Input("2026/123"));
        var h = await svc.GetAsync(id);
        Assert.NotNull(h);
        Assert.Equal("2026/123", h!.DosyaNo);
        Assert.Equal(LegalType.Icra, h.Tur);
        Assert.Equal(15000m, h.Tutar);
        Assert.Equal(LegalStatus.Acik, h.Durum);

        Assert.True(await svc.UpdateAsync(id, new HukukDosyaInput
        { DosyaNo = "2026/123", Tur = LegalType.Icra, Tutar = 15000m, Durum = LegalStatus.Kapali }));
        Assert.Equal(LegalStatus.Kapali, (await svc.GetAsync(id))!.Durum);

        Assert.Single(await svc.ListAsync());
        Assert.True(await svc.DeleteAsync(id));
        Assert.Empty(await svc.ListAsync());
    }

    [Fact]
    public async Task Duplicate_dosyano_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<LegalCaseService>();

        await svc.CreateAsync(Input("2026/999"));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Input("2026/999")));
    }

    [Fact]
    public async Task Tenant_isolation()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<LegalCaseService>().CreateAsync(Input("2026/123"));

        using var s2 = host.ScopeFor(t2);
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<LegalCaseService>().ListAsync());
    }

    [Fact]
    public async Task Non_operations_role_denied()
    {
        using var host = new TestHost(fx.AppConnectionString);
        // Muhasebe: FinanceWrite/ViewReports var, OperationsWrite YOK → yazma reddedilir.
        using var scope = host.ScopeFor(Guid.NewGuid(), role: UserRole.Muhasebe);
        await Assert.ThrowsAsync<NoPermissionException>(
            () => scope.ServiceProvider.GetRequiredService<LegalCaseService>().CreateAsync(Input("2026/1")));
    }

    // ---------------- FAZ-41 ----------------

    /// <summary>
    /// 7 yeni alanın round-trip'i + Kalan. BAĞIMSIZ ORACLE: Tutar 1000, Tahsilat 300 elle kurulur →
    /// Kalan 700 SABİTİ beklenir (formül testte yeniden üretilmez).
    /// </summary>
    [Fact]
    public async Task Yeni_alanlar_roundtrip_ve_kalan()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<LegalCaseService>();

        var id = await svc.CreateAsync(new HukukDosyaInput
        {
            DosyaNo = "2026/41", Tur = LegalType.Icra, Tutar = 1000m, Tahsilat = 300m,
            FaturaNoTemp = "FTR-77", Avukat = "Av. Demir", AvukatTel = "0212 111 22 33",
            AvukatMail = "demir@ornek.com", Avukat2Ad = "Av. Yılmaz", Avukat2Tel = "0532 444 55 66",
            Avukat2Mail = "yilmaz@ornek.com"
        });

        var h = (await svc.GetAsync(id))!;
        Assert.Equal("FTR-77", h.FaturaNoTemp);
        Assert.Equal("0212 111 22 33", h.AvukatTel);
        Assert.Equal("demir@ornek.com", h.AvukatMail);
        Assert.Equal("Av. Yılmaz", h.Avukat2Ad);
        Assert.Equal("0532 444 55 66", h.Avukat2Tel);
        Assert.Equal("yilmaz@ornek.com", h.Avukat2Mail);
        Assert.Equal(300m, h.Tahsilat);
        Assert.Equal(700m, h.Kalan);   // 1000 − 300 (elle kurulmuş sabit)

        // Tahsilat temizlenebilir: null → Kalan tutarın kendisi.
        await svc.UpdateAsync(id, new HukukDosyaInput { DosyaNo = "2026/41", Tutar = 1000m, Tahsilat = null });
        var h2 = (await svc.GetAsync(id))!;
        Assert.Null(h2.Tahsilat);
        Assert.Equal(1000m, h2.Kalan);
    }

    /// <summary>Negatif tahsilat işaret hatasıdır → red. Tutarı AŞAN tahsilat ise meşrudur (faiz/masraf).</summary>
    [Fact]
    public async Task Negatif_tahsilat_reddedilir_fazla_tahsilat_kabul()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<LegalCaseService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new HukukDosyaInput { DosyaNo = "2026/NEG", Tutar = 100m, Tahsilat = -1m }));

        var id = await svc.CreateAsync(new HukukDosyaInput { DosyaNo = "2026/FAZ", Tutar = 100m, Tahsilat = 150m });
        Assert.Equal(-50m, (await svc.GetAsync(id))!.Kalan); // 100 − 150 (sıfıra kırpılmaz)
    }

    /// <summary>
    /// KIRILGAN REGRESYON KİLİDİ (KARARLAR.md genel politikası): hukuk dosyasına UÇUK bir Tahsilat
    /// yazmak defter satır sayısını ve cari bakiyeyi DEĞİŞTİRMEZ.
    ///
    /// <para>Bağımsız oracle: cariye Kasa'dan 700 TL tahsilat girilir → bakiye −700 (alacaklı) ve
    /// 2 defter satırı (çift-taraflı). Ardından hukuk dosyasına Tahsilat=987.654.321 yazılır;
    /// iki sayı da AYNEN kalmalı. Bu satır bir gün kırmızıya dönerse hukuk ekranı sessizce
    /// muhasebeleşmiş demektir.</para>
    /// </summary>
    [Fact]
    public async Task Tahsilat_deftere_yazmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        var account = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Hukuk", Soyad = "Müşterisi" });
        await sp.GetRequiredService<CashService>().CollectAsync(new CashInput
        { CariId = account, Tutar = 700m, Hesap = LedgerAccountType.Kasa });

        var db = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        async Task<int> RowCountAsync()
        {
            await using var c = await db.CreateDbContextAsync();
            return await c.AccountLedgerEntries.AsNoTracking().CountAsync();
        }
        async Task<decimal> BalanceAsync()
            => (await sp.GetRequiredService<ReportService>().GetAccountBalancesAsync())
                .Where(b => b.CariId == account).Sum(b => b.Bakiye);

        var rowBefore = await RowCountAsync();
        var balanceBefore = await BalanceAsync();
        Assert.Equal(2, rowBefore);        // tahsilat = 1 borç + 1 alacak
        Assert.Equal(-700m, balanceBefore);   // müşteri alacaklı (Credit → negatif)

        var svc = sp.GetRequiredService<LegalCaseService>();
        var file = await svc.CreateAsync(new HukukDosyaInput
        { DosyaNo = "2026/LEDGER", CariId = account, Tutar = 5_000m, Tahsilat = 987_654_321m });
        await svc.UpdateAsync(file, new HukukDosyaInput
        { DosyaNo = "2026/LEDGER", CariId = account, Tutar = 9_999_999m, Tahsilat = 123_456_789m });

        Assert.Equal(rowBefore, await RowCountAsync());  // defter büyümedi
        Assert.Equal(balanceBefore, await BalanceAsync());      // cari bakiye kıpırdamadı
    }

    /// <summary>
    /// Liste süzgeci. BAĞIMSIZ ORACLE: 3 dosya elle kurulur (Ali'nin 2'si, Veli'nin 1'i; fatura no
    /// yalnız birinde). Beklenen satır sayıları elle sayılmıştır.
    /// </summary>
    [Fact]
    public async Task Arama_cari_fatura_dosya_ve_tarih()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cust = sp.GetRequiredService<CustomerService>();
        var ali = await cust.CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Ali", Soyad = "Veli" });
        var ayse = await cust.CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Ayşe", Soyad = "Kaya" });
        var svc = sp.GetRequiredService<LegalCaseService>();

        await svc.CreateAsync(new HukukDosyaInput
        { DosyaNo = "2026/A1", CariId = ali, Tutar = 100m, FaturaNoTemp = "FTR-1", Tarih = D(2026, 1, 10) });
        await svc.CreateAsync(new HukukDosyaInput
        { DosyaNo = "2026/A2", CariId = ali, Tutar = 200m, Tarih = D(2026, 5, 10), Avukat = "Av. Özdemir" });
        await svc.CreateAsync(new HukukDosyaInput
        { DosyaNo = "2027/B1", CariId = ayse, Tutar = 300m, FaturaNoTemp = "FTR-2", Tarih = D(2026, 9, 10) });

        Assert.Equal(3, (await svc.SearchAsync()).Count);
        Assert.Equal(2, (await svc.SearchAsync(new HukukDosyaFilter { CariId = ali })).Count);

        var tekAyse = Assert.Single(await svc.SearchAsync(new HukukDosyaFilter { CariId = ayse }));
        Assert.Equal("2027/B1", tekAyse.Dosya.DosyaNo);
        Assert.Equal("Ayşe Kaya", tekAyse.MusteriAd);   // ad dosyada saklanmaz, cariden çözülür

        Assert.Equal("2026/A1", Assert.Single(
            await svc.SearchAsync(new HukukDosyaFilter { FaturaNo = "ftr-1" })).Dosya.DosyaNo); // harf duyarsız
        Assert.Equal(2, (await svc.SearchAsync(new HukukDosyaFilter { DosyaNo = "2026/" })).Count);
        Assert.Equal("2026/A2", Assert.Single(
            await svc.SearchAsync(new HukukDosyaFilter { Ara = "özdemir" })).Dosya.DosyaNo);

        // Tarih aralığı: 01.02.2026 – 30.06.2026 → yalnız 2026/A2 (10.05).
        Assert.Equal("2026/A2", Assert.Single(await svc.SearchAsync(
            new HukukDosyaFilter { Bas = D(2026, 2, 1), Bit = D(2026, 6, 30) })).Dosya.DosyaNo);
    }

    /// <summary>Süzgeçli liste de tenant sınırını aşmaz (racar_app + RLS).</summary>
    [Fact]
    public async Task Arama_tenant_izolasyonu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
            await s1.ServiceProvider.GetRequiredService<LegalCaseService>().CreateAsync(Input("2026/ISO"));

        using var s2 = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<LegalCaseService>()
            .SearchAsync(new HukukDosyaFilter { DosyaNo = "2026" }));
    }

    private static DateTimeOffset D(int y, int m, int d) => new(y, m, d, 0, 0, 0, TimeSpan.Zero);
}
