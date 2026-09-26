using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Reporting;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Finans Analiz 12-ay trend metodu (GetAylikGelirGiderTrendAsync). Bağımsız oracle: defter satırları
/// ELLE kurulan tarih/tutarlarla doğrudan yazılır (insert serbest; immutability yalnız update/delete
/// engeller), beklenen (AyBas, Gelir, Gider, NetKar) üçlüleri girdilerden elle türetilir.
/// Ay çıpaları deterministik olsun diye `simdi` parametresi sabitlenir.
/// </summary>
[Collection("postgres")]
public sealed class FinansTrendTests(PostgresFixture fx)
{
    private static IDbContextFactory<AppDbContext> Factory(IServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

    private static AccountLedgerEntry Satir(LedgerAccountType tip, LedgerDirection yon, decimal tutar,
        DateTimeOffset tarih, string kaynak = "Fatura") => new()
    {
        AccountType = tip, Direction = yon, Amount = new Money(tutar, "TRY", 1m),
        EntryDateUtc = tarih, SourceType = kaynak, SourceId = Guid.NewGuid()
    };

    [Fact]
    public async Task Aylik_gelir_gider_trend_elle_hesapli_degerleri_verir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();

        // Sabit "şimdi": 2026-06-15 → pencereler Nis/May/Haz 2026 (3 ay isteyeceğiz).
        var simdi = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var nisan = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var mayis = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var haziran = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        await using (var db = await Factory(scope).CreateDbContextAsync())
        {
            // NİSAN: fatura geliri 1000.
            db.AccountLedgerEntries.Add(Satir(LedgerAccountType.Gelir, LedgerDirection.Credit, 1000m, nisan.AddDays(9)));
            // MAYIS ay-SINIRI kaydı: tam 1 Mayıs 00:00:00 UTC → YALNIZ Mayıs'a sayılmalı (Nisan üst ucu AddTicks(-1)).
            db.AccountLedgerEntries.Add(Satir(LedgerAccountType.Gelir, LedgerDirection.Credit, 2000m, mayis));
            // MAYIS: gider 400 + gelir İADESİ 100 (Debit → gelirden düşer).
            db.AccountLedgerEntries.Add(Satir(LedgerAccountType.Gider, LedgerDirection.Debit, 400m, mayis.AddDays(4), "Gider"));
            db.AccountLedgerEntries.Add(Satir(LedgerAccountType.Gelir, LedgerDirection.Debit, 100m, mayis.AddDays(7)));
            await db.SaveChangesAsync();
        }

        var trend = await reports.GetMonthlyRevenueExpenseTrendAsync(3, simdi);

        Assert.Equal(3, trend.Count);
        // Çıpalar: Nisan, Mayıs, Haziran 1'i (UTC) — sırayla.
        Assert.Equal(nisan, trend[0].AyBas);
        Assert.Equal(mayis, trend[1].AyBas);
        Assert.Equal(haziran, trend[2].AyBas);

        // NİSAN (elle): gelir 1000 (sınır kaydı DAHİL DEĞİL), gider 0, net 1000.
        Assert.Equal(1000m, trend[0].Gelir);
        Assert.Equal(0m, trend[0].Gider);
        Assert.Equal(1000m, trend[0].NetKar);

        // MAYIS (elle): gelir 2000 − 100 iade = 1900; gider 400; net 1500.
        Assert.Equal(1900m, trend[1].Gelir);
        Assert.Equal(400m, trend[1].Gider);
        Assert.Equal(1500m, trend[1].NetKar);

        // HAZİRAN (elle): boş ay → sıfırlar, çıpa doğru.
        Assert.Equal(0m, trend[2].Gelir);
        Assert.Equal(0m, trend[2].Gider);
        Assert.Equal(0m, trend[2].NetKar);
    }

    [Fact]
    public async Task Eski_gelir_trendi_yeni_metotla_ayni_gelirleri_verir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();

        var simdi = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        await using (var db = await Factory(scope).CreateDbContextAsync())
        {
            db.AccountLedgerEntries.Add(Satir(LedgerAccountType.Gelir, LedgerDirection.Credit, 750m,
                new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero)));
            await db.SaveChangesAsync();
        }

        // Delege doğrulaması: eski Home metodu = yeni metodun Gelir izdüşümü.
        var eski = await reports.GetMonthlyRevenueTrendAsync(2, simdi);
        var yeni = await reports.GetMonthlyRevenueExpenseTrendAsync(2, simdi);
        Assert.Equal(yeni.Select(n => (n.AyBas, n.Gelir)), eski.Select(n => (n.AyBas, n.Gelir)));
        Assert.Equal(750m, eski[0].Gelir); // Mayıs (elle)
        Assert.Equal(0m, eski[1].Gelir);   // Haziran boş
    }
}
