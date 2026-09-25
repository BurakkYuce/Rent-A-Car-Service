using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Components.Pages.Audit;

namespace RentACar.IntegrationTests;

/// <summary>
/// #304 L2 (2026-09-25): denetim interceptor'ı *Enc / *Hash / *Token kolonlarının DEĞERİNİ AuditLogs'a yazmaz.
/// Önceden yalnız sabit bir PII listesi maskeleniyordu; <c>SmtpSifreEnc</c> gibi sır cipher'ları düz yazılıyordu.
/// Beklenen değerler elle: testin kendi yazdığı nöbetçi metinler AuditLogs'ta HİÇ geçmemeli, anahtar "***" taşımalı.
/// </summary>
[Collection("postgres")]
public sealed class AuditSecretColumnTests(PostgresFixture fx)
{
    private static async Task<List<AuditLog>> LogsAsync(IServiceScope scope, Guid entityId)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var key = entityId.ToString();
        return await db.AuditLogs.Where(a => a.EntityId == key).OrderBy(a => a.TimestampUtc).ToListAsync();
    }

    /// <summary>jsonb biçimi yeniden düzenlediği için metin değil JSON olarak okunur; değer yoksa/null ise <c>null</c>.</summary>
    private static string? Value(string? json, string key)
    {
        Assert.NotNull(json);
        var p = System.Text.Json.JsonDocument.Parse(json!).RootElement.GetProperty(key);
        return p.ValueKind == System.Text.Json.JsonValueKind.Null ? null : p.ToString();
    }

    [Fact]
    public async Task Secret_cipher_columns_never_reach_audit_values_on_create_update_delete()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        var id = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Add(new TenantSettings
            {
                Id = id, FirmaUnvan = "Deneme Firma A", SmtpSifreEnc = "SENTINEL-SMTP-1", SmsApiKeyEnc = "SENTINEL-SMS-1",
                PosApiKeyEnc = "SENTINEL-POS-1", EFaturaSifreEnc = "SENTINEL-EF-1",
            });
            await db.SaveChangesAsync();
        }
        await using (var db = await factory.CreateDbContextAsync())
        {
            var s = await db.Set<TenantSettings>().SingleAsync(x => x.Id == id);
            s.SmtpSifreEnc = "SENTINEL-SMTP-2";
            s.FirmaUnvan = "Deneme Firma B";
            await db.SaveChangesAsync();
        }
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Remove(await db.Set<TenantSettings>().SingleAsync(x => x.Id == id));
            await db.SaveChangesAsync();
        }

        var logs = await LogsAsync(scope, id);
        Assert.Equal([AuditAction.Create, AuditAction.Update, AuditAction.Delete], logs.Select(l => l.Action));
        foreach (var l in logs)
        {
            Assert.DoesNotContain("SENTINEL", l.OldValues ?? "");
            Assert.DoesNotContain("SENTINEL", l.NewValues ?? "");
        }
        Assert.Equal("***", Value(logs[0].NewValues, "SmtpSifreEnc"));
        Assert.Equal("***", Value(logs[0].NewValues, "SmsApiKeyEnc"));
        // Değişen sır alanı güncellemede görünür ama değersiz; sır olmayan alan aynen yazılır.
        Assert.Equal("***", Value(logs[1].OldValues, "SmtpSifreEnc"));
        Assert.Equal("***", Value(logs[1].NewValues, "SmtpSifreEnc"));
        Assert.Equal("Deneme Firma A", Value(logs[1].OldValues, "FirmaUnvan"));
        Assert.Equal("Deneme Firma B", Value(logs[1].NewValues, "FirmaUnvan"));
        Assert.Equal("***", Value(logs[2].OldValues, "PosApiKeyEnc"));
    }

    [Fact]
    public async Task Hash_and_token_columns_are_masked_but_cleared_value_stays_visible()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        var id = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Add(new TarifeGrubu { Id = id, Kod = "BRK1", Ad = "Broker", SifreHash = "SENTINEL-HASH-1" });
            await db.SaveChangesAsync();
        }
        await using (var db = await factory.CreateDbContextAsync())
        {
            (await db.Set<TarifeGrubu>().SingleAsync(x => x.Id == id)).SifreHash = null;
            await db.SaveChangesAsync();
        }

        var logs = await LogsAsync(scope, id);
        Assert.Equal(2, logs.Count);
        Assert.DoesNotContain("SENTINEL", logs[0].NewValues);
        Assert.Equal("***", Value(logs[0].NewValues, "SifreHash"));
        Assert.Equal("***", Value(logs[1].OldValues, "SifreHash"));
        // Temizleme bilgisi (null) sır taşımaz ve iz için değerlidir.
        Assert.Null(Value(logs[1].NewValues, "SifreHash"));
    }

    [Fact]
    public void Blazor_audit_screen_masks_legacy_rows_with_the_same_rule()
    {
        // Kural öncesinden kalmış kayıt: cipher/hash/token düz duruyor.
        const string legacy = "{\"SmtpSifreEnc\":\"CfDJ8-legacy\",\"PasswordHash\":\"AQAAAA-legacy\",\"CalendarToken\":\"tok-legacy\",\"FirmaUnvan\":\"X\"}";
        var shown = AuditList.Masked(legacy);
        Assert.DoesNotContain("legacy", shown);
        Assert.Contains("\"FirmaUnvan\":\"X\"", shown);
        Assert.Equal("(gösterilemiyor)", AuditList.Masked("düz metin CfDJ8-legacy"));
    }
}
