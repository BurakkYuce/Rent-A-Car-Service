using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Application.Personnel;
using RentACar.Domain.Common;
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
    private static readonly DateOnly Day = new(2026, 6, 15);   // Pazartesi (sabit — now'a bağlı değil)

    private static async Task<Guid> StaffAsync(IServiceProvider sp, string code, string name, string? branch = null)
        => await sp.GetRequiredService<PersonnelService>().CreateAsync(new PersonelInput
        { Kod = code, Ad = name, Soyad = "Test", Sube = branch });

    private static async Task<Guid> BranchAsync(IServiceProvider sp, string name)
        => await sp.GetRequiredService<BranchService>().CreateAsync(new BranchInput { Kod = name, Ad = name });

    private static VardiyaInput V(Guid staff, DateOnly day, string start, string bit, string? branch = null)
        => new()
        {
            PersonelId = staff, Tarih = day,
            BaslangicSaat = TimeOnly.Parse(start), BitisSaat = TimeOnly.Parse(bit), Sube = branch
        };

    [Fact]
    public async Task CRUD_round_trip_ve_sure_ELLE_dogrulanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        await BranchAsync(sp, "Merkez");
        var p = await StaffAsync(sp, "P1", "Ali");
        var svc = sp.GetRequiredService<StaffShiftService>();

        var id = await svc.CreateAsync(new VardiyaInput
        {
            PersonelId = p, Tarih = Day,
            BaslangicSaat = new TimeOnly(8, 0), BitisSaat = new TimeOnly(18, 30),
            Sube = " Merkez ", Aciklama = "  havalimanı destek  "
        });

        var v = await svc.GetAsync(id);
        Assert.NotNull(v);
        Assert.Equal(p, v!.PersonelId);
        Assert.Equal(Day, v.Tarih);
        Assert.Equal(new TimeOnly(8, 0), v.BaslangicSaat);
        Assert.Equal("Merkez", v.Sube);                 // trim
        Assert.NotNull(v.SubeId);                       // FK metinden çözüldü (interceptor)
        Assert.Equal("havalimanı destek", v.Aciklama);  // trim
        Assert.Equal(630, v.SureDk);                    // ELLE: 10 sa 30 dk = 630

        Assert.True(await svc.UpdateAsync(id, V(p, Day, "09:00", "17:00", "Merkez")));
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
        var p = await StaffAsync(sp, "P1", "Gece");
        var svc = sp.GetRequiredService<StaffShiftService>();

        var id = await svc.CreateAsync(V(p, Day, "22:00", "06:00"));
        Assert.Equal(480, (await svc.GetAsync(id))!.SureDk);   // ELLE: 22->24 (120) + 0->6 (360) = 480

        // Sıfır uzunluk reddedilir (gece vardiyasıyla karışmasın).
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(V(p, Day.AddDays(2), "09:00", "09:00")));
        Assert.Contains("sıfır olamaz", ex.Message);
    }

    [Fact]
    public async Task Cakisan_vardiya_REDDEDILIR_bolunmus_mesai_SERBEST()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var p = await StaffAsync(sp, "P1", "Ali");
        var q = await StaffAsync(sp, "P2", "Veli");
        var svc = sp.GetRequiredService<StaffShiftService>();

        await svc.CreateAsync(V(p, Day, "08:00", "12:00"));

        // Bölünmüş mesai: bitişik (12:00-18:00) çakışma DEĞİL — yarı açık aralık.
        await svc.CreateAsync(V(p, Day, "12:00", "18:00"));

        // Gerçek kesişim → red.
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(V(p, Day, "11:00", "13:00")));
        Assert.Contains("çakışan vardiya", ex.Message);

        // BAŞKA personel aynı saatte serbest.
        await svc.CreateAsync(V(q, Day, "11:00", "13:00"));

        Assert.Equal(3, (await svc.ListAsync(new VardiyaFilter { Bas = Day, Bit = Day })).Count);
    }

    [Fact]
    public async Task Gece_vardiyasi_ERTESI_GUN_sabah_vardiyasiyla_cakisirsa_YAKALANIR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var p = await StaffAsync(sp, "P1", "Gece");
        var svc = sp.GetRequiredService<StaffShiftService>();

        await svc.CreateAsync(V(p, Day, "22:00", "06:00"));           // 15'i 22:00 -> 16'sı 06:00

        // 16'sı 05:00-13:00 → gece vardiyasının son saatiyle kesişir. Gün-içi saat karşılaştıran
        // bir kontrol bunu göremezdi (05:00 < 22:00 diye).
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(V(p, Day.AddDays(1), "05:00", "13:00")));
        Assert.Contains("çakışan vardiya", ex.Message);

        // 16'sı 06:00-14:00 → tam bitişik, çakışmaz.
        await svc.CreateAsync(V(p, Day.AddDays(1), "06:00", "14:00"));

        // Güncellemede kayıt KENDİSİYLE çakışmamalı.
        var own = (await svc.ListAsync(new VardiyaFilter { Bas = Day.AddDays(1), Bit = Day.AddDays(1) }))
            .Single().Vardiya;
        Assert.True(await svc.UpdateAsync(own.Id, V(p, Day.AddDays(1), "06:00", "15:00")));
    }

    [Fact]
    public async Task Matris_ELLE_kurulan_senaryoyu_dogru_yerlestirir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var ali = await StaffAsync(sp, "P1", "Ali");
        var guardian = await StaffAsync(sp, "P2", "Veli");
        var svc = sp.GetRequiredService<StaffShiftService>();

        // ELLE: 2 personel × 3 gün = 6 vardiya, hepsi 08:00-16:00 (480 dk).
        foreach (var p in new[] { ali, guardian })
            for (var i = 0; i < 3; i++)
                await svc.CreateAsync(V(p, Day.AddDays(i), "08:00", "16:00"));

        var m = await svc.MatrixAsync(new VardiyaFilter { Bas = Day, Bit = Day.AddDays(4) });

        Assert.Equal(5, m.Gunler.Count);            // sütunlar aralığın TAMAMI (2 boş gün dahil)
        Assert.Equal(Day, m.Gunler[0]);
        Assert.Equal(Day.AddDays(4), m.Gunler[4]);
        Assert.Equal(2, m.Satirlar.Count);
        Assert.Equal(6, m.ToplamVardiya);
        Assert.Equal(6 * 480, m.ToplamDk);          // ELLE: 2880

        var rowAli = m.Satirlar.Single(x => x.PersonelId == ali);
        Assert.Equal("Ali Test", rowAli.PersonelAd);
        Assert.Equal(3 * 480, rowAli.ToplamDk);
        Assert.Equal("24 sa 00 dk", rowAli.ToplamSaatMetni);
        Assert.Equal(3, rowAli.Gunler.Count);
        Assert.Single(rowAli.Gunler[Day]);
        Assert.False(rowAli.Gunler.ContainsKey(Day.AddDays(3)));   // vardiyasız gün hücresi boş
    }

    [Fact]
    public async Task Pencere_bos_ters_ve_asiri_genis_girdiyi_duzeltir()
    {
        // Saf fonksiyon — DB gerekmez.
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

        var (b1, s1) = StaffShiftService.Window(new VardiyaFilter());
        Assert.Equal(today, b1);
        Assert.Equal(today.AddDays(6), s1);                       // ELLE: 7 günlük varsayılan

        // Ters aralık boş sayfa yerine düzeltilir.
        var (b2, s2) = StaffShiftService.Window(new VardiyaFilter { Bas = Day.AddDays(5), Bit = Day });
        Assert.Equal(Day, b2);
        Assert.Equal(Day.AddDays(5), s2);

        // 10 yıllık istek tavana kırpılır (personel×gün ızgarası patlamasın).
        var (b3, s3) = StaffShiftService.Window(new VardiyaFilter { Bas = Day, Bit = Day.AddYears(10) });
        Assert.Equal(Day, b3);
        Assert.Equal(Day.AddDays(StaffShiftService.MaxDays - 1), s3);
    }

    [Fact]
    public async Task Sube_kapsami_OPERATORU_kendi_subesiyle_sinirlar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid ali, guardian;
        using (var admin = host.ScopeFor(tenant))
        {
            var sp = admin.ServiceProvider;
            await BranchAsync(sp, "Sube A");
            await BranchAsync(sp, "Sube B");
            ali = await StaffAsync(sp, "P1", "Ali", "Sube B");     // KADROSU B'de
            guardian = await StaffAsync(sp, "P2", "Veli", "Sube B");
            var svc = sp.GetRequiredService<StaffShiftService>();
            // ELLE: 2 vardiya A'da (biri kadrosu B olan Ali'nin), 1 vardiya B'de.
            await svc.CreateAsync(V(ali, Day, "08:00", "16:00", "Sube A"));
            await svc.CreateAsync(V(guardian, Day, "08:00", "16:00", "Sube A"));
            await svc.CreateAsync(V(guardian, Day.AddDays(1), "08:00", "16:00", "Sube B"));
        }

        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Sube A");
        var opSvc = op.ServiceProvider.GetRequiredService<StaffShiftService>();
        var rows = await opSvc.ListAsync(new VardiyaFilter { Bas = Day, Bit = Day.AddDays(3) });

        // ELLE: 2 — kapsam VARDİYANIN şubesine bakar; Ali'nin kadrosu B olsa da A'da çalıştığı
        // vardiya A operatörüne görünür.
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("Sube A", r.Vardiya.Sube));

        // Operatör başka şubeye vardiya YAZAMAZ.
        await Assert.ThrowsAsync<NoPermissionException>(() => opSvc.CreateAsync(V(ali, Day.AddDays(5), "08:00", "16:00", "Sube B")));
        // 2026-09-25: kadrosu kapsam DIŞI personele kendi şubesinde de yazamaz (vardiya saatleri kehaneti).
        await Assert.ThrowsAsync<NoPermissionException>(() => opSvc.CreateAsync(V(ali, Day.AddDays(5), "08:00", "16:00", "Sube A")));
        // Kadrosu kendi şubesinde olan personele yazabilir.
        Guid ayse;
        using (var admin = host.ScopeFor(tenant)) ayse = await StaffAsync(admin.ServiceProvider, "P3", "Ayşe", "Sube A");
        await opSvc.CreateAsync(V(ayse, Day.AddDays(5), "08:00", "16:00", "Sube A"));
    }

    // 2026-09-25 — kapsam dışı personele yazma 403: çakışma kontrolü başka şubenin vardiya saatleri için kehanet olmaz.
    // Senaryo elle: Veli'nin kadrosu B, B'de 09:00–12:00 vardiyası var. A operatörü Veli için A'da 10:00–11:00 dener.
    // Kapsam kontrolü çakışmadan ÖNCE geldiği için çakışan ve çakışmayan deneme AYNI 403'ü alır (ayırt edilemez).
    [Fact]
    public async Task Kapsam_disi_personele_yazma_403_ve_cakisma_kehaneti_yok()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        var day = DateOnly.FromDateTime(TestZaman.DaysLater(15).UtcDateTime);
        Guid guardian, nobody, shiftB;
        using (var admin = host.ScopeFor(tenant))
        {
            var sp = admin.ServiceProvider;
            await BranchAsync(sp, "Sube A");
            await BranchAsync(sp, "Sube B");
            guardian = await StaffAsync(sp, "P1", "Veli", "Sube B");
            nobody = await StaffAsync(sp, "P2", "Serbest");              // şubesiz
            shiftB = await sp.GetRequiredService<StaffShiftService>().CreateAsync(V(guardian, day, "09:00", "12:00", "Sube B"));
        }

        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Sube A");
        var opSvc = op.ServiceProvider.GetRequiredService<StaffShiftService>();

        var overlapping = await Assert.ThrowsAsync<NoPermissionException>(() => opSvc.CreateAsync(V(guardian, day, "10:00", "11:00", "Sube A")));
        var free = await Assert.ThrowsAsync<NoPermissionException>(() => opSvc.CreateAsync(V(guardian, day, "14:00", "15:00", "Sube A")));
        Assert.Equal(free.Message, overlapping.Message);
        Assert.Equal("Bu kayıt şube kapsamınız dışında.", overlapping.Message);
        // Şubesiz personel de kapsamlı operatör için kapsam dışı.
        await Assert.ThrowsAsync<NoPermissionException>(() => opSvc.CreateAsync(V(nobody, day, "14:00", "15:00", "Sube A")));

        // Admin (kapsamsız) aynı personele yazabilir; yalnız bir kayıt eklenir.
        using var admin2 = host.ScopeFor(tenant);
        await admin2.ServiceProvider.GetRequiredService<StaffShiftService>().CreateAsync(V(guardian, day, "14:00", "15:00", "Sube B"));
        var all = await admin2.ServiceProvider.GetRequiredService<StaffShiftService>()
            .ListAsync(new VardiyaFilter { Bas = day, Bit = day });
        Assert.Equal(2, all.Count);
        Assert.Contains(all, r => r.Vardiya.Id == shiftB);
    }

    // #302 L2 — satır ve sürüm TEK tutarlı çift: satır okunurken araya giren yazım yeni sürümü eski alanlarla eşleştiremez.
    [Fact]
    public async Task Satir_ve_surum_arada_yazim_olsa_da_tutarli_cift_doner()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var day = DateOnly.FromDateTime(TestZaman.DaysLater(12).UtcDateTime);
        var p = await StaffAsync(sp, "P1", "Ali");
        var id = await sp.GetRequiredService<StaffShiftService>().CreateAsync(V(p, day, "08:00", "12:00"));

        var inner = sp.GetRequiredService<IRowVersionStore>();
        // İlk sürüm okumasının BAŞINDA başka bir oturum vardiyayı 10:00'a çeker (yarış penceresi deterministik kurulur).
        var racing = new WriteOnFirstVersionRead(inner, id);
        var svc = new StaffShiftService(
            sp.GetRequiredService<IPersonnelShiftRepository>(), sp.GetRequiredService<IPersonnelRepository>(),
            sp.GetRequiredService<IBranchRepository>(), sp.GetRequiredService<ICurrentUser>(), racing);

        var pair = await svc.GetWithStaffAndVersionAsync(id);
        Assert.NotNull(pair);
        var (row, version) = pair!.Value;
        Assert.True(racing.Wrote);
        // ELLE: yarışan yazım başlangıcı 10:00 yaptı → dönen alanlar ve sürüm ikisi de yazım SONRASI.
        Assert.Equal(new TimeOnly(10, 0), row.Vardiya.BaslangicSaat);
        Assert.Equal(await inner.GetVersionAsync<PersonelVardiya>(id), version);
    }

    private sealed class WriteOnFirstVersionRead(IRowVersionStore inner, Guid target) : IRowVersionStore
    {
        public bool Wrote { get; private set; }

        public async Task<string?> GetVersionAsync<T>(Guid id, CancellationToken ct = default) where T : class
        {
            if (!Wrote && id == target && typeof(T) == typeof(PersonelVardiya))
            {
                Wrote = true;
                var v = await inner.GetVersionAsync<PersonelVardiya>(id, ct);
                await inner.UpdateAsync<PersonelVardiya>(id, v!, r => r.BaslangicSaat = new TimeOnly(10, 0), "dup", ct);
            }
            return await inner.GetVersionAsync<T>(id, ct);
        }

        public Task<bool> UpdateAsync<T>(Guid id, string expectedVersion, Action<T> apply, string duplicateMessage,
            CancellationToken ct = default) where T : class
            => inner.UpdateAsync(id, expectedVersion, apply, duplicateMessage, ct);
    }

    // #302 L1 — çakışan vardiya operatörün kapsamı dışındaki şubedeyse mesaj tarih/saat taşımaz; kapsam içindeyse taşır.
    [Fact]
    public async Task Cakisma_mesaji_kapsam_disi_subenin_saatini_sizdirmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        var day = DateOnly.FromDateTime(TestZaman.DaysLater(10).UtcDateTime);
        Guid ali;
        using (var admin = host.ScopeFor(tenant))
        {
            var sp = admin.ServiceProvider;
            await BranchAsync(sp, "Sube A");
            await BranchAsync(sp, "Sube B");
            // Kadrosu A (operatörün kapsamında); B'de de vardiyası var — kapsam dışı ŞUBEDEKİ çakışma.
            ali = await StaffAsync(sp, "P1", "Ali", "Sube A");
            var svc = sp.GetRequiredService<StaffShiftService>();
            await svc.CreateAsync(V(ali, day, "09:30", "13:15", "Sube B"));   // kapsam dışı (operatör A'da)
            await svc.CreateAsync(V(ali, day, "15:00", "17:45", "Sube A"));   // kapsam içi
        }

        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Sube A");
        var opSvc = op.ServiceProvider.GetRequiredService<StaffShiftService>();

        var hidden = await Assert.ThrowsAsync<ValidationException>(() => opSvc.CreateAsync(V(ali, day, "12:00", "14:00", "Sube A")));
        Assert.Equal("Bu personelin bu saatlerde başka bir şubede vardiyası var.", hidden.Message);
        Assert.DoesNotContain("09:30", hidden.Message);
        Assert.DoesNotContain("13:15", hidden.Message);
        Assert.DoesNotContain(day.ToString("dd.MM.yyyy"), hidden.Message);

        var visible = await Assert.ThrowsAsync<ValidationException>(() => opSvc.CreateAsync(V(ali, day, "17:00", "19:00", "Sube A")));
        Assert.Contains("çakışan vardiya", visible.Message);
        Assert.Contains(day.ToString("dd.MM.yyyy"), visible.Message);
        Assert.Contains("15:00", visible.Message);

        // Admin (kapsamsız) kapsam dışı yok: B'deki satır için de ayrıntılı mesaj.
        using var admin2 = host.ScopeFor(tenant);
        var full = await Assert.ThrowsAsync<ValidationException>(() => admin2.ServiceProvider
            .GetRequiredService<StaffShiftService>().CreateAsync(V(ali, day, "12:00", "14:00", "Sube A")));
        Assert.Contains("09:30", full.Message);
    }

    [Fact]
    public async Task Gecersiz_girdi_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var p = await StaffAsync(sp, "P1", "Ali");
        var svc = sp.GetRequiredService<StaffShiftService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(V(Guid.Empty, Day, "08:00", "16:00")));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(V(Guid.NewGuid(), Day, "08:00", "16:00")));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(V(p, default, "08:00", "16:00")));
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(V(p, Day, "08:00", "16:00", "Olmayan Şube")));
        Assert.Contains("Şube bulunamadı", ex.Message);

        Assert.Empty(await svc.ListAsync(new VardiyaFilter { Bas = Day, Bit = Day.AddDays(30) }));
    }

    [Fact]
    public async Task Muhasebe_rolu_RAPORU_gorur_ama_YAZAMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid p;
        using (var admin = host.ScopeFor(tenant))
        {
            p = await StaffAsync(admin.ServiceProvider, "P1", "Ali");
            await admin.ServiceProvider.GetRequiredService<StaffShiftService>()
                .CreateAsync(V(p, Day, "08:00", "16:00"));
        }

        using var acct = host.ScopeFor(tenant, Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = acct.ServiceProvider.GetRequiredService<StaffShiftService>();
        Assert.Single(await svc.ListAsync(new VardiyaFilter { Bas = Day, Bit = Day }));   // ViewReports
        await Assert.ThrowsAsync<NoPermissionException>(() => svc.CreateAsync(V(p, Day.AddDays(1), "08:00", "16:00")));
    }

    [Fact]
    public async Task Sube_birlestirmede_vardiyalar_HEDEFE_tasinir()
    {
        // FAZ-23'ün kapsam çiti testi bu eksiği yakalamıştı — burada davranış olarak da kilitleniyor.
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var source = await BranchAsync(sp, "Sube A");
        var target = await BranchAsync(sp, "Sube B");
        var p = await StaffAsync(sp, "P1", "Ali");
        var svc = sp.GetRequiredService<StaffShiftService>();
        var id = await svc.CreateAsync(V(p, Day, "08:00", "16:00", "Sube A"));

        var branches = sp.GetRequiredService<BranchService>();
        var preview = await branches.PreviewMergeAsync(source, target);
        Assert.Contains(preview!.Etkilenen, x => x.Tablo.StartsWith("Personel vardiyası"));

        await branches.MergeAsync(source, target);

        var v = await svc.GetAsync(id);
        Assert.Equal("Sube B", v!.Sube);      // metin
        Assert.Equal(target, v.SubeId);        // FK
    }

    [Fact]
    public async Task Vardiya_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        Guid id;
        using (var s1 = host.ScopeFor(t1))
        {
            var p = await StaffAsync(s1.ServiceProvider, "P1", "Gizli");
            id = await s1.ServiceProvider.GetRequiredService<StaffShiftService>()
                .CreateAsync(V(p, Day, "08:00", "16:00"));
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var svc = s2.ServiceProvider.GetRequiredService<StaffShiftService>();
        var f = new VardiyaFilter { Bas = Day.AddDays(-30), Bit = Day.AddDays(30) };
        Assert.Empty(await svc.ListAsync(f));
        Assert.Empty((await svc.MatrixAsync(f)).Satirlar);
        Assert.Null(await svc.GetAsync(id));                  // id bilinse bile görünmez
        Assert.False(await svc.DeleteAsync(id));              // silinemez

        // Kaynak tenant'ta hâlâ duruyor (silinmediği kanıtlanır).
        using var s1b = host.ScopeFor(t1);
        Assert.NotNull(await s1b.ServiceProvider.GetRequiredService<StaffShiftService>().GetAsync(id));
    }
}
