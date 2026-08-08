using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Regulation;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-75 — sigorta/muayene birleşik belge envanteri.
///
/// <para><b>BELGESİZ ARAÇ DA SATIR ALIR</b> — eksik belge bu raporun asıl konusudur. JOIN'i
/// "belgesi olanlar" diye kursaydık rapor tam da göstermesi gereken şeyi gizlerdi.</para>
///
/// <para><b>/vade panosunun yerine geçmez:</b> orası kova+kalan gün akışı, burası araç bazlı
/// envanter. İkisi ayrı sorulardır ve ikisi de duruyor.</para>
///
/// <para>Bağımsız oracle: beklenen tarihler/sayımlar senaryodan ELLE yazılır.</para>
/// </summary>
[Collection("postgres")]
public sealed class SigortaMuayeneRaporuTests(PostgresFixture fx)
{
    private static DateTimeOffset D(int y, int m, int g) => new(y, m, g, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Belgesiz_arac_DA_satir_alir_ve_belgeler_TEK_satirda_birlesir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var araclar = sp.GetRequiredService<VehicleService>();
        var reg = sp.GetRequiredService<RegulationService>();

        var v1 = await araclar.CreateAsync(new VehicleInput
        { Plaka = "34 SM 01", Marka = "Fiat", Tip = "Egea", ModelYili = 2023, AracSahibi = "Bizim" });
        var v2 = await araclar.CreateAsync(new VehicleInput
        { Plaka = "34 SM 02", Marka = "Renault", AracSahibi = "Dış" });   // HİÇ BELGESİ YOK

        await reg.AddInsuranceAsync(v1, InsuranceType.Trafik, D(2026, 1, 1), D(2027, 1, 1), 5000m, null, null, null);
        await reg.AddInsuranceAsync(v1, InsuranceType.Kasko, D(2026, 1, 1), D(2026, 12, 31), 15000m, null, null, null);
        await reg.AddInspectionAsync(v1, D(2026, 3, 1), D(2028, 3, 1), 800m);
        await reg.AddMtvAsync(v1, "2026/1", 2000m, D(2026, 1, 31));

        var rows = await sp.GetRequiredService<ReportService>().GetSigortaMuayeneAsync();
        Assert.Equal(2, rows.Count);

        var a = rows.Single(r => r.Plaka == "34SM01");
        Assert.Equal("Fiat", a.Marka);
        Assert.Equal(2023, a.ModelYili);
        Assert.Equal(D(2027, 1, 1), a.TrafikBitis);
        Assert.Equal(D(2026, 12, 31), a.KaskoBitis);
        Assert.Equal(D(2028, 3, 1), a.MuayeneBitis);
        Assert.Equal(D(2026, 1, 31), a.MtvVade);
        Assert.False(a.MtvOdendi);          // henüz ödenmedi

        // BELGESİZ ARAÇ: satır var, alanlar null.
        var b = rows.Single(r => r.Plaka == "34SM02");
        Assert.Null(b.TrafikBitis);
        Assert.Null(b.KaskoBitis);
        Assert.Null(b.MuayeneBitis);
        Assert.Null(b.MtvVade);
        Assert.False(b.MtvOdendi);          // MTV kaydı hiç yok → "ödendi" DEĞİL
    }

    [Fact]
    public async Task Yenilenen_police_EN_GEC_biteni_gosterir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 SM 03" });
        var reg = sp.GetRequiredService<RegulationService>();

        // ELLE: eski poliçe 2025 sonunda bitiyor, yenisi 2027'de. Rapor GEÇERLİ olanı göstermeli.
        await reg.AddInsuranceAsync(v, InsuranceType.Trafik, D(2024, 1, 1), D(2025, 12, 31), 4000m, null, null, null);
        await reg.AddInsuranceAsync(v, InsuranceType.Trafik, D(2026, 1, 1), D(2027, 1, 1), 5000m, null, null, null);

