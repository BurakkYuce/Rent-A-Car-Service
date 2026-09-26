using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Application.Kur;
using RentACar.Application.Personnel;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// "Sürüm, satır, sürüm" okumasının yeniden deneme döngüsünü GERÇEKTEN çalıştıran testler (2026-09-25).
/// <list type="bullet">
/// <item>Yazım ilk sürüm okuması ile satır okuması ARASINA düşer → döngü bir kez döner; dönen çift yazım SONRASI ve tutarlı.</item>
/// <item>HER sürüm okumasında yazım olur → 3 denemede de tutarsız; ESKİ sürüm satırla döner ve bu sürümle PUT 409 alır
///   (güvenli yön: bayat form başka oturumun değişikliğini ezemez).</item>
/// </list>
/// Yazım sayıları ve beklenen değerler elle sayılmıştır: deneme başına iki sürüm okuması; 3 deneme = 6 okuma = 6 yazım.
/// </summary>
[Collection("postgres")]
public sealed class RowVersionReadRetryTests(PostgresFixture fx)
{
    // ---------------- Vardiya (ShiftApi → PersonelVardiyaService.GetWithStaffAndVersionAsync) ----------------

    /// <summary>Sürüm okumasını gerçek depodan yapar, SONRA (koşula göre) satırı değiştirir ve okuduğu ESKİ sürümü döner —
    /// yani yazım "bu sürüm okuması ile bir sonraki okuma" arasına düşer.</summary>
    private sealed class ShiftWriter(IRowVersionStore inner, Guid target, bool everyRead) : IRowVersionStore
    {
        public int Reads { get; private set; }
        public int Writes { get; private set; }

        public async Task<string?> GetVersionAsync<T>(Guid id, CancellationToken ct = default) where T : class
        {
            var v = await inner.GetVersionAsync<T>(id, ct);
            if (id != target || typeof(T) != typeof(PersonelVardiya)) return v;
            Reads++;
            if (everyRead || Writes == 0)
            {
                Writes++;
                var n = Writes;
                await inner.UpdateAsync<PersonelVardiya>(id, v!, r => r.Aciklama = $"yazim-{n}", "dup", ct);
            }
            return v;
        }

        public Task<bool> UpdateAsync<T>(Guid id, string expectedVersion, Action<T> apply, string duplicateMessage,
            CancellationToken ct = default) where T : class
            => inner.UpdateAsync(id, expectedVersion, apply, duplicateMessage, ct);
    }

    private static async Task<(IServiceScope Scope, Guid Id)> ShiftAsync(TestHost host)
    {
        var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var staff = await sp.GetRequiredService<PersonnelService>().CreateAsync(new PersonelInput { Kod = "P1", Ad = "Ali", Soyad = "Test" });
        var day = DateOnly.FromDateTime(TestZaman.DaysLater(20).UtcDateTime);
        var id = await sp.GetRequiredService<StaffShiftService>().CreateAsync(new VardiyaInput
        {
            PersonelId = staff, Tarih = day, BaslangicSaat = new TimeOnly(8, 0), BitisSaat = new TimeOnly(12, 0), Aciklama = "ilk",
        });
        return (scope, id);
    }

    private static StaffShiftService ShiftService(IServiceProvider sp, IRowVersionStore store)
        => new(sp.GetRequiredService<IPersonnelShiftRepository>(), sp.GetRequiredService<IPersonnelRepository>(),
            sp.GetRequiredService<IBranchRepository>(), sp.GetRequiredService<ICurrentUser>(), store);

    [Fact]
    public async Task Shift_write_between_first_version_and_row_retries_once_and_returns_consistent_pair()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var (scope, id) = await ShiftAsync(host);
        using var _ = scope;
        var inner = scope.ServiceProvider.GetRequiredService<IRowVersionStore>();
        var writer = new ShiftWriter(inner, id, everyRead: false);

        var (row, version) = (await ShiftService(scope.ServiceProvider, writer).GetWithStaffAndVersionAsync(id))!.Value;

