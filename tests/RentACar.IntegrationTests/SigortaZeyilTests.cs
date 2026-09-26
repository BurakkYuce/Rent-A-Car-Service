using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Regulation;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-15 — sigorta poliçesi ZEYLİ (poliçe eki) + sigorta değer tabanı.
///
/// <para>BAĞIMSIZ ORACLE: beklenen değerler elle kurulan senaryodan gelir (3 zeyil eklenir → 3;
/// 1 silinir → 2; prim 1200 ödenince gider 1200; tahsilat 2000 → cari bakiye −2000). Hiçbir
/// beklenen değer servis/rapor kodundan türetilmez.</para>
///
/// <para><b>KİLİTLİ KARAR (docs/KARARLAR.md): zeyil DEFTERE YAZMAZ.</b>
/// <c>Zeyil_deftere_yazmaz_*</c> testleri kasıtlı olarak KIRILGANDIR: zeyile herhangi bir
/// <c>AccountLedgerEntry</c> eklenirse defter satır sayısı ve/veya cari bakiye değişir ve test
/// patlar. Bu, çift-sayıma karşı kalıcı kilittir.</para>
/// </summary>
[Collection("postgres")]
public sealed class SigortaZeyilTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Bit = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ZDate = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ZIssue = new(2026, 2, 25, 0, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> PoliceAsync(IServiceProvider sp, string plate, decimal premium = 1200m,
        string currency = "TRY")
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plate, Durum = VehicleStatus.Musait });
        return await sp.GetRequiredService<RegulationService>().AddInsuranceAsync(
            v, InsuranceType.Kasko, Start, Bit, premium, "POL-" + plate, "AnadoluSigorta", null, currency);
    }

    private static ZeyilInput Input(Guid policyId, string no, decimal gross) => new()
    {
        PolicyId = policyId, ZeyilNo = no, Tarih = ZDate, Tanzim = ZIssue,
        Deger = 250_000m, Brut = gross, Net = 1000m, FonVergi = 200.50m,
        Tipi = "Zam", Neden = "Teminat artışı"
    };

    private static async Task<int> LedgerLineCountAsync(IServiceProvider sp)
    {
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        return await db.AccountLedgerEntries.CountAsync();
    }

    // ---- CRUD (bağımsız oracle) ----

    [Fact]
    public async Task Zeyil_ekle_listele_sil()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var reg = sp.GetRequiredService<RegulationService>();
        var pol = await PoliceAsync(sp, "34 ZY 01");

        await reg.AddEndorsementAsync(Input(pol, "Z-1", 500m));
        var second = await reg.AddEndorsementAsync(Input(pol, "Z-2", 1200.50m));
        await reg.AddEndorsementAsync(Input(pol, "Z-3", -300m)); // tenzil (iade) zeyli — negatif brüt

        var list = await reg.ListEndorsementsAsync(pol);
        Assert.Equal(3, list.Count);   // ELLE: 3 zeyil eklendi

        // Alan turu (round-trip): girilen değer aynen okunur.
        var z2 = list.Single(z => z.ZeyilNo == "Z-2");
        Assert.Equal(ZDate, z2.Tarih);
        Assert.Equal(ZIssue, z2.Tanzim);
        Assert.Equal(250_000m, z2.Deger);
        Assert.Equal(1200.50m, z2.Brut);
        Assert.Equal(1000m, z2.Net);
        Assert.Equal(200.50m, z2.FonVergi);
        Assert.Equal("Zam", z2.Tipi);
        Assert.Equal("Teminat artışı", z2.Neden);
        Assert.Equal(pol, z2.PolicyId);

        await reg.DeleteEndorsementAsync(second);
        var remainingList = await reg.ListEndorsementsAsync(pol);
        Assert.Equal(2, remainingList.Count);   // ELLE: 3 − 1 = 2
        Assert.Equal(new[] { "Z-1", "Z-3" },
            remainingList.Select(z => z.ZeyilNo).OrderBy(x => x, StringComparer.Ordinal).ToArray());

        // Olmayan kaydın silinmesi temiz red (500 değil).
        await Assert.ThrowsAsync<ValidationException>(() => reg.DeleteEndorsementAsync(second));
    }

    [Fact]
    public async Task Ayni_police_ayni_zeyil_no_ikinci_kez_eklenemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var reg = sp.GetRequiredService<RegulationService>();
        var pol = await PoliceAsync(sp, "34 ZY 02");

        await reg.AddEndorsementAsync(Input(pol, "Z-1", 500m));
        await Assert.ThrowsAsync<ValidationException>(() => reg.AddEndorsementAsync(Input(pol, "Z-1", 700m)));
        Assert.Single(await reg.ListEndorsementsAsync(pol));   // ikinci giriş yazılmadı

        // Aynı zeyil no BAŞKA poliçede serbest (benzersizlik poliçe içindedir).
        var pol2 = await PoliceAsync(sp, "34 ZY 03");
        await reg.AddEndorsementAsync(Input(pol2, "Z-1", 700m));
        Assert.Single(await reg.ListEndorsementsAsync(pol2));
    }

    [Fact]
    public async Task Zorunlu_alanlar_ve_deger_isareti_dogrulanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var reg = sp.GetRequiredService<RegulationService>();
        var pol = await PoliceAsync(sp, "34 ZY 04");

        var missingNo = Input(pol, "Z-1", 100m); missingNo.ZeyilNo = "   ";
        await Assert.ThrowsAsync<ValidationException>(() => reg.AddEndorsementAsync(missingNo));

        var noDate = Input(pol, "Z-1", 100m); noDate.Tarih = null;
        await Assert.ThrowsAsync<ValidationException>(() => reg.AddEndorsementAsync(noDate));

        // Değer TEMİNAT tabanı → negatif olamaz.
        var negativeValue = Input(pol, "Z-1", 100m); negativeValue.Deger = -1m;
        await Assert.ThrowsAsync<ValidationException>(() => reg.AddEndorsementAsync(negativeValue));

        // Kolon sınırlarını aşan metin SUNUCUDA reddedilir (form maxlength'i atlayan POST → 500 değil).
        var longNo = Input(pol, new string('N', 33), 100m);
        await Assert.ThrowsAsync<ValidationException>(() => reg.AddEndorsementAsync(longNo));
        var longType = Input(pol, "Z-1", 100m); longType.Tipi = new string('T', 65);
        await Assert.ThrowsAsync<ValidationException>(() => reg.AddEndorsementAsync(longType));
        var longReason = Input(pol, "Z-1", 100m); longReason.Neden = new string('S', 513);
        await Assert.ThrowsAsync<ValidationException>(() => reg.AddEndorsementAsync(longReason));

        // Brüt/Net/Fon-Vergi negatif OLABİLİR (tenzil/iade zeyli) — bilgi alanı, işaret serbest.
        var deduction = Input(pol, "TZ-1", -750m); deduction.Net = -600m; deduction.FonVergi = -150m;
        await reg.AddEndorsementAsync(deduction);
        var record = (await reg.ListEndorsementsAsync(pol)).Single();
        Assert.Equal(-750m, record.Brut);
        Assert.Equal(-600m, record.Net);
        Assert.Equal(-150m, record.FonVergi);

        Assert.Empty(await reg.ListEndorsementsAsync(Guid.NewGuid())); // olmayan poliçe → boş
        var noPolicy = Input(Guid.NewGuid(), "Z-9", 100m);
        await Assert.ThrowsAsync<ValidationException>(() => reg.AddEndorsementAsync(noPolicy));
    }

    // ---- KİLİTLİ KARAR: deftere yazmaz ----

    [Fact]
    public async Task Zeyil_deftere_yazmaz_defter_satiri_ve_cari_bakiye_degismez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var reg = sp.GetRequiredService<RegulationService>();
        var reports = sp.GetRequiredService<ReportService>();

        var account = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Zeyil", Soyad = "Tanik" });
        await sp.GetRequiredService<CashService>().CollectAsync(new CashInput { CariId = account, Tutar = 2000m });

        var pol = await PoliceAsync(sp, "34 ZY 10", premium: 1200m);
        await reg.PayInsuranceAsync(pol, LedgerAccountType.Kasa);

        // ELLE oracle: tahsilat 2 satır (Borç Kasa / Alacak Cari) + sigorta ödeme 2 satır = 4.
        Assert.Equal(4, await LedgerLineCountAsync(sp));
        var previousDay = await reports.GetRevenueExpenseAsync();
        Assert.Equal(1200m, previousDay.GiderToplam);
        Assert.Equal(0m, previousDay.GelirToplam);
        Assert.Equal(-2000m, (await reports.GetAccountBalancesAsync()).Single(b => b.CariId == account).Bakiye);

        // UÇUK değerli 3 zeyil: deftere sızsaydı hiçbir toplam yerinde kalmazdı.
        for (var i = 1; i <= 3; i++)
        {
            var g = Input(pol, $"ZZ-{i}", 999_999m);
            g.Net = 888_888m; g.FonVergi = 111_111m; g.Deger = 5_000_000m;
            await reg.AddEndorsementAsync(g);
        }
        Assert.Equal(3, (await reg.ListEndorsementsAsync(pol)).Count);

        // Defter BİREBİR aynı: satır sayısı, gider/gelir toplamı, cari bakiye.
        Assert.Equal(4, await LedgerLineCountAsync(sp));
        var nextDay = await reports.GetRevenueExpenseAsync();
        Assert.Equal(1200m, nextDay.GiderToplam);
        Assert.Equal(0m, nextDay.GelirToplam);
        Assert.Equal(-2000m, (await reports.GetAccountBalancesAsync()).Single(b => b.CariId == account).Bakiye);

        // Ve hiçbir defter satırı zeyil kaynaklı değil.
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.AccountLedgerEntries.Where(e => e.SourceType!.Contains("Zeyil")).ToListAsync());

        // Zeyil SİLMEK de defteri değiştirmez (ters kayıt üretmez — mali belge değil).
        await reg.DeleteEndorsementAsync((await reg.ListEndorsementsAsync(pol)).First().Id);
        Assert.Equal(4, await LedgerLineCountAsync(sp));
        Assert.Equal(1200m, (await reports.GetRevenueExpenseAsync()).GiderToplam);
    }

    [Fact]
    public async Task Zeyil_police_kalanini_ve_zeyilprimini_degistirmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var reg = sp.GetRequiredService<RegulationService>();
        var pol = await PoliceAsync(sp, "34 ZY 11", premium: 1200m);

        Assert.Equal(1200m, (await reg.ListInsuranceAsync()).Single(p => p.Id == pol).Kalan); // açılışta = Prim

        await reg.AddEndorsementAsync(Input(pol, "Z-1", 5000m));
        var after = (await reg.ListInsuranceAsync()).Single(p => p.Id == pol);
        Assert.Equal(1200m, after.Kalan);    // zeyil bakiyeyi BÜYÜTMEZ (defter dışı borç kaynağı açmaz)
        Assert.Equal(0m, after.ZeyilPrim);   // ödeme alanına da dokunmaz
        Assert.False(after.Odendi);

        await reg.PayInsuranceAsync(pol, LedgerAccountType.Kasa); // prim tamamı → Kalan 0
        var paid = (await reg.ListInsuranceAsync()).Single(p => p.Id == pol);
        Assert.Equal(0m, paid.Kalan);
        Assert.True(paid.Odendi);
        Assert.Equal(1200m, (await sp.GetRequiredService<ReportService>().GetRevenueExpenseAsync()).GiderToplam);
    }

    // ---- Sigorta değer tabanı (bilgi alanları) ----

    [Fact]
    public async Task Police_deger_tabani_kaydedilir_ve_negatif_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var reg = sp.GetRequiredService<RegulationService>();
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 ZY 20" });

        var pol = await reg.AddInsuranceAsync(v, InsuranceType.Kasko, Start, Bit, 1200m, "P-20", "Sig", null, "TRY",
            vehicleValue: 750_000m, immValue: 100_000m, accessoryValue: 25_000m);

        var rec = (await reg.ListInsuranceAsync()).Single(p => p.Id == pol);
        Assert.Equal(750_000m, rec.AracDegeri);
        Assert.Equal(100_000m, rec.ImmDegeri);
        Assert.Equal(25_000m, rec.AksesuarDegeri);

        // Değer tabanı BİLGİdir: defterle bağı yok (poliçe henüz ödenmedi → hiç satır yok).
        Assert.Equal(0, await LedgerLineCountAsync(sp));

        await Assert.ThrowsAsync<ValidationException>(() => reg.AddInsuranceAsync(
            v, InsuranceType.Trafik, Start, Bit, 1m, "P-21", "Sig", null, "TRY", vehicleValue: -1m));
        await Assert.ThrowsAsync<ValidationException>(() => reg.AddInsuranceAsync(
            v, InsuranceType.Trafik, Start, Bit, 1m, "P-22", "Sig", null, "TRY", immValue: -1m));
        await Assert.ThrowsAsync<ValidationException>(() => reg.AddInsuranceAsync(
            v, InsuranceType.Trafik, Start, Bit, 1m, "P-23", "Sig", null, "TRY", accessoryValue: -1m));

        // Alanlar OPSİYONEL: verilmezse null kalır (eski çağrılar daralmaz).
        var sade = await reg.AddInsuranceAsync(v, InsuranceType.Trafik, Start, Bit, 500m, "P-24", "Sig", null);
        var sadeRec = (await reg.ListInsuranceAsync()).Single(p => p.Id == sade);
        Assert.Null(sadeRec.AracDegeri);
        Assert.Null(sadeRec.ImmDegeri);
        Assert.Null(sadeRec.AksesuarDegeri);
        Assert.Equal(500m, sadeRec.Kalan);
    }

    /// <summary>
    /// Regresyon (FAZ-15 spec "Notlar"): dövizli poliçede kur bulunamazsa ödeme REDDEDİLİR —
    /// bizde olan, canlıda olmayan güvenlik davranışı. Zeyil eklemek bunu ne gevşetir ne de
    /// tetikler (zeyil mali yol DEĞİLDİR, KurCozucu'ya hiç uğramaz).
    /// </summary>
    [Fact]
    public async Task Fx_police_kur_dogrulamasi_zeyilden_etkilenmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var reg = sp.GetRequiredService<RegulationService>();
        // GBP: hiçbir testte kur seed'lenmez → çözülemez (OdemeKurOtomatikTests deseni).
        var pol = await PoliceAsync(sp, "34 ZY 30", premium: 100m, currency: "GBP");

        await reg.AddEndorsementAsync(Input(pol, "Z-1", 999m));   // zeyil kura DOKUNMAZ

        await Assert.ThrowsAsync<ValidationException>(() => reg.PayInsuranceAsync(pol, LedgerAccountType.Kasa));
        Assert.Equal(0, await LedgerLineCountAsync(sp));  // sessiz kur=1 ile postlanmadı
        Assert.False((await reg.ListInsuranceAsync()).Single(p => p.Id == pol).Odendi);
    }

    // ---- Yetki ----

    [Fact]
    public async Task Yetkisiz_rol_zeyil_yazamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid pol, endorsement;
        using (var admin = host.ScopeFor(tenant))
        {
            var sp = admin.ServiceProvider;
            pol = await PoliceAsync(sp, "34 ZY 40");
            endorsement = await sp.GetRequiredService<RegulationService>().AddEndorsementAsync(Input(pol, "Z-1", 100m));
        }

        // Muhasebe: FinanceWrite var, OperationsWrite YOK → zeyil yazamaz/silemez (okuma serbest).
        using var accounting = host.ScopeFor(tenant, Guid.NewGuid(), "muhasebeci", UserRole.Muhasebe);
        var reg = accounting.ServiceProvider.GetRequiredService<RegulationService>();
        await Assert.ThrowsAsync<NoPermissionException>(() => reg.AddEndorsementAsync(Input(pol, "Z-2", 100m)));
        await Assert.ThrowsAsync<NoPermissionException>(() => reg.DeleteEndorsementAsync(endorsement));
        Assert.Single(await reg.ListEndorsementsAsync(pol));

        // Operatör: OperationsWrite var → yazabilir.
        using var operatorScope = host.ScopeFor(tenant, Guid.NewGuid(), "operator", UserRole.Operator);
        await operatorScope.ServiceProvider.GetRequiredService<RegulationService>()
            .AddEndorsementAsync(Input(pol, "Z-3", 100m));
    }

    // ---- Tenant izolasyonu: servis + HAM RLS (racar_app) ----

    [Fact]
    public async Task Zeyil_tenant_izole_ham_rls_ile_dogrulanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Guid polA, endorsementA;
        using (var sa = host.ScopeFor(a))
        {
            polA = await PoliceAsync(sa.ServiceProvider, "34 ZY 50");
            endorsementA = await sa.ServiceProvider.GetRequiredService<RegulationService>()
                .AddEndorsementAsync(Input(polA, "Z-1", 500m));
        }

        Guid polB;
        using (var sb = host.ScopeFor(b))
        {
            var regB = sb.ServiceProvider.GetRequiredService<RegulationService>();
            Assert.Empty(await regB.ListAllEndorsementsAsync());        // B, A'nın zeylini görmez
            Assert.Empty(await regB.ListEndorsementsAsync(polA));
            await Assert.ThrowsAsync<ValidationException>(() => regB.DeleteEndorsementAsync(endorsementA)); // silemez
            // B, A'nın poliçesine zeyil AÇAMAZ (poliçe "bulunamadı" — FK de zaten tenant'lı).
            await Assert.ThrowsAsync<ValidationException>(() => regB.AddEndorsementAsync(Input(polA, "Z-X", 1m)));
            polB = await PoliceAsync(sb.ServiceProvider, "34 ZY 51"); // ham-RLS insert denemesi için geçerli FK
        }

        // HAM RLS: racar_app + B GUC'u → A satırı yok, UPDATE/DELETE 0 satır; A GUC'u → 1 satır.
        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();
        async Task SetTenant(Guid t)
        {
            await using var set = new NpgsqlCommand("select set_config('app.tenant_id', @t, false)", conn);
            set.Parameters.AddWithValue("t", t.ToString());
            await set.ExecuteScalarAsync();
        }

        // RLS ENABLE + FORCE gerçekten açık mı (migration'daki elle blok) — katalogdan doğrula.
        // FORCE olmadan tablo SAHİBİ (racar_owner) policy'yi atlar; ayrıca racar_app NOBYPASSRLS.
        await using (var kat = new NpgsqlCommand(
            "select relrowsecurity, relforcerowsecurity from pg_class where relname = 'InsurancePolicyZeyilleri'", conn))
        await using (var r = await kat.ExecuteReaderAsync())
        {
            Assert.True(await r.ReadAsync());
            Assert.True(r.GetBoolean(0), "ENABLE ROW LEVEL SECURITY yok");
            Assert.True(r.GetBoolean(1), "FORCE ROW LEVEL SECURITY yok");
        }

        await SetTenant(b);
        await using (var say = new NpgsqlCommand("select count(*) from \"InsurancePolicyZeyilleri\" where \"TenantId\" = @a", conn))
        {
            say.Parameters.AddWithValue("a", a);
            Assert.Equal(0L, (long)(await say.ExecuteScalarAsync())!);
        }
        await using (var upd = new NpgsqlCommand("update \"InsurancePolicyZeyilleri\" set \"Neden\" = 'hack' where \"TenantId\" = @a", conn))
        {
            upd.Parameters.AddWithValue("a", a);
            Assert.Equal(0, await upd.ExecuteNonQueryAsync());
        }
        await using (var del = new NpgsqlCommand("delete from \"InsurancePolicyZeyilleri\"", conn))
            Assert.Equal(0, await del.ExecuteNonQueryAsync());

        await SetTenant(a);
        await using (var say2 = new NpgsqlCommand("select count(*) from \"InsurancePolicyZeyilleri\" where \"TenantId\" = @a", conn))
        {
            say2.Parameters.AddWithValue("a", a);
            Assert.Equal(1L, (long)(await say2.ExecuteScalarAsync())!); // FORCE RLS kendi tenant'ında engellemez
        }
        // Yabancı TenantId ile INSERT (GUC hâlâ A): WITH CHECK reddeder — sızdırma YÖNÜ de kapalı.
        // FK geçerli seçildi (B'nin kendi poliçesi) → red RLS'ten gelir, FK'den değil.
        await using (var ins = new NpgsqlCommand(
            "insert into \"InsurancePolicyZeyilleri\" (\"Id\",\"TenantId\",\"PolicyId\",\"ZeyilNo\",\"Tarih\",\"Deger\",\"Brut\",\"Net\",\"FonVergi\",\"CreatedAtUtc\") " +
            "values (@id, @b, @p, 'X-1', now(), 0, 0, 0, 0, now())", conn))
        {
            ins.Parameters.AddWithValue("id", Guid.NewGuid());
            ins.Parameters.AddWithValue("b", b);
            ins.Parameters.AddWithValue("p", polB);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => ins.ExecuteNonQueryAsync());
            Assert.Equal("42501", ex.SqlState); // RLS WITH CHECK ihlali
        }
    }
}
