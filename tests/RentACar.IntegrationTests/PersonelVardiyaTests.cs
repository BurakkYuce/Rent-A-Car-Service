using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Application.Personnel;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-45 — Personel vardiya dikeyi.
///
/// <para><b>Bağımsız oracle:</b> beklenen süreler/adetler burada ELLE hesaplanır
/// ("08:00-18:00 = 600 dk" sabiti <c>VardiyaZaman</c>'dan türetilmez).</para>
///
/// <para><b>Gece vardiyası</b> (bitiş &lt; başlangıç) bilinçli olarak GEÇERLİ; süresi gün aşımıyla
/// hesaplanır ve ertesi günün sabah vardiyasıyla çakışması YAKALANIR — yalnız gün-içi saat
/// karşılaştıran bir kontrol bunu kaçırırdı.</para>
/// </summary>
[Collection("postgres")]
public sealed class PersonelVardiyaTests(PostgresFixture fx)
{
    private static readonly DateOnly Gun = new(2026, 6, 15);   // Pazartesi (sabit — now'a bağlı değil)

    private static async Task<Guid> PersonelAsync(IServiceProvider sp, string kod, string ad, string? sube = null)
        => await sp.GetRequiredService<PersonelService>().CreateAsync(new PersonelInput
        { Kod = kod, Ad = ad, Soyad = "Test", Sube = sube });

    private static async Task<Guid> SubeAsync(IServiceProvider sp, string ad)
        => await sp.GetRequiredService<BranchService>().CreateAsync(new BranchInput { Kod = ad, Ad = ad });

    private static VardiyaInput V(Guid personel, DateOnly gun, string bas, string bit, string? sube = null)
        => new()
        {
            PersonelId = personel, Tarih = gun,
            BaslangicSaat = TimeOnly.Parse(bas), BitisSaat = TimeOnly.Parse(bit), Sube = sube
        };

    [Fact]
    public async Task CRUD_round_trip_ve_sure_ELLE_dogrulanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        await SubeAsync(sp, "Merkez");
        var p = await PersonelAsync(sp, "P1", "Ali");
        var svc = sp.GetRequiredService<PersonelVardiyaService>();

        var id = await svc.CreateAsync(new VardiyaInput
        {
            PersonelId = p, Tarih = Gun,
            BaslangicSaat = new TimeOnly(8, 0), BitisSaat = new TimeOnly(18, 30),
            Sube = " Merkez ", Aciklama = "  havalimanı destek  "
        });

        var v = await svc.GetAsync(id);
        Assert.NotNull(v);
        Assert.Equal(p, v!.PersonelId);
        Assert.Equal(Gun, v.Tarih);
        Assert.Equal(new TimeOnly(8, 0), v.BaslangicSaat);
        Assert.Equal("Merkez", v.Sube);                 // trim
        Assert.NotNull(v.SubeId);                       // FK metinden çözüldü (interceptor)
        Assert.Equal("havalimanı destek", v.Aciklama);  // trim
        Assert.Equal(630, v.SureDk);                    // ELLE: 10 sa 30 dk = 630

        Assert.True(await svc.UpdateAsync(id, V(p, Gun, "09:00", "17:00", "Merkez")));
        Assert.Equal(480, (await svc.GetAsync(id))!.SureDk);   // ELLE: 8 sa = 480