        // Elle: deneme 1 = okuma(v0)+yazım, satır, okuma(v1) → farklı; deneme 2 = okuma(v1), satır, okuma(v1) → eşit.
        Assert.Equal(1, writer.Writes);
        Assert.Equal(4, writer.Reads);
        Assert.Equal("yazim-1", row.Vardiya.Aciklama);
        Assert.Equal(await inner.GetVersionAsync<PersonelVardiya>(id), version);
    }

    [Fact]
    public async Task Shift_write_on_every_version_read_returns_older_version_and_put_gets_conflict()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var (scope, id) = await ShiftAsync(host);
        using var _ = scope;
        var inner = scope.ServiceProvider.GetRequiredService<IRowVersionStore>();
        var writer = new ShiftWriter(inner, id, everyRead: true);
        var svc = ShiftService(scope.ServiceProvider, writer);

        var (row, version) = (await svc.GetWithStaffAndVersionAsync(id))!.Value;

        // Elle: 3 deneme × 2 okuma = 6 okuma = 6 yazım. Deneme 3: okuma(v4)+yazım5 → satır "yazim-5" → okuma(v5)+yazım6.
        // Dönen: satır "yazim-5" + ESKİ sürüm v4 (satırdan eski) — güncel sürüm v6.
        Assert.Equal(6, writer.Reads);
        Assert.Equal(6, writer.Writes);
        Assert.Equal("yazim-5", row.Vardiya.Aciklama);
        Assert.NotEqual(await inner.GetVersionAsync<PersonelVardiya>(id), version);

        // Bu sürümle tam değiştirme 409 (güvenli yön); kayıt değişmez.
        var real = scope.ServiceProvider.GetRequiredService<StaffShiftService>();
        await Assert.ThrowsAsync<ConcurrentModificationException>(() => real.UpdateVersionedAsync(id, new VardiyaInput
        {
            PersonelId = row.Vardiya.PersonelId, Tarih = row.Vardiya.Tarih,
            BaslangicSaat = new TimeOnly(9, 0), BitisSaat = new TimeOnly(10, 0), Aciklama = "bayat",
        }, version));
        Assert.Equal("yazim-6", (await real.GetAsync(id))!.Aciklama);
    }

    // ---------------- Sabit kur (FinanceHubApi.Rates → SabitKurService.ListWithVersionsAsync) ----------------

    private sealed class RateWriter(IPinnedRateRepository inner, Guid target, bool everyRead) : IPinnedRateRepository
    {
        public int Reads { get; private set; }
        public int Writes { get; private set; }

        public async Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default)
        {
            var v = await inner.GetVersionAsync(id, ct);
            if (id != target) return v;
            Reads++;
            if (everyRead || Writes == 0)
            {
                Writes++;
                var n = Writes;
                await inner.UpdateAsync(id, v!, s => s.Kur = 30m + n, ct);   // yazım n → kur 30+n
            }
            return v;
        }

        public Task<IReadOnlyList<SabitKur>> ListAsync(CancellationToken ct = default) => inner.ListAsync(ct);
        public Task<SabitKur?> FindAsync(Guid id, CancellationToken ct = default) => inner.FindAsync(id, ct);
        public Task<SabitKur?> GetActiveAsync(string code, DateTimeOffset date, CancellationToken ct = default)
            => inner.GetActiveAsync(code, date, ct);
        public Task<bool> CodeExistsAsync(string code, Guid? excludeId, CancellationToken ct = default)
            => inner.CodeExistsAsync(code, excludeId, ct);
        public Task CreateAsync(SabitKur fixedValue, CancellationToken ct = default) => inner.CreateAsync(fixedValue, ct);
        public Task<bool> UpdateAsync(SabitKur fixedValue, CancellationToken ct = default) => inner.UpdateAsync(fixedValue, ct);
        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) => inner.DeleteAsync(id, ct);
        public Task<bool> UpdateAsync(Guid id, string expectedVersion, Action<SabitKur> apply, CancellationToken ct = default)
            => inner.UpdateAsync(id, expectedVersion, apply, ct);
    }

    private static async Task<(IServiceScope Scope, Guid Id)> RateAsync(TestHost host)
    {
        var scope = host.ScopeFor(Guid.NewGuid());
        var id = await scope.ServiceProvider.GetRequiredService<FixedExchangeRateService>()
            .CreateAsync(new SabitKurInput { Kod = "USD", Kur = 30m, Aktif = true });
        return (scope, id);
    }

    [Fact]
    public async Task Rate_list_write_between_first_version_and_row_retries_once_and_returns_consistent_pair()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var (scope, id) = await RateAsync(host);
        using var _ = scope;
        var inner = scope.ServiceProvider.GetRequiredService<IPinnedRateRepository>();
        var writer = new RateWriter(inner, id, everyRead: false);

        var pair = Assert.Single(await new FixedExchangeRateService(writer, scope.ServiceProvider.GetRequiredService<ICurrentUser>())
            .ListWithVersionsAsync());

        Assert.Equal(1, writer.Writes);
        Assert.Equal(4, writer.Reads);
        Assert.Equal(31m, pair.Row.Kur);                                   // elle: yazım 1 → 30+1
        Assert.Equal(await inner.GetVersionAsync(id), pair.Version);
        // Dönen sürümle PUT geçer (tutarlı çift).
        Assert.True(await scope.ServiceProvider.GetRequiredService<FixedExchangeRateService>()
            .UpdateAsync(id, new SabitKurInput { Kur = 32m, Aktif = true }, pair.Version));
    }

    [Fact]
    public async Task Rate_list_write_on_every_version_read_returns_older_version_and_put_gets_conflict()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var (scope, id) = await RateAsync(host);
        using var _ = scope;
        var inner = scope.ServiceProvider.GetRequiredService<IPinnedRateRepository>();
        var writer = new RateWriter(inner, id, everyRead: true);

        var pair = Assert.Single(await new FixedExchangeRateService(writer, scope.ServiceProvider.GetRequiredService<ICurrentUser>())
            .ListWithVersionsAsync());

        // Elle: 6 okuma = 6 yazım; deneme 3'te satır yazım 5 sonrası (35), dönen sürüm yazım 5 ÖNCESİ.
        Assert.Equal(6, writer.Reads);
        Assert.Equal(6, writer.Writes);
        Assert.Equal(35m, pair.Row.Kur);
        Assert.NotEqual(await inner.GetVersionAsync(id), pair.Version);

        var real = scope.ServiceProvider.GetRequiredService<FixedExchangeRateService>();
        await Assert.ThrowsAsync<ConcurrentModificationException>(() =>
            real.UpdateAsync(id, new SabitKurInput { Kur = 99m, Aktif = true }, pair.Version));
        Assert.Equal(36m, (await real.GetAsync(id))!.Kur);                 // son yazım (6) korunur
    }
}
