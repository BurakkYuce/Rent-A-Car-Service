using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Finance;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Reports;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kabul bulguları d-rapor-gunluk-04 / d-rapor-arac-gunluk-04 / d-rapor-karsilastirmali-02/04: export dosyası
/// ekrandakiyle AYNI İstanbul gününü kullanmalı ("gördüğün = indirdiğin"). Beklenen sayılar elle kurulan
/// kayıtlardan yazılır (1 tahsilat × 500, 1 kira), rapor kodundan türetilmez.
/// </summary>
public sealed partial class UiReportTests
{
    /// <summary>İstanbul gününün başlangıç anı (UTC) — test kendi hesaplar, üretim yardımcısını çağırmaz.</summary>
    private static DateTimeOffset IstanbulDayStart(DateOnly day)
        => new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), TenantDay.Slice), TimeSpan.Zero);

    private static async Task<string[]> CsvLines(Session s, string url)
    {
        var res = await s.C.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadAsStringAsync()).TrimStart('﻿')
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    private static string Csv(string excelLink) => excelLink.Replace("format=excel", "format=csv");

    private static decimal CsvValue(string[] lines, string label)
        => decimal.Parse(lines.Single(l => l.StartsWith(label + ",", StringComparison.Ordinal)).Split(',')[1],
            CultureInfo.InvariantCulture);

    [Fact]
    public async Task Daily_activity_export_uses_the_screen_day()
    {
        var e = await SetupAsync(ledger: false);
        var s = await LoginAsync(e, Who.Admin);
        // Dün (İstanbul) öğlen UTC — her saatte geçmişte kalır; ekran günü UTC takvim günü çıpasıyla sayar.
        var day = TenantDay.Day(DateTimeOffset.UtcNow).AddDays(-1);
        var at = new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        using (var host = new TestHost(fx.Pg.AppConnectionString))
        using (var scope = host.ScopeFor(e.TenantId))
            await scope.ServiceProvider.GetRequiredService<CashService>()
                .CollectAsync(new CashInput { CariId = e.CustomerA, Tutar = 500m, Tarih = at });

        var screen = await GetJson(s, Report + $"/gunluk?gun={day:yyyy-MM-dd}");
        Assert.Equal(1, screen.GetProperty("ozet").GetProperty("tahsilatAdet").GetInt32());

        var lines = await CsvLines(s, Csv(screen.GetProperty("export").GetProperty("excel").GetString()!));
        Assert.Equal(1m, CsvValue(lines, "Tahsilat Adet"));
        Assert.Equal(500m, CsvValue(lines, "Tahsilat Tutar"));
    }

    [Fact]
    public async Task Vehicle_daily_status_export_uses_the_screen_day()
    {
        var e = await SetupAsync(ledger: false);
        var s = await LoginAsync(e, Who.Admin);
        var day = TenantDay.Day(DateTimeOffset.UtcNow).AddDays(-1);
        using (var host = new TestHost(fx.Pg.AppConnectionString))
        using (var scope = host.ScopeFor(e.TenantId))
            await scope.ServiceProvider.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
            {
                MusteriId = e.CustomerA, VehicleId = e.VehicleA, GunlukUcret = 100m,
                BasTar = new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero),
                BitTar = new DateTimeOffset(day.AddDays(3).ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero),
            });

        var screen = await GetJson(s, Report + $"/arac-gunluk-durum?gun={day:yyyy-MM-dd}");
        var plate = Assert.Single(screen.GetProperty("satirlar").GetProperty("kayitlar").EnumerateArray())
            .GetProperty("plaka").GetString()!;

        var lines = await CsvLines(s, Csv(screen.GetProperty("export").GetProperty("excel").GetString()!));
        Assert.Contains(lines, l => l.StartsWith(plate + ",", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ayın 1'i → bugün dönemi tek ay sütunu verir (UTC'ye çevrilmiş dönem başı önceki ayı açıyordu); bugün (İstanbul
    /// günü başında) başlayan kira hem ekranda hem CSV'de sayılır; Excel 500 vermez (sayfa adında "/" vardı).
    /// </summary>
    [Fact]
    public async Task Comparative_analysis_month_columns_and_exports_match_the_screen()
    {
        var e = await SetupAsync(ledger: false);
        var s = await LoginAsync(e, Who.Admin);
        var today = TenantDay.Day(DateTimeOffset.UtcNow);
        var first = new DateOnly(today.Year, today.Month, 1);
        using (var host = new TestHost(fx.Pg.AppConnectionString))
        using (var scope = host.ScopeFor(e.TenantId))
            await scope.ServiceProvider.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
            {
                MusteriId = e.CustomerA, VehicleId = e.VehicleA, GunlukUcret = 100m,
                BasTar = IstanbulDayStart(today).AddMinutes(1),
                BitTar = IstanbulDayStart(today).AddDays(2),
            });

        var screen = await GetJson(s, Report + $"/karsilastirmali-analiz?bas={first:yyyy-MM-dd}&bit={today:yyyy-MM-dd}");
        var summary = screen.GetProperty("ozet");
        Assert.Equal([$"{today:yyyy-MM}"], summary.GetProperty("ayAnahtarlari").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(1m, summary.GetProperty("genelToplam").GetDecimal());

        var excel = screen.GetProperty("export").GetProperty("excel").GetString()!;
        var lines = await CsvLines(s, Csv(excel));
        var total = lines.Single(l => l.StartsWith("Toplam,", StringComparison.Ordinal)).Split(',');
        Assert.Equal(1m, decimal.Parse(total[^1], CultureInfo.InvariantCulture));

        var file = await s.C.GetAsync(excel);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
    }

    /// <summary>
    /// Kabul bulgusu d-rapor-karlilik-02: "KDV dahil" bayrağı hiçbir şeyi değiştirmiyordu ve kaldırıldı. KDV referans
    /// bilgisi ekranda bayraktan bağımsız (kart + sütun) durduğu için export'ta da bayraksız gelir; P&amp;L NET kalır.
    /// Elle: 4 gün × 300 = 1.200 brüt, %20 → net 1.000 + KDV 200; Gelir 1.000, Gelir KDV Dahil 1.200.
    /// </summary>
    [Fact]
    public async Task Profitability_export_carries_vat_reference_without_the_flag()
    {
        var e = await SetupAsync(ledger: false);
        var s = await LoginAsync(e, Who.Admin);
        using (var host = new TestHost(fx.Pg.AppConnectionString))
        using (var scope = host.ScopeFor(e.TenantId))
        {
            var sp = scope.ServiceProvider;
            var rentalId = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
            {
                MusteriId = e.CustomerA, VehicleId = e.VehicleA, GunlukUcret = 300m,
                BasTar = TestZaman.DaysLater(-9), BitTar = TestZaman.DaysLater(-5),
            });
            await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(rentalId, vatRate: 0.20m);
        }

        var screen = await GetJson(s, Report + "/karlilik");
        var row = Assert.Single(screen.GetProperty("satirlar").GetProperty("kayitlar").EnumerateArray(),
            r => r.TryGetProperty("vehicleId", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
                 && v.GetGuid() == e.VehicleA);
        Assert.Equal(1000m, Dec(row, "gelir"));
        Assert.Equal(1200m, Dec(row, "gelirKdvDahil"));

        var lines = await CsvLines(s, Csv(screen.GetProperty("export").GetProperty("excel").GetString()!));
        var headers = lines[0].Split(',');
        var plate = row.GetProperty("plaka").GetString()!;
        var cells = lines.Single(l => l.StartsWith(plate + ",", StringComparison.Ordinal)).Split(',');
        Assert.Equal(1000m, decimal.Parse(cells[Array.IndexOf(headers, "Gelir")], CultureInfo.InvariantCulture));
        Assert.Equal(200m, decimal.Parse(cells[Array.IndexOf(headers, "Hesaplanan KDV (ref.)")], CultureInfo.InvariantCulture));
        Assert.Equal(1200m, decimal.Parse(cells[Array.IndexOf(headers, "Gelir KDV Dahil (ref.)")], CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Excel_sheet_name_is_cleaned_to_excel_rules()
    {
        // Elle: yasak karakterler (\ / ? * [ ] :) '-' olur, 31 karakterde kesilir, sondaki boşluk atılır.
        Assert.Equal("Karşılaştırmalı Analiz (Kira -", ExcelSheetName.Clean("Karşılaştırmalı Analiz (Kira / Adet)"));
        Assert.Equal("a-b-c-d-e-f-g-h", ExcelSheetName.Clean("a\\b/c?d*e[f]g:h"));
        Assert.Equal("Rapor", ExcelSheetName.Clean("  "));
        Assert.Equal("Rapor", ExcelSheetName.Clean("'"));
        Assert.Equal("Kasa", ExcelSheetName.Clean("'Kasa'"));
        Assert.Equal("history-", ExcelSheetName.Clean("history"));
        Assert.Equal(31, ExcelSheetName.Clean(new string('x', 40)).Length);

        var bytes = new ReportExportService().Xlsx("Kârlılık (Araç Grubu / Şube: Merkez)", ["A"], [[1m]]);
        Assert.NotEmpty(bytes);
    }
}