        var r = Assert.Single(await sp.GetRequiredService<ReportService>().GetSigortaMuayeneAsync());
        Assert.Equal(D(2027, 1, 1), r.TrafikBitis);
    }

    [Fact]
    public async Task MTV_ACIK_olanin_EN_YAKIN_vadesi_gosterilir_hepsi_odendiyse_ODENDI()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var reg = sp.GetRequiredService<RegulationService>();
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 SM 04" });

        // ELLE: iki dönem — 1. ödendi, 2. açık. Rapor AÇIK olanın vadesini göstermeli.
        var m1 = await reg.AddMtvAsync(v, "2026/1", 1000m, D(2026, 1, 31));
        await reg.AddMtvAsync(v, "2026/2", 1000m, D(2026, 7, 31));
        await reg.MtvOdeAsync(m1, LedgerAccountType.Kasa, odemeTarih: DateTimeOffset.UtcNow.AddDays(-1));

        var rapor = sp.GetRequiredService<ReportService>();
        var r = Assert.Single(await rapor.GetSigortaMuayeneAsync());
        Assert.Equal(D(2026, 7, 31), r.MtvVade);
        Assert.False(r.MtvOdendi);

        // İkinci dönem de ödenince ÖDENDİ ve en geç vade bilgi olarak kalır.
        var acik = (await reg.ListMtvAsync()).Single(x => !x.Odendi);
        await reg.MtvOdeAsync(acik.Id, LedgerAccountType.Kasa, odemeTarih: DateTimeOffset.UtcNow.AddDays(-1));
        var r2 = Assert.Single(await rapor.GetSigortaMuayeneAsync());
        Assert.True(r2.MtvOdendi);
        Assert.Equal(D(2026, 7, 31), r2.MtvVade);
    }

    [Fact]
    public async Task Filtreler_ELLE_beklenen_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var araclar = sp.GetRequiredService<VehicleService>();
        var reg = sp.GetRequiredService<RegulationService>();

        var v1 = await araclar.CreateAsync(new VehicleInput { Plaka = "34 FL 01", AracSahibi = "Bizim" });
        var v2 = await araclar.CreateAsync(new VehicleInput { Plaka = "34 FL 02", AracSahibi = "Dış" });
        var v3 = await araclar.CreateAsync(new VehicleInput { Plaka = "06 XY 03", AracSahibi = "Bizim" });

        // ELLE: v1 trafiği 2026 sonunda, v2 trafiği 2030'da, v3 trafiği YOK.
        await reg.AddInsuranceAsync(v1, InsuranceType.Trafik, D(2026, 1, 1), D(2026, 12, 31), 1m, null, null, null);
        await reg.AddInsuranceAsync(v2, InsuranceType.Trafik, D(2026, 1, 1), D(2030, 1, 1), 1m, null, null, null);

        var svc = sp.GetRequiredService<ReportService>();
        Assert.Equal(3, (await svc.GetSigortaMuayeneAsync()).Count);

        // Araç sahibi (harf duyarsız)
        Assert.Equal(2, (await svc.GetSigortaMuayeneAsync(new SigortaMuayeneFilter { AracSahibi = "bizim" })).Count);
        Assert.Single(await svc.GetSigortaMuayeneAsync(new SigortaMuayeneFilter { AracSahibi = "Dış" }));

        // Plaka (boşluklu giriş normalize)
        Assert.Equal(2, (await svc.GetSigortaMuayeneAsync(new SigortaMuayeneFilter { Plaka = "34 FL" })).Count);
        Assert.Single(await svc.GetSigortaMuayeneAsync(new SigortaMuayeneFilter { Plaka = "06xy03" }));

        // Tür + bitiş: trafiği 2027 öncesi bitenler VE trafiği HİÇ OLMAYANLAR (eksik belge de
        // raporun konusu) → v1 + v3 = 2.
        var vade = await svc.GetSigortaMuayeneAsync(new SigortaMuayeneFilter
        { Tur = SigortaMuayeneTur.Trafik, BitisEnGec = D(2027, 1, 1) });
        Assert.Equal(2, vade.Count);
        Assert.DoesNotContain(vade, r => r.Plaka == "34FL02");
    }

    [Fact]
    public async Task Rapor_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
            await s1.ServiceProvider.GetRequiredService<VehicleService>()
                .CreateAsync(new VehicleInput { Plaka = "34 GZ 01" });

        using var s2 = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<ReportService>().GetSigortaMuayeneAsync());
    }
}
