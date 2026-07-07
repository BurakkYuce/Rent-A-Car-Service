using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// KVKK/F2 PII blind-index — BAĞIMSIZ ORACLE: beklenen hash, testin KENDİ HMAC-SHA256 hesabıyla
/// (bilinen dev anahtarı + tenant tuzu + resmî checksum'lı TC'ler) türetilir; üretim kodundan alınmaz.
/// Kapsam: at-rest şifreli/düz-metin-yok, çözülmüş okuma, hash benzersizliği (servis + DB),
/// tenant izolasyonu + tuz (cross-tenant hash FARKLI), tam-eşleşme arama, geriye-dönük backfill
/// + tarihsel audit scrub, audit maskesi, update yolu, Development-dışı anahtar guard'ı.
/// </summary>
[Collection("postgres")]
public sealed class PiiBlindIndexTests(PostgresFixture fx)
{
    // Checksum-geçerli test TC'leri (elle doğrulandı: d10/d11 resmî algoritma).
    private const string Tc1 = "10000000146";
    private const string Tc2 = "10000000214";

    /// <summary>Bağımsız oracle: HmacPiiHasher ile AYNI dev anahtarı + tenant tuzu formatı.</summary>
    private static string ExpectedHash(Guid tenant, string value)
        => Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("dev-only-pii-hmac-key-uretimde-kullanma"),
            Encoding.UTF8.GetBytes($"{tenant:N}:{value}")));

    private static CustomerInput Bireysel(string tc, string ad = "Ali") => new()
    {
        Tip = CariType.Bireysel, Ad = ad, Soyad = "Test", TcKimlik = tc,
        EhliyetNo = "EHL-123", PasaportNo = "P-456"
    };

    [Fact]
    public async Task Db_de_duz_metin_yok_cipher_ve_hash_dogru()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var svc = scope.ServiceProvider.GetRequiredService<CustomerService>();
        var id = await svc.CreateAsync(Bireysel(Tc1));

        // HAM satır (repo şifre çözmesi devre dışı — doğrudan context)
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var raw = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == id);

        Assert.Null(raw.TcKimlik);                       // düz metin YOK
        Assert.Null(raw.EhliyetNo);
        Assert.Null(raw.PasaportNo);
        Assert.NotNull(raw.TcKimlikEnc);
        Assert.DoesNotContain(Tc1, raw.TcKimlikEnc);     // cipher düz metni içermez
        Assert.NotNull(raw.EhliyetNoEnc);
        Assert.NotNull(raw.PasaportNoEnc);
        Assert.Equal(ExpectedHash(tenant, Tc1), raw.TcKimlikHash); // bağımsız HMAC oracle (tenant tuzlu)
    }

    [Fact]
    public async Task Okuma_yollari_cozulmus_pii_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CustomerService>();
        var id = await svc.CreateAsync(Bireysel(Tc1));

        var c = await svc.GetAsync(id);
        Assert.Equal(Tc1, c!.TcKimlik);                  // görüntüleme çözülmüş
        Assert.Equal("EHL-123", c.EhliyetNo);
        Assert.Equal("P-456", c.PasaportNo);

        var rows = await svc.SearchRowsAsync(new CustomerFilter());
        Assert.Equal(Tc1, rows.Items.Single().TcKimlik); // liste satırı da çözülmüş
    }

    [Fact]
    public async Task Ayni_tc_ikinci_kayit_reddedilir_servis_ve_db()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<CustomerService>();
        await svc.CreateAsync(Bireysel(Tc1));

        // 1) Servis ön-kontrolü (hash exists)
        await Assert.ThrowsAsync<DuplicateCariException>(() => svc.CreateAsync(Bireysel(Tc1, ad: "Veli")));

        // 2) DB savunması: ön-kontrolü ATLAYIP aynı hash'le doğrudan repo'ya yaz → kısmi unique index
        var repo = sp.GetRequiredService<ICustomerRepository>();
        var hash = sp.GetRequiredService<IPiiHasher>().Hash(tenant, Tc1);
        var raw = new Customer
        {
            Tip = CariType.Bireysel, Ad = "Yarış", TcKimlikHash = hash, TcKimlikEnc = "x"
        };
        await Assert.ThrowsAsync<DuplicateCariException>(() => repo.CreateAsync(raw));
    }

    [Fact]
    public async Task Farkli_tenant_ayni_tc_serbest_ve_hashler_farkli()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        using var s1 = host.ScopeFor(t1);
        using var s2 = host.ScopeFor(t2);
        var id1 = await s1.ServiceProvider.GetRequiredService<CustomerService>().CreateAsync(Bireysel(Tc1));
        // index (TenantId, TcKimlikHash) — başka tenant'ta aynı TC engellenmez
        var id2 = await s2.ServiceProvider.GetRequiredService<CustomerService>().CreateAsync(Bireysel(Tc1));

        // Tenant TUZU: aynı TC iki tenant'ta FARKLI hash üretir → dump'ta cross-tenant korelasyon yok.
        var f1 = s1.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var f2 = s2.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db1 = await f1.CreateDbContextAsync();
        await using var db2 = await f2.CreateDbContextAsync();
        var h1 = (await db1.Customers.AsNoTracking().SingleAsync(c => c.Id == id1)).TcKimlikHash;
        var h2 = (await db2.Customers.AsNoTracking().SingleAsync(c => c.Id == id2)).TcKimlikHash;
        Assert.NotNull(h1);
        Assert.NotEqual(h1, h2);
        Assert.Equal(ExpectedHash(t1, Tc1), h1);
        Assert.Equal(ExpectedHash(t2, Tc1), h2);
    }

    [Fact]
    public async Task Tam_tc_aramasi_bulur_kismi_tc_bulmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CustomerService>();
        await svc.CreateAsync(Bireysel(Tc1));

        var tam = await svc.SearchAsync(new CustomerFilter { Query = Tc1 });
        Assert.Equal(1, tam.Total);                       // 11 hane → blind-index eşleşmesi

        var kismi = await svc.SearchAsync(new CustomerFilter { Query = Tc1[..7] });
        Assert.Equal(0, kismi.Total);                     // kısmi TC bilinçli olarak aranamaz (şifreli)

        var adIle = await svc.SearchAsync(new CustomerFilter { Query = "Ali" });
        Assert.Equal(1, adIle.Total);                     // ad araması etkilenmedi
    }

    [Fact]
    public async Task Audit_izine_duz_pii_yazilmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CustomerService>();
        var id = await svc.CreateAsync(Bireysel(Tc1));
        await svc.UpdateAsync(id, Bireysel(Tc2)); // update yolu da audit üretir

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var izler = (await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == "Customers" && a.EntityId == id.ToString())
            .Select(a => new { a.OldValues, a.NewValues })
            .ToListAsync())
            .Select(a => (a.OldValues ?? "") + (a.NewValues ?? "")) // jsonb'ye SQL'de '' eklenemez → bellek-içi
            .ToList();

        Assert.NotEmpty(izler);
        Assert.All(izler, json =>
        {
            Assert.DoesNotContain(Tc1, json);      // düz TC yok
            Assert.DoesNotContain(Tc2, json);
            Assert.DoesNotContain("EHL-123", json); // ehliyet/pasaport da yok
            Assert.DoesNotContain("P-456", json);
        });
        Assert.Contains(izler, json => json.Contains("***")); // değişiklik izi maskeli ama mevcut
    }

    [Fact]
    public async Task Backfill_duz_metni_sifreler_audit_izini_temizler_benzersizlik_calisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;

        // ESKİ (F2 öncesi) veri simülasyonu: düz metin dolu, cipher boş + maskesiz TARİHSEL audit satırı.
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var legacyId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Customers.Add(new Customer
            {
                Id = legacyId, Tip = CariType.Bireysel, Ad = "Eski", Soyad = "Kayıt",
                TcKimlik = Tc2, EhliyetNo = "EHL-OLD", PasaportNo = "P-OLD"
            });
            db.AuditLogs.Add(new AuditLog // maske ÖNCESİ yazılmış tarihsel iz simülasyonu
            {
                EntityName = "Customers", EntityId = legacyId.ToString(), Action = AuditAction.Create,
                NewValues = $$$"""{"Ad": "Eski", "TcKimlik": "{{{Tc2}}}", "EhliyetNo": "EHL-OLD", "PasaportNo": "P-OLD"}"""
            });
            db.AuditLogs.Add(new AuditLog // ZEHİRLİ değer: kaçışlı tırnak (adversarial Medium — regex'i kırıyordu)
            {
                EntityName = "Customers", EntityId = legacyId.ToString(), Action = AuditAction.Update,
                NewValues = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["Ad"] = "Zehir", ["PasaportNo"] = "ab\"cd"
                })
            });
            await db.SaveChangesAsync();
        }

        // Backfill tenant LİSTESİ üzerinden döner (BYPASSRLS varsayımı yok) → tenant kaydı şart
        // (üretimde her gerçek tenant Tenants'ta zaten var; testler normalde satırsız GUID kullanır).
        var ownerOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fx.OwnerConnectionString).Options;
        await using (var seedDb = new AppDbContext(ownerOptions, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            seedDb.Tenants.Add(new Tenant { Id = tenant, Code = $"pii-{tenant:N}"[..12], Name = "PII Backfill Test" });
            await seedDb.SaveChangesAsync();
        }

        await using (var ownerDb = new AppDbContext(ownerOptions, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            var n = await PiiBackfill.RunAsync(ownerDb,
                sp.GetRequiredService<ISecretProtector>(), sp.GetRequiredService<IPiiHasher>());
            Assert.True(n >= 1); // en az bizim eski kayıt şifrelendi
        }

        // Düz metin gitti, cipher/hash geldi (bağımsız oracle), okuma çözülmüş döner
        await using (var db = await factory.CreateDbContextAsync())
        {
            var raw = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == legacyId);
            Assert.Null(raw.TcKimlik);
            Assert.Null(raw.EhliyetNo);
            Assert.Null(raw.PasaportNo);
            Assert.Equal(ExpectedHash(tenant, Tc2), raw.TcKimlikHash);

            // Tarihsel audit izi SCRUB edildi: düz PII değerleri maskelendi, diğer alanlar dokunulmadı.
            // (İki Create izi var: interceptor'ın maskeli izi + elle eklenen tarihsel iz — İKİSİ de temiz olmalı.)
            var auditler = await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityName == "Customers" && a.EntityId == legacyId.ToString())
                .ToListAsync();
            Assert.NotEmpty(auditler);
            Assert.All(auditler, a =>
            {
                var json = (a.OldValues ?? "") + (a.NewValues ?? "");
                Assert.DoesNotContain(Tc2, json);
                Assert.DoesNotContain("EHL-OLD", json);
                Assert.DoesNotContain("P-OLD", json);
                Assert.DoesNotContain("ab\\\"cd", json); // zehirli değer de maskelendi
                if (a.NewValues is not null)
                    System.Text.Json.JsonDocument.Parse(a.NewValues); // scrub geçerli JSON bıraktı
            });
            Assert.Contains(auditler, a => (a.NewValues ?? "").Contains("***")
                                        && (a.NewValues ?? "").Contains("Eski")); // maske var, PII-dışı alan duruyor
            Assert.Contains(auditler, a => (a.NewValues ?? "").Contains("Zehir")); // zehirli satır sağ ve temiz
        }
        var svc = sp.GetRequiredService<CustomerService>();
        var okunan = await svc.GetAsync(legacyId);
        Assert.Equal(Tc2, okunan!.TcKimlik);
        Assert.Equal("EHL-OLD", okunan.EhliyetNo);

        // Backfill İDEMPOTENT: ikinci koşu bu kaydı yeniden İŞLEMEZ
        await using (var ownerDb = new AppDbContext(ownerOptions, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            var again = await PiiBackfill.RunAsync(ownerDb,
                sp.GetRequiredService<ISecretProtector>(), sp.GetRequiredService<IPiiHasher>());
            Assert.Equal(0, again);
        }

        // Backfill'lenmiş kayda karşı benzersizlik: aynı TC ile yeni kayıt reddedilir
        await Assert.ThrowsAsync<DuplicateCariException>(() => svc.CreateAsync(Bireysel(Tc2, ad: "Yeni")));
    }

    [Fact]
    public async Task Update_tc_degisince_hash_ve_cipher_guncellenir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var svc = scope.ServiceProvider.GetRequiredService<CustomerService>();
        var id = await svc.CreateAsync(Bireysel(Tc1));

        Assert.True(await svc.UpdateAsync(id, Bireysel(Tc2)));

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var raw = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.Equal(ExpectedHash(tenant, Tc2), raw.TcKimlikHash); // yeni TC'nin özeti
        Assert.Equal(Tc2, (await svc.GetAsync(id))!.TcKimlik);

        // eski TC artık aranamaz, yenisi bulunur
        Assert.Equal(0, (await svc.SearchAsync(new CustomerFilter { Query = Tc1 })).Total);
        Assert.Equal(1, (await svc.SearchAsync(new CustomerFilter { Query = Tc2 })).Total);
    }

    [Fact]
    public void Development_disi_ortam_HmacKey_siz_ACILMAZ()
    {
        // Adversarial M1: Staging'de bilinen dev anahtarına sessiz düşüş olmamalı — açılış reddedilir.
        using var api = new ApiFactory(fx.AppConnectionString, new Dictionary<string, string?>
        {
            ["environment"] = "Staging",
        });
        var ex = Record.Exception(() => api.CreateClient());
        Assert.NotNull(ex);
        Assert.Contains("Pii:HmacKey", ex!.ToString());
    }

    [Fact]
    public void Development_disi_ortam_JwtKey_siz_ACILMAZ()
    {
        // Adversarial C1: API JWT imza anahtarı üretimde ZORUNLU — committed sabit/boş anahtarla açılmaz
        // (aksi halde herkes Admin token forge eder → tam bypass). Pii+DP guard'larını geçir, yalnız Jwt:Key boş.
        var oldDp = Environment.GetEnvironmentVariable("RACAR_DP_KEYS");
        try
        {
            Environment.SetEnvironmentVariable("RACAR_DP_KEYS", Path.Combine(Path.GetTempPath(), "racar-dp-jwtguard"));
            using var api = new ApiFactory(fx.AppConnectionString, new Dictionary<string, string?>
            {
                ["environment"] = "Staging",
                ["Pii:HmacKey"] = "staging-pii-hmac-key-min-32-bytes-length-ok!!",
                ["Jwt:Key"] = "", // JWT anahtarı YOK → JWT guard reddetmeli
            });
            var ex = Record.Exception(() => api.CreateClient());
            Assert.NotNull(ex);
            Assert.Contains("Jwt:Key", ex!.ToString());
        }
        finally { Environment.SetEnvironmentVariable("RACAR_DP_KEYS", oldDp); }
    }

    /// <summary>
    /// Boot-idempotenslik (review bulgusu): tarihsel audit'te maskesiz PII varsa cari düz-metni
    /// OLMASA BİLE maskelenir (gate 'legacy cari var mı'ya değil 'maskesiz audit var mı'ya bağlı —
    /// geçiş penceresi kaçmaz); ve göç tamamlandıktan sonraki koşu audit satırını FİZİKSEL yeniden
    /// yazmaz → immutability trigger'ı her boot'ta gereksiz açılıp kapanmaz. Kanıt: xmin (satır
    /// sürümü) 2. koşuda DEĞİŞMEZ.
    /// </summary>
    [Fact]
    public async Task Backfill_audit_only_pii_maskeler_ve_temiz_kosu_satiri_yeniden_yazmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();

        // Tenant kaydı (backfill tenant-loop'u Tenants'tan besleniyor).
        var ownerOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fx.OwnerConnectionString).Options;
        await using (var seedDb = new AppDbContext(ownerOptions, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            seedDb.Tenants.Add(new Tenant { Id = tenant, Code = $"pib-{tenant:N}"[..12], Name = "PII Boot Test" });
            await seedDb.SaveChangesAsync();
        }

        // SADECE tarihsel maskesiz audit izi — hiç legacy CARİ yok (geçiş penceresi senaryosu).
        var auditId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AuditLogs.Add(new AuditLog
            {
                Id = auditId, EntityName = "Customers", EntityId = Guid.NewGuid().ToString(),
                Action = AuditAction.Create,
                NewValues = $$$"""{"Ad": "Onur", "TcKimlik": "{{{Tc1}}}"}"""
            });
            await db.SaveChangesAsync();
        }

        // 1. koşu: cari şifrelemesi 0 ama audit izi maskelenmeli.
        await using (var ownerDb = new AppDbContext(ownerOptions, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            var n = await PiiBackfill.RunAsync(ownerDb,
                sp.GetRequiredService<ISecretProtector>(), sp.GetRequiredService<IPiiHasher>());
            Assert.Equal(0, n); // hiç cari yok → 0; ama audit scrub çalışmış olmalı
        }

        string xminSonrasi1;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var audit = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Id == auditId);
            Assert.DoesNotContain(Tc1, audit.NewValues!);   // düz TC maskelendi
            Assert.Contains("***", audit.NewValues!);
            Assert.Contains("Onur", audit.NewValues!);      // PII-dışı alan duruyor
            // Satır fiziksel sürümü (xmin) — 1. koşu maskelediği için bir kez değişti.
            xminSonrasi1 = await db.Database
                .SqlQuery<string>($"""SELECT xmin::text AS "Value" FROM "AuditLogs" WHERE "Id" = {auditId}""")
                .SingleAsync();
        }

        // 2. koşu (steady-state): maskesiz PII yok → scrub HİÇ çalışmamalı, satır yeniden yazılmamalı.
        await using (var ownerDb = new AppDbContext(ownerOptions, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            var again = await PiiBackfill.RunAsync(ownerDb,
                sp.GetRequiredService<ISecretProtector>(), sp.GetRequiredService<IPiiHasher>());
            Assert.Equal(0, again);
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var xminSonrasi2 = await db.Database
                .SqlQuery<string>($"""SELECT xmin::text AS "Value" FROM "AuditLogs" WHERE "Id" = {auditId}""")
                .SingleAsync();
            Assert.Equal(xminSonrasi1, xminSonrasi2); // satır DOKUNULMADI → boş boot yazma yapmıyor
        }
    }
}
