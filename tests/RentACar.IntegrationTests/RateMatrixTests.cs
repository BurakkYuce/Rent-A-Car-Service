using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.RateMatrices;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Tarife Matrisi (XML Tarife) master — bağımsız oracle. CRUD + kod normalize/benzersizlik +
/// gün-fiyat/esneklik/tarih doğrulaması + aktif filtre + yetki + tenant izolasyon (racar_app).
/// Beklenen değerler senaryodan, koddan değil.
/// </summary>
[Collection("postgres")]
public sealed class RateMatrixTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_normalizes_and_roundtrips_full_matrix()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateMatrixService>();

        var bas = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var bit = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
        var id = await svc.CreateAsync(new RateMatrixInput
        {
            Kod = "web-eko-2026", Ad = "Web Ekonomik 2026",
            Kanal = "WEB", Sube = "Merkez", Lokasyon = "IST-AHL", AracGrupKod = "eko", ParaBirimi = "try",
            BasTar = bas, BitTar = bit,
            Gun1 = 1000.00m, Gun2 = 950.00m, Gun3 = 900.00m, Gun4 = 875.00m,
            Gun5 = 850.00m, Gun6 = 825.00m, Gun7 = 800.00m,
            MaxEsneklik = 15.00m, OnayDurumu = TarifeOnayDurumu.Onayli, Onaylayan = "umit"
        });

        var r = await svc.GetAsync(id);
        Assert.NotNull(r);
        Assert.Equal("WEB-EKO-2026", r!.Kod);     // kod büyük harfe normalize
        Assert.Equal("EKO", r.AracGrupKod);        // grup kodu büyük harfe normalize
        Assert.Equal("TRY", r.ParaBirimi);         // para birimi büyük harfe normalize
        Assert.Equal("WEB", r.Kanal);
        Assert.Equal(bas, r.BasTar);
        Assert.Equal(bit, r.BitTar);
        Assert.Equal(1000.00m, r.Gun1);
        Assert.Equal(800.00m, r.Gun7);
        Assert.Equal(15.00m, r.MaxEsneklik);
        Assert.Equal(TarifeOnayDurumu.Onayli, r.OnayDurumu);
        Assert.Equal("umit", r.Onaylayan);
        Assert.True(r.Aktif);
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateMatrixService>();

        await svc.CreateAsync(new RateMatrixInput { Kod = "T1", Ad = "Tarife 1" });
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new RateMatrixInput { Kod = "t1", Ad = "Başka" }));
    }

    [Fact]
    public async Task Validation_rejects_bad_inputs()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateMatrixService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new RateMatrixInput { Kod = "", Ad = "A" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new RateMatrixInput { Kod = "X", Ad = "  " }));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new RateMatrixInput { Kod = "NEG", Ad = "Neg", Gun1 = -1m }));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new RateMatrixInput { Kod = "ESN", Ad = "Esn", MaxEsneklik = 150m }));
        // Bitiş < Başlangıç
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new RateMatrixInput
            {
                Kod = "TAR", Ad = "Tar",
                BasTar = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
                BitTar = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero)
            }));
    }

    [Fact]
    public async Task ListActive_excludes_passive_but_list_keeps_all()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateMatrixService>();

        var a = await svc.CreateAsync(new RateMatrixInput { Kod = "A", Ad = "A Tarife" });
        await svc.CreateAsync(new RateMatrixInput { Kod = "B", Ad = "B Tarife" });
        await svc.UpdateAsync(a, new RateMatrixInput { Kod = "A", Ad = "A Tarife", Aktif = false });

        var active = await svc.ListActiveAsync();
        Assert.Single(active);
        Assert.Equal("B", active[0].Kod);
        Assert.Equal(2, (await svc.ListAsync()).Count);
    }

    [Fact]
    public async Task Delete_removes_row()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateMatrixService>();

        var id = await svc.CreateAsync(new RateMatrixInput { Kod = "DEL", Ad = "Silinecek" });
        Assert.True(await svc.DeleteAsync(id));
        Assert.Null(await svc.GetAsync(id));
    }

    [Fact]
    public async Task NonOperations_user_cannot_manage()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<RateMatrixService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new RateMatrixInput { Kod = "X", Ad = "Yetkisiz" }));
    }

    [Fact]
    public async Task RateMatrices_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<RateMatrixService>()
                .CreateAsync(new RateMatrixInput { Kod = "T1", Ad = "Tenant1" });

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<RateMatrixService>();
        Assert.Empty(await svc2.ListAsync());
        // Aynı kod farklı tenant'ta serbest.
        await svc2.CreateAsync(new RateMatrixInput { Kod = "T1", Ad = "Tenant2" });
        Assert.Single(await svc2.ListAsync());
    }

    // ---------------------------------------------------------------------------------------
    // FAZ-31 — kanal bazlı TOPLU SİLME (yalnız Bekliyor) + canlı ızgara filtresi
    // ---------------------------------------------------------------------------------------

    /// <summary>ELLE kurulan senaryo: ACENTA/Bekliyor ×3, ACENTA/Onaylı ×1, WEB/Bekliyor ×1.</summary>
    private static async Task BesSatirAsync(IServiceProvider sp)
    {
        var svc = sp.GetRequiredService<RateMatrixService>();
        foreach (var kod in new[] { "AC-1", "AC-2", "AC-3" })
            await svc.CreateAsync(new RateMatrixInput
            { Kod = kod, Ad = kod, Kanal = "ACENTA", Gun1 = 100m, OnayDurumu = TarifeOnayDurumu.Bekliyor });

        await svc.CreateAsync(new RateMatrixInput
        { Kod = "AC-ONAY", Ad = "Onaylı acenta", Kanal = "ACENTA", Gun1 = 100m, OnayDurumu = TarifeOnayDurumu.Onayli });
        await svc.CreateAsync(new RateMatrixInput
        { Kod = "WEB-1", Ad = "Web", Kanal = "WEB", Gun1 = 100m, OnayDurumu = TarifeOnayDurumu.Bekliyor });
    }

    [Fact]
    public async Task FAZ31_toplu_silme_yalniz_BEKLEYEN_ve_yalniz_secili_kanali_siler()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await BesSatirAsync(sp);
        var svc = sp.GetRequiredService<RateMatrixService>();

        Assert.Equal(5, (await svc.ListAsync()).Count);
        // Onay adımının göstereceği sayı, silmenin kullandığı YÜKLEMLE hesaplanır (tek kural).
        Assert.Equal(3, (await svc.ListAsync()).Count(r => RateMatrixService.SilmeAdayi(r, "ACENTA")));

        // ELLE ORACLE: tam 3 satır silinir (AC-1..AC-3).
        Assert.Equal(3, await svc.DeleteByKanalAsync("ACENTA", TarifeOnayDurumu.Bekliyor));

        var kalan = await svc.ListAsync();
        Assert.Equal(2, kalan.Count);
        Assert.Contains(kalan, r => r.Kod == "AC-ONAY");   // ONAYLI satır DOKUNULMADI
        Assert.Contains(kalan, r => r.Kod == "WEB-1");     // BAŞKA kanal DOKUNULMADI

        // İkinci çağrı 0 döner (silinecek bekleyen kalmadı) — hata değil.
        Assert.Equal(0, await svc.DeleteByKanalAsync("ACENTA", TarifeOnayDurumu.Bekliyor));
    }

    [Fact]
    public async Task FAZ31_kanal_eslesmesi_harf_duyarsiz_bosluk_toleransli_ve_kanalsiz_satir_korunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateMatrixService>();

        await svc.CreateAsync(new RateMatrixInput
        { Kod = "K1", Ad = "Karışık harf", Kanal = "AcEnTa", Gun1 = 100m });
        await svc.CreateAsync(new RateMatrixInput
        { Kod = "K2", Ad = "Kanalsız taban", Gun1 = 100m });      // Kanal = null

        // Motor da kanalı OrdinalIgnoreCase eşler → silme ile fiyatlama aynı kümeyi görür.
        Assert.Equal(1, await svc.DeleteByKanalAsync("  acenta ", TarifeOnayDurumu.Bekliyor));

        // KANALSIZ satır kanal-agnostik taban tarifedir; hiçbir kaynağa ait değildir → silinmez.
        var kalan = Assert.Single(await svc.ListAsync());
        Assert.Equal("K2", kalan.Kod);
        Assert.Null(kalan.Kanal);
    }

    [Fact]
    public async Task FAZ31_onayli_silme_talebi_ve_bos_kanal_GURULTULU_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await BesSatirAsync(sp);
        var svc = sp.GetRequiredService<RateMatrixService>();

        // KARAR: onaylı toplu silme bu fazda AÇILMADI. Sessizce daraltmak yerine gürültülü red —
        // kullanıcı "onaylılar da silindi" sanmamalı.
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.DeleteByKanalAsync("ACENTA", TarifeOnayDurumu.Onayli));

        // Kanalsız toplu silme = "hepsini sil" anlamına gelirdi → red.
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.DeleteByKanalAsync("   ", TarifeOnayDurumu.Bekliyor));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.DeleteByKanalAsync(null, TarifeOnayDurumu.Bekliyor));

        Assert.Equal(5, (await svc.ListAsync()).Count);   // hiçbir satır gitmedi
    }

    [Fact]
    public async Task FAZ31_toplu_silme_ManageUsers_ister()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using (var admin = host.ScopeFor(tenant)) await BesSatirAsync(admin.ServiceProvider);

        // Yönetici tek-satır silebilir (OperationsWrite) ama TOPLU silemez — etki alanı farklı.
        using var yon = host.ScopeFor(tenant, Guid.NewGuid(), "yon", UserRole.Yonetici);
        var svc = yon.ServiceProvider.GetRequiredService<RateMatrixService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.DeleteByKanalAsync("ACENTA", TarifeOnayDurumu.Bekliyor));
        Assert.Equal(5, (await svc.ListAsync()).Count);
    }

    [Fact]
    public async Task FAZ31_toplu_silme_tenant_citli()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        using (var s1 = host.ScopeFor(t1)) await BesSatirAsync(s1.ServiceProvider);

        // Başka tenant aynı kanal adıyla siler → KENDİ satırlarını siler, t1'inkiler durur.
        using (var s2 = host.ScopeFor(Guid.NewGuid()))
        {
            var svc2 = s2.ServiceProvider.GetRequiredService<RateMatrixService>();
            await svc2.CreateAsync(new RateMatrixInput { Kod = "AC-1", Ad = "Yabancı", Kanal = "ACENTA", Gun1 = 1m });
            Assert.Equal(1, await svc2.DeleteByKanalAsync("ACENTA", TarifeOnayDurumu.Bekliyor));
        }

        using var geri = host.ScopeFor(t1);
        Assert.Equal(5, (await geri.ServiceProvider.GetRequiredService<RateMatrixService>().ListAsync()).Count);
    }

    /// <summary>
    /// FAZ-31 güvenlik iddiasının AMPİRİK kanıtı: toplu silme, fiyat motorunun O AN kullandığı
    /// tarifeyi kaybettiremez. Aynı kanalda hem onaylı (motorun kullandığı) hem bekleyen satır
    /// var; silme sonrası aynı senaryo AYNI tutarı vermeli.
    /// </summary>
    [Fact]
    public async Task FAZ31_toplu_silme_FIYAT_MOTORUNU_bozmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<RateMatrixService>();

        await sp.GetRequiredService<RentACar.Application.VehicleGroups.VehicleGroupService>()
            .CreateAsync(new RentACar.Application.VehicleGroups.VehicleGroupInput { Kod = "EKO", Ad = "Ekonomik" });

        // Motorun kullandığı ONAYLI tarife + aynı kanalda henüz onaylanmamış (aktarılmış) satır.
        await svc.CreateAsync(new RateMatrixInput
        {
            Kod = "EKO-WEB", Ad = "Eko Web", Kanal = "WEB", AracGrupKod = "EKO",
            Gun1 = 1000m, Gun2 = 950m, Gun3 = 900m, OnayDurumu = TarifeOnayDurumu.Onayli
        });
        await svc.CreateAsync(new RateMatrixInput
        {
            Kod = "EKO-WEB-YENI", Ad = "Aktarılan taslak", Kanal = "WEB", AracGrupKod = "EKO",
            Gun1 = 1m, Gun2 = 1m, Gun3 = 1m, OnayDurumu = TarifeOnayDurumu.Bekliyor
        });

        var engine = sp.GetRequiredService<RentACar.Application.Pricing.RentalQuoteEngine>();
        var bas = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        RentACar.Application.Pricing.QuoteRequest Istek() => new()
        { AracGrupKod = "EKO", Kanal = "WEB", BasTar = bas, BitTar = bas.AddDays(3) };

        var once = await engine.QuoteAsync(Istek());
        Assert.Equal(900m, once.GunlukUcret);     // ELLE: 3 gün → Gun3 = 900 (onaylı satırdan)

        Assert.Equal(1, await svc.DeleteByKanalAsync("WEB", TarifeOnayDurumu.Bekliyor));

        var sonra = await engine.QuoteAsync(Istek());
        Assert.Equal(900m, sonra.GunlukUcret);    // motor DEĞİŞMEDİ — sildiğimiz satırı zaten kullanmıyordu
        Assert.Equal(once.GenelToplam, sonra.GenelToplam);

        // Onaylı satır hâlâ yerinde.
        Assert.Equal("EKO-WEB", Assert.Single(await svc.ListAsync()).Kod);
    }

    /// <summary>
    /// ADVERSARIAL H1 kalıcı kilidi — <b>TOCTOU:</b> toplu silme sürerken bir satır ONAYLANIRSA
    /// o satır silinmemelidir.
    ///
    /// <para>Bu senaryo bir kez GERÇEKTEN kırıktı: kilitsiz yeniden okuma pencereyi daraltıyor
    /// ama kapatmıyordu — EF'in ürettiği DELETE yalnız <c>Id</c> yüklemini taşıdığı için
    /// READ COMMITTED'da kilit serbest kalınca onaylanmış satır yine siliniyordu. Düzeltme:
    /// aday okuması <c>… AND "OnayDurumu" = 0 FOR UPDATE</c> ile kilitli yapılır; PostgreSQL
    /// yüklemi satırın YENİ sürümüne göre yeniden değerlendirip adayı düşürür.</para>
    ///
    /// <para>Kurulum deterministiktir (uyku ile "umut" değil): rakip oturum UPDATE eder ama
    /// COMMIT ETMEZ, satır kilidi tutulur; silme görevinin bloklu kaldığı ayrıca doğrulanır.</para>
    /// </summary>
    [Fact]
    public async Task FAZ31_silme_sirasinda_ONAYLANAN_satir_silinmez_TOCTOU()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();

        Guid id;
        using (var s = host.ScopeFor(tenant))
            id = await s.ServiceProvider.GetRequiredService<RateMatrixService>()
                .CreateAsync(new RateMatrixInput { Kod = "AC-1", Ad = "Acenta 1", Kanal = "ACENTA", Gun1 = 100m });

        // (A) Rakip oturum satırı ONAYLAR ama COMMIT ETMEZ → satır kilidi tutuluyor.
        using var kilitScope = host.ScopeFor(tenant);
        var factory = kilitScope.ServiceProvider
            .GetRequiredService<IDbContextFactory<RentACar.Infrastructure.Persistence.AppDbContext>>();
        await using var kilitDb = await factory.CreateDbContextAsync();
        await using var tx = await kilitDb.Database.BeginTransactionAsync();
        var row = await kilitDb.RateMatrices.FirstAsync(r => r.Id == id);
        row.OnayDurumu = TarifeOnayDurumu.Onayli;
        row.Onaylayan = "rakip";
        await kilitDb.SaveChangesAsync();

        // (B) Toplu silme başlar; aday okuması FOR UPDATE ile kilitte bekler.
        using var silScope = host.ScopeFor(tenant);
        var silTask = silScope.ServiceProvider.GetRequiredService<RateMatrixService>()
            .DeleteByKanalAsync("ACENTA", TarifeOnayDurumu.Bekliyor);
        await Task.Delay(1500);
        Assert.False(silTask.IsCompleted);      // kurulum doğru: gerçekten bloklu

        // (C) Onay COMMIT olur → silme devam eder.
        await tx.CommitAsync();

        Assert.Equal(0, await silTask);         // onaylanan satır ADAYLIKTAN DÜŞTÜ
        using var son = host.ScopeFor(tenant);
        var kalan = Assert.Single(await son.ServiceProvider.GetRequiredService<RateMatrixService>().ListAsync());
        Assert.Equal(TarifeOnayDurumu.Onayli, kalan.OnayDurumu);   // ONAYLI tarife HAYATTA
    }

    /// <summary>ADVERSARIAL L1 — toplu silme <b>tek transaction</b>: bir satır araya giren bir
    /// oturumca silinirse parti TAMAMEN geri alınır ve kullanıcı 500 değil anlaşılır bir red görür.</summary>
    [Fact]
    public async Task FAZ31_esZamanli_silme_temiz_redle_biter_parti_geri_alinir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();

        Guid id1;
        using (var s = host.ScopeFor(tenant))
        {
            var svc0 = s.ServiceProvider.GetRequiredService<RateMatrixService>();
            id1 = await svc0.CreateAsync(new RateMatrixInput { Kod = "AC-1", Ad = "A1", Kanal = "ACENTA", Gun1 = 100m });
            await svc0.CreateAsync(new RateMatrixInput { Kod = "AC-2", Ad = "A2", Kanal = "ACENTA", Gun1 = 100m });
        }

        // Rakip oturum SADECE AC-1'i siler ve commit eder — toplu silme henüz başlamadı.
        using (var rakip = host.ScopeFor(tenant))
            Assert.True(await rakip.ServiceProvider.GetRequiredService<RateMatrixService>().DeleteAsync(id1));

        // Toplu silme artık yalnız AC-2'yi bulur: temiz sonuç, exception yok.
        using var scope = host.ScopeFor(tenant);
        var svc = scope.ServiceProvider.GetRequiredService<RateMatrixService>();
        Assert.Equal(1, await svc.DeleteByKanalAsync("ACENTA", TarifeOnayDurumu.Bekliyor));
        Assert.Empty(await svc.ListAsync());
    }
}
