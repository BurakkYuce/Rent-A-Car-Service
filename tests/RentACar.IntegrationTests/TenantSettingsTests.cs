using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.TenantSettings;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap D1 — Ayarlar + şifreleme. BAĞIMSIZ ORACLE: yaz/oku roundtrip; ham DB kolonu cipher (düz metin
/// DEĞİL) + ISecretProtector ile çözülür; sır boş→mevcut korunur; tenant izolasyon; key KALICI (ikinci
/// host aynı disk key-ring'den cipher'ı çözer).
/// </summary>
[Collection("postgres")]
public sealed class TenantSettingsTests(PostgresFixture fx)
{
    private const string EFaturaSifre = "efatura-sifre-123";
    private const string SmsKey = "sms-key-xyz";

    [Fact]
    public async Task Roundtrip_and_raw_column_is_cipher_not_plaintext()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<TenantSettingsService>();

        await svc.SaveAsync(new TenantSettingsModel
        {
            FirmaUnvan = "Yüce Rent A.Ş.", FirmaVergiNo = "1234567890",
            EFaturaKullanici = "user1", EFaturaSifre = EFaturaSifre, SmsApiKey = SmsKey
        });

        var m = await svc.GetAsync();
        Assert.Equal("Yüce Rent A.Ş.", m.FirmaUnvan);
        Assert.Equal("user1", m.EFaturaKullanici);
        Assert.Equal(EFaturaSifre, m.EFaturaSifre); // roundtrip düz metin
        Assert.Equal(SmsKey, m.SmsApiKey);

        // Ham DB kolonu ŞİFRELİ: düz metin değil, içinde düz metin geçmiyor; protector ile çözülür.
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var raw = await db.TenantSettings.AsNoTracking().FirstAsync();
        Assert.NotNull(raw.EFaturaSifreEnc);
        Assert.NotEqual(EFaturaSifre, raw.EFaturaSifreEnc);
        Assert.DoesNotContain(EFaturaSifre, raw.EFaturaSifreEnc!);
        Assert.Equal(EFaturaSifre, sp.GetRequiredService<ISecretProtector>().Unprotect(raw.EFaturaSifreEnc));
    }

    [Fact]
    public async Task Blank_secret_keeps_existing()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<TenantSettingsService>();

        await svc.SaveAsync(new TenantSettingsModel { FirmaUnvan = "İlk", EFaturaSifre = EFaturaSifre });
        // İkinci kayıt: sır BOŞ, firma değişir → sır KORUNUR.
        await svc.SaveAsync(new TenantSettingsModel { FirmaUnvan = "Güncel", EFaturaSifre = null });

        var m = await svc.GetAsync();
        Assert.Equal("Güncel", m.FirmaUnvan);
        Assert.Equal(EFaturaSifre, m.EFaturaSifre); // sır korundu
    }

    [Fact]
    public async Task Tenant_isolation()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<TenantSettingsService>()
                .SaveAsync(new TenantSettingsModel { FirmaUnvan = "T1 Firma", EFaturaSifre = EFaturaSifre });

        using var s2 = host.ScopeFor(t2);
        var m2 = await s2.ServiceProvider.GetRequiredService<TenantSettingsService>().GetAsync();
        Assert.Null(m2.FirmaUnvan); // t2 t1'in ayarını görmez
        Assert.Null(m2.EFaturaSifre);
    }

    [Fact]
    public async Task Key_persists_second_host_can_decrypt()
    {
        var tenant = Guid.NewGuid();
        string cipher;

        // 1. host: şifrele + ham cipher'ı al.
        using (var host1 = new TestHost(fx.AppConnectionString))
        using (var scope1 = host1.ScopeFor(tenant))
        {
            var sp1 = scope1.ServiceProvider;
            await sp1.GetRequiredService<TenantSettingsService>()
                .SaveAsync(new TenantSettingsModel { EFaturaSifre = EFaturaSifre });
            await using var db = await sp1.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
            cipher = (await db.TenantSettings.AsNoTracking().FirstAsync()).EFaturaSifreEnc!;
        }

        // 2. host (yeni DI/provider, AYNI disk key-ring): cipher'ı çözebilmeli → anahtar KALICI.
        using var host2 = new TestHost(fx.AppConnectionString);
        using var scope2 = host2.ScopeFor(tenant);
        var prot2 = scope2.ServiceProvider.GetRequiredService<ISecretProtector>();
        Assert.Equal(EFaturaSifre, prot2.Unprotect(cipher));
    }
}
