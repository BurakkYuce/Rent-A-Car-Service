using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RentACar.Application.Finance;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Belge numarası üretiminin GERÇEK PostgreSQL'e karşı davranışı — para yolu olduğu için
/// adversarial odaklı (CLAUDE.md §3).
///
/// <para>Çürütme hedefleri: gün iki kez hesaplanıyor mu, eşzamanlı tahsis çift numara üretiyor mu,
/// rollback boşluk bırakıyor mu, tenant izolasyonu numara benzersizliğini bozuyor mu, fatura
/// sayacı yıl/seri başına mı.</para>
/// </summary>
[Collection("postgres")]
public sealed class BelgeNoUretimTests(PostgresFixture fx)
{
    private static async Task<AppDbContext> CtxAsync(PostgresFixture fx, Guid tenant)
    {
        var host = new TestHost(fx.AppConnectionString);
        var scope = host.ScopeFor(tenant);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        return await factory.CreateDbContextAsync();
    }

    [Fact]
    public async Task Ayni_gun_ayni_tip_sirali_ve_bosluksuz()
    {
        var tenant = Guid.NewGuid();
        await using var db = await CtxAsync(fx, tenant);

        var uretilen = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            uretilen.Add(await BelgeNoUretici.UretAsync(db, tenant, BelgeNoTuru.KiraSozlesmesi, default));
            await tx.CommitAsync();
        }