        Assert.True(await svc.DeleteAsync(id));
        Assert.Null(await svc.GetAsync(id));
    }

    [Fact]
    public async Task Gece_vardiyasi_GECERLI_ve_suresi_gun_asimiyla_hesaplanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var p = await PersonelAsync(sp, "P1", "Gece");
        var svc = sp.GetRequiredService<PersonelVardiyaService>();

        var id = await svc.CreateAsync(V(p, Gun, "22:00", "06:00"));
        Assert.Equal(480, (await svc.GetAsync(id))!.SureDk);   // ELLE: 22->24 (120) + 0->6 (360) = 480

        // Sıfır uzunluk reddedilir (gece vardiyasıyla karışmasın).
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(V(p, Gun.AddDays(2), "09:00", "09:00")));
        Assert.Contains("sıfır olamaz", ex.Message);
    }

    [Fact]
    public async Task Cakisan_vardiya_REDDEDILIR_bolunmus_mesai_SERBEST()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var p = await PersonelAsync(sp, "P1", "Ali");
        var q = await PersonelAsync(sp, "P2", "Veli");
        var svc = sp.GetRequiredService<PersonelVardiyaService>();

        await svc.CreateAsync(V(p, Gun, "08:00", "12:00"));

        // Bölünmüş mesai: bitişik (12:00-18:00) çakışma DEĞİL — yarı açık aralık.
        await svc.CreateAsync(V(p, Gun, "12:00", "18:00"));

        // Gerçek kesişim → red.
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(V(p, Gun, "11:00", "13:00")));
        Assert.Contains("çakışan vardiya", ex.Message);

        // BAŞKA personel aynı saatte serbest.
        await svc.CreateAsync(V(q, Gun, "11:00", "13:00"));

        Assert.Equal(3, (await svc.ListAsync(new VardiyaFilter { Bas = Gun, Bit = Gun })).Count);
    }

    [Fact]
    public async Task Gece_vardiyasi_ERTESI_GUN_sabah_vardiyasiyla_cakisirsa_YAKALANIR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var p = await PersonelAsync(sp, "P1", "Gece");
        var svc = sp.GetRequiredService<PersonelVardiyaService>();

        await svc.CreateAsync(V(p, Gun, "22:00", "06:00"));           // 15'i 22:00 -> 16'sı 06:00

        // 16'sı 05:00-13:00 → gece vardiyasının son saatiyle kesişir. Gün-içi saat karşılaştıran
        // bir kontrol bunu göremezdi (05:00 < 22:00 diye).
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(V(p, Gun.AddDays(1), "05:00", "13:00")));
        Assert.Contains("çakışan vardiya", ex.Message);

        // 16'sı 06:00-14:00 → tam bitişik, çakışmaz.
        await svc.CreateAsync(V(p, Gun.AddDays(1), "06:00", "14:00"));

        // Güncellemede kayıt KENDİSİYLE çakışmamalı.
        var kendi = (await svc.ListAsync(new VardiyaFilter { Bas = Gun.AddDays(1), Bit = Gun.AddDays(1) }))
            .Single().Vardiya;
        Assert.True(await svc.UpdateAsync(kendi.Id, V(p, Gun.AddDays(1), "06:00", "15:00")));
    }

    [Fact]
    public async Task Matris_ELLE_kurulan_senaryoyu_dogru_yerlestirir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var ali = await PersonelAsync(sp, "P1", "Ali");
        var veli = await PersonelAsync(sp, "P2", "Veli");
        var svc = sp.GetRequiredService<PersonelVardiyaService>();

        // ELLE: 2 personel × 3 gün = 6 vardiya, hepsi 08:00-16:00 (480 dk).
        foreach (var p in new[] { ali, veli })
            for (var i = 0; i < 3; i++)
                await svc.CreateAsync(V(p, Gun.AddDays(i), "08:00", "16:00"));

        var m = await svc.MatrisAsync(new VardiyaFilter { Bas = Gun, Bit = Gun.AddDays(4) });

        Assert.Equal(5, m.Gunler.Count);            // sütunlar aralığın TAMAMI (2 boş gün dahil)
        Assert.Equal(Gun, m.Gunler[0]);
        Assert.Equal(Gun.AddDays(4), m.Gunler[4]);
        Assert.Equal(2, m.Satirlar.Count);
        Assert.Equal(6, m.ToplamVardiya);
        Assert.Equal(6 * 480, m.ToplamDk);          // ELLE: 2880

        var satirAli = m.Satirlar.Single(x => x.PersonelId == ali);
        Assert.Equal("Ali Test", satirAli.PersonelAd);
        Assert.Equal(3 * 480, satirAli.ToplamDk);
        Assert.Equal("24 sa 00 dk", satirAli.ToplamSaatMetni);
        Assert.Equal(3, satirAli.Gunler.Count);
        Assert.Single(satirAli.Gunler[Gun]);
        Assert.False(satirAli.Gunler.ContainsKey(Gun.AddDays(3)));   // vardiyasız gün hücresi boş
    }

    [Fact]
    public async Task Pencere_bos_ters_ve_asiri_genis_girdiyi_duzeltir()
    {
        // Saf fonksiyon — DB gerekmez.
        var bugun = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

        var (b1, s1) = PersonelVardiyaService.Pencere(new VardiyaFilter());
        Assert.Equal(bugun, b1);
        Assert.Equal(bugun.AddDays(6), s1);                       // ELLE: 7 günlük varsayılan

        // Ters aralık boş sayfa yerine düzeltilir.
        var (b2, s2) = PersonelVardiyaService.Pencere(new VardiyaFilter { Bas = Gun.AddDays(5), Bit = Gun });
        Assert.Equal(Gun, b2);
        Assert.Equal(Gun.AddDays(5), s2);

        // 10 yıllık istek tavana kırpılır (personel×gün ızgarası patlamasın).
        var (b3, s3) = PersonelVardiyaService.Pencere(new VardiyaFilter { Bas = Gun, Bit = Gun.AddYears(10) });
        Assert.Equal(Gun, b3);
        Assert.Equal(Gun.AddDays(PersonelVardiyaService.MaxGun - 1), s3);
    }

    [Fact]
    public async Task Sube_kapsami_OPERATORU_kendi_subesiyle_sinirlar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid ali, veli;
        using (var admin = host.ScopeFor(tenant))
        {
            var sp = admin.ServiceProvider;
            await SubeAsync(sp, "Sube A");
            await SubeAsync(sp, "Sube B");
            ali = await PersonelAsync(sp, "P1", "Ali", "Sube B");     // KADROSU B'de
            veli = await PersonelAsync(sp, "P2", "Veli", "Sube B");
            var svc = sp.GetRequiredService<PersonelVardiyaService>();
            // ELLE: 2 vardiya A'da (biri kadrosu B olan Ali'nin), 1 vardiya B'de.
            await svc.CreateAsync(V(ali, Gun, "08:00", "16:00", "Sube A"));
            await svc.CreateAsync(V(veli, Gun, "08:00", "16:00", "Sube A"));
            await svc.CreateAsync(V(veli, Gun.AddDays(1), "08:00", "16:00", "Sube B"));
        }

        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Sube A");
        var opSvc = op.ServiceProvider.GetRequiredService<PersonelVardiyaService>();
        var rows = await opSvc.ListAsync(new VardiyaFilter { Bas = Gun, Bit = Gun.AddDays(3) });

        // ELLE: 2 — kapsam VARDİYANIN şubesine bakar; Ali'nin kadrosu B olsa da A'da çalıştığı
        // vardiya A operatörüne görünür.
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("Sube A", r.Vardiya.Sube));

        // Operatör başka şubeye vardiya YAZAMAZ.
        await Assert.ThrowsAsync<ValidationException>(() => opSvc.CreateAsync(V(ali, Gun.AddDays(5), "08:00", "16:00", "Sube B")));
        // Kendi şubesine yazabilir.
        await opSvc.CreateAsync(V(ali, Gun.AddDays(5), "08:00", "16:00", "Sube A"));
    }

    [Fact]
    public async Task Gecersiz_girdi_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var p = await PersonelAsync(sp, "P1", "Ali");
        var svc = sp.GetRequiredService<PersonelVardiyaService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(V(Guid.Empty, Gun, "08:00", "16:00")));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(V(Guid.NewGuid(), Gun, "08:00", "16:00")));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(V(p, default, "08:00", "16:00")));
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(V(p, Gun, "08:00", "16:00", "Olmayan Şube")));
        Assert.Contains("Şube bulunamadı", ex.Message);

        Assert.Empty(await svc.ListAsync(new VardiyaFilter { Bas = Gun, Bit = Gun.AddDays(30) }));
    }

    [Fact]
    public async Task Muhasebe_rolu_RAPORU_gorur_ama_YAZAMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid p;
        using (var admin = host.ScopeFor(tenant))
        {
            p = await PersonelAsync(admin.ServiceProvider, "P1", "Ali");
            await admin.ServiceProvider.GetRequiredService<PersonelVardiyaService>()
                .CreateAsync(V(p, Gun, "08:00", "16:00"));
        }

        using var muh = host.ScopeFor(tenant, Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = muh.ServiceProvider.GetRequiredService<PersonelVardiyaService>();
        Assert.Single(await svc.ListAsync(new VardiyaFilter { Bas = Gun, Bit = Gun }));   // ViewReports
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(V(p, Gun.AddDays(1), "08:00", "16:00")));
    }

    [Fact]
    public async Task Sube_birlestirmede_vardiyalar_HEDEFE_tasinir()
    {
        // FAZ-23'ün kapsam çiti testi bu eksiği yakalamıştı — burada davranış olarak da kilitleniyor.
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var kaynak = await SubeAsync(sp, "Sube A");
        var hedef = await SubeAsync(sp, "Sube B");
        var p = await PersonelAsync(sp, "P1", "Ali");
        var svc = sp.GetRequiredService<PersonelVardiyaService>();
        var id = await svc.CreateAsync(V(p, Gun, "08:00", "16:00", "Sube A"));

        var subeler = sp.GetRequiredService<BranchService>();
        var onizleme = await subeler.BirlestirOnizleAsync(kaynak, hedef);
        Assert.Contains(onizleme!.Etkilenen, x => x.Tablo.StartsWith("Personel vardiyası"));

        await subeler.BirlestirAsync(kaynak, hedef);

        var v = await svc.GetAsync(id);
        Assert.Equal("Sube B", v!.Sube);      // metin
        Assert.Equal(hedef, v.SubeId);        // FK
    }

    [Fact]
    public async Task Vardiya_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        Guid id;
        using (var s1 = host.ScopeFor(t1))
        {
            var p = await PersonelAsync(s1.ServiceProvider, "P1", "Gizli");
            id = await s1.ServiceProvider.GetRequiredService<PersonelVardiyaService>()
                .CreateAsync(V(p, Gun, "08:00", "16:00"));
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var svc = s2.ServiceProvider.GetRequiredService<PersonelVardiyaService>();
        var f = new VardiyaFilter { Bas = Gun.AddDays(-30), Bit = Gun.AddDays(30) };
        Assert.Empty(await svc.ListAsync(f));
        Assert.Empty((await svc.MatrisAsync(f)).Satirlar);
        Assert.Null(await svc.GetAsync(id));                  // id bilinse bile görünmez
        Assert.False(await svc.DeleteAsync(id));              // silinemez

        // Kaynak tenant'ta hâlâ duruyor (silinmediği kanıtlanır).
        using var s1b = host.ScopeFor(t1);
        Assert.NotNull(await s1b.ServiceProvider.GetRequiredService<PersonelVardiyaService>().GetAsync(id));
    }
}