        for (var i = 0; i < 5; i++)
            BelgeNoOracle.BeklenenlerdenBiri(1, i + 1, uretilen[i]);
        Assert.Equal(5, uretilen.Distinct().Count());
    }

    [Fact]
    public async Task Farkli_tipler_ayni_gun_BIRBIRINI_etkilemez()
    {
        var tenant = Guid.NewGuid();
        await using var db = await CtxAsync(fx, tenant);

        await using var tx = await db.Database.BeginTransactionAsync();
        var kira = await BelgeNoUretici.UretAsync(db, tenant, BelgeNoTuru.KiraSozlesmesi, default);
        var rez = await BelgeNoUretici.UretAsync(db, tenant, BelgeNoTuru.Rezervasyon, default);
        var tahsilat = await BelgeNoUretici.UretAsync(db, tenant, BelgeNoTuru.Tahsilat, default);
        await tx.CommitAsync();

        // Her tip KENDİ sayacına sahip → hepsi o günün 001'i olmalı.
        BelgeNoOracle.BeklenenlerdenBiri(1, 1, kira);
        BelgeNoOracle.BeklenenlerdenBiri(2, 1, rez);
        BelgeNoOracle.BeklenenlerdenBiri(5, 1, tahsilat);
    }

    [Fact]
    public async Task Tahsilat_ve_Tediye_AYRI_sayac()
    {
        // Eskiden ikisi tek "CashNo" sayacını paylaşıyordu. Ayrı tip kodu = ayrı sayaç.
        var tenant = Guid.NewGuid();
        await using var db = await CtxAsync(fx, tenant);
        await using var tx = await db.Database.BeginTransactionAsync();
        var th = await BelgeNoUretici.UretAsync(db, tenant, BelgeNoTuru.Tahsilat, default);
        var td = await BelgeNoUretici.UretAsync(db, tenant, BelgeNoTuru.Tediye, default);
        await tx.CommitAsync();

        BelgeNoOracle.BeklenenlerdenBiri(5, 1, th);
        BelgeNoOracle.BeklenenlerdenBiri(6, 1, td);
    }

    [Fact]
    public async Task Rollback_BOSLUK_birakmaz()
    {
        var tenant = Guid.NewGuid();
        await using var db = await CtxAsync(fx, tenant);

        // 1) Tahsis et, GERİ AL.
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await BelgeNoUretici.UretAsync(db, tenant, BelgeNoTuru.Gider, default);
            await tx.RollbackAsync();
        }

        // 2) Yeniden tahsis → hâlâ günün 1'incisi olmalı (boşluk yok).
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var no = await BelgeNoUretici.UretAsync(db, tenant, BelgeNoTuru.Gider, default);
            BelgeNoOracle.BeklenenlerdenBiri(7, 1, no);
            await tx.CommitAsync();
        }
    }

    [Fact]
    public async Task Eszamanli_tahsis_TEKRARSIZ()
    {
        var tenant = Guid.NewGuid();
        const int n = 8;

        var gorevler = Enumerable.Range(0, n).Select(async _ =>
        {
            await using var db = await CtxAsync(fx, tenant);
            await using var tx = await db.Database.BeginTransactionAsync();
            var no = await BelgeNoUretici.UretAsync(db, tenant, BelgeNoTuru.Tahsilat, default);
            await tx.CommitAsync();
            return no;
        });

        var numaralar = await Task.WhenAll(gorevler);
        Assert.Equal(n, numaralar.Distinct().Count());          // çift numara YOK
        var siralar = numaralar.Select(x => int.Parse(x[10..])).OrderBy(x => x).ToList();
        Assert.Equal(Enumerable.Range(1, n), siralar);          // boşluk da YOK
    }

    [Fact]
    public async Task Tenant_izolasyonu_AYNI_numarayi_uretir_ve_bu_DOGRUDUR()
    {
        // Benzersizlik (TenantId, No) üzerinedir — GLOBAL değil. İki firma aynı gün aynı numarayı
        // görür; RLS onları birbirinden ayırır. Açıkça iddia edilmezse biri "global benzersiz" sanar.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await using var dbA = await CtxAsync(fx, a);
        await using (var tx = await dbA.Database.BeginTransactionAsync())
        {
            Assert.Equal(BelgeNoOracle.Bekle(1, 1),
                await BelgeNoUretici.UretAsync(dbA, a, BelgeNoTuru.KiraSozlesmesi, default, simdi: Sabit()));
            await tx.CommitAsync();
        }

        await using var dbB = await CtxAsync(fx, b);
        await using (var tx = await dbB.Database.BeginTransactionAsync())
        {
            Assert.Equal(BelgeNoOracle.Bekle(1, 1),
                await BelgeNoUretici.UretAsync(dbB, b, BelgeNoTuru.KiraSozlesmesi, default, simdi: Sabit()));
            await tx.CommitAsync();
        }

        // Sabit "şimdi" ile çağırdık → gün dönümü yarışı yok, iki numara BİREBİR aynı.
        static DateTimeOffset Sabit() => DateTimeOffset.UtcNow;
    }

    [Fact]
    public async Task Gun_degisince_sayac_SIFIRLANIR()
    {
        var tenant = Guid.NewGuid();
        await using var db = await CtxAsync(fx, tenant);

        var gun1 = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var gun2 = gun1.AddDays(1);

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            Assert.Equal("2026100301001", await BelgeNoUretici.UretAsync(db, tenant, BelgeNoTuru.KiraSozlesmesi, default, simdi: gun1));
            Assert.Equal("2026100301002", await BelgeNoUretici.UretAsync(db, tenant, BelgeNoTuru.KiraSozlesmesi, default, simdi: gun1));
            await tx.CommitAsync();
        }
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            // Ertesi gün 001'e döner — sıfırlama KODU yok, anahtardaki tarih bunu kendiliğinden yapar.
            Assert.Equal("2026110301001", await BelgeNoUretici.UretAsync(db, tenant, BelgeNoTuru.KiraSozlesmesi, default, simdi: gun2));
            await tx.CommitAsync();
        }
    }

    [Fact]
    public async Task Gece_yarisi_gun_TEK_KEZ_hesaplanir()
    {
        // ADVERSARIAL: gün iki kez hesaplansaydı anahtar bir güne, metin başka güne düşebilir ve
        // ertesi gün AYNI numara ikinci kez üretilirdi. Sınıra oturan bir an ile kontrol edilir:
        // 21:00 UTC = ertesi gün 00:00 İstanbul.
        var tenant = Guid.NewGuid();
        await using var db = await CtxAsync(fx, tenant);
        var sinir = new DateTimeOffset(2026, 5, 20, 21, 0, 0, TimeSpan.Zero);   // İstanbul 21 Mayıs 00:00

        await using var tx = await db.Database.BeginTransactionAsync();
        var no = await BelgeNoUretici.UretAsync(db, tenant, BelgeNoTuru.Ceza, default, simdi: sinir);
        await tx.CommitAsync();

        // 21 Mayıs 2026, ceza (08), sıra 1 → 2026 21 05 08 001
        Assert.Equal("2026210508001", no);

        // Aynı sınır anı için ikinci çağrı 002 vermeli (anahtar da metin de AYNI güne bakıyor).
        await using var tx2 = await db.Database.BeginTransactionAsync();
        Assert.Equal("2026210508002", await BelgeNoUretici.UretAsync(db, tenant, BelgeNoTuru.Ceza, default, simdi: sinir));
        await tx2.CommitAsync();
    }

    // ───────────────────────── Fatura (GİB) ─────────────────────────

    [Fact]
    public async Task Fatura_varsayilan_seri_ile_uretilir()
    {
        var tenant = Guid.NewGuid();
        await using var db = await CtxAsync(fx, tenant);
        await using var tx = await db.Database.BeginTransactionAsync();
        var no = await BelgeNoUretici.FaturaAsync(db, tenant, default);
        await tx.CommitAsync();

        // Ayar satırı yok → varsayılan seri. Numara 16 hane, seri + yıl + 9 haneli sıra.
        Assert.Equal(BelgeNoOracle.FaturaBekle(BelgeNo.VarsayilanSeri, 1), no);
        Assert.Equal(16, no.Length);
    }

    [Fact]
    public async Task Fatura_sayaci_SERI_ve_YIL_basina()
    {
        var tenant = Guid.NewGuid();
        await using var db = await CtxAsync(fx, tenant);

        db.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenant, FaturaSeriKodu = "AAA" });
        await db.SaveChangesAsync();

        var y1 = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
        var y2 = new DateTimeOffset(2027, 6, 1, 9, 0, 0, TimeSpan.Zero);

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            Assert.Equal("AAA2026000000001", await BelgeNoUretici.FaturaAsync(db, tenant, default, simdi: y1));
            Assert.Equal("AAA2026000000002", await BelgeNoUretici.FaturaAsync(db, tenant, default, simdi: y1));
            await tx.CommitAsync();
        }
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            // Yıl dönünce sıra 1'e döner — mevzuat şartı.
            Assert.Equal("AAA2027000000001", await BelgeNoUretici.FaturaAsync(db, tenant, default, simdi: y2));
            await tx.CommitAsync();
        }
    }

    [Fact]
    public async Task Bicimsiz_seri_kodu_GURULTULU_reddedilir()
    {
        // Kullanıcı bilerek bir şey yazmış ama biçimsiz → sessizce varsayılana KAÇILMAZ.
        var tenant = Guid.NewGuid();
        await using var db = await CtxAsync(fx, tenant);
        db.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenant, FaturaSeriKodu = "xy" });
        await db.SaveChangesAsync();

        await using var tx = await db.Database.BeginTransactionAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BelgeNoUretici.FaturaAsync(db, tenant, default));
        Assert.Contains("geçersiz", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Yeni_numaralar_eski_format_kayitlarla_CAKISMAZ()
    {
        // Eski numaralar harf+tire taşır; yeni genel desen tamamı rakam. (TenantId, No) unique
        // indeksleri bu yüzden güvende — geriye dönük numaralandırma gerekmedi.
        var tenant = Guid.NewGuid();
        await using var db = await CtxAsync(fx, tenant);
        await using var tx = await db.Database.BeginTransactionAsync();
        var no = await BelgeNoUretici.UretAsync(db, tenant, BelgeNoTuru.AracSatis, default);
        await tx.CommitAsync();

        Assert.DoesNotContain('-', no);
        Assert.All(no, c => Assert.InRange(c, '0', '9'));
    }
}
