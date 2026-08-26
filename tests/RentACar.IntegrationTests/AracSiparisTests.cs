using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.AracKredileri;
using RentACar.Application.AracSiparisleri;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Reporting;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap L3 + FAZ-17 — araç sipariş/tedarik. BAĞIMSIZ ORACLE: No "SP-" boşluksuz; durum
/// Bekliyor→Onaylandi→TeslimAlindi; tedarikçi zorunlu; tenant izolasyonu (racar_app, RLS).
///
/// <para><b>FAZ-17 kapsamı:</b> tedarikçi Cari-FK + kredi FK, dosya/temsilci/spesifikasyon alanları,
/// üç fiyat katmanı (Piyasa/Ops/Filo) ve liste süzgeci. Beklenen değerler ELLE kurulan senaryodan
/// yazılmıştır (ör. 3 adet × 750.000 = <b>2.250.000</b> resmi toplam — servis kodundan değil).</para>
///
/// <para><b>PARA ÇİTİ:</b> fiyat katmanları ve cari/kredi bağı BİLGİdir. Kırılgan regresyon testi
/// <see cref="Fiyat_katmanlari_ve_cari_bagi_DEFTERE_yazmaz"/> uçuk değerlerle doldurulmuş bir
/// siparişin defteri ve cari bakiyesini kıpırdatmadığını ampirik olarak kilitler.</para>
/// </summary>
[Collection("postgres")]
public sealed class AracSiparisTests(PostgresFixture fx)
{
    /// <summary>Saat dilimi kaymasına kapalı sabit gün (ayın ortası).</summary>
    private static DateTimeOffset D(int y, int m, int g) => new(y, m, g, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_no_and_durum_gecisi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<AracSiparisService>();

        var id = await svc.CreateAsync(new AracSiparisInput
        { Tedarikci = "ABC Otomotiv", Marka = "Toyota", Adet = 2, BirimFiyat = 500_000m });

        var s = await svc.GetAsync(id);
        BelgeNoOracle.BeklenenlerdenBiri(13, 1, s!.No);   // 13 = AracSiparis
        Assert.Equal(SiparisDurum.Bekliyor, s.Durum);
        Assert.Equal(2, s.Adet);

        Assert.True(await svc.OnaylaAsync(id));
        Assert.Equal(SiparisDurum.Onaylandi, (await svc.GetAsync(id))!.Durum);
        Assert.True(await svc.TeslimAlAsync(id));
        Assert.Equal(SiparisDurum.TeslimAlindi, (await svc.GetAsync(id))!.Durum);
    }

    [Fact]
    public async Task Tedarikci_zorunlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await Assert.ThrowsAsync<ValidationException>(() =>
            scope.ServiceProvider.GetRequiredService<AracSiparisService>()
                .CreateAsync(new AracSiparisInput { Tedarikci = "  ", Adet = 1, BirimFiyat = 100m }));
    }

    [Fact]
    public async Task Tenant_isolation()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var a = host.ScopeFor(Guid.NewGuid()))
        {
            await a.ServiceProvider.GetRequiredService<AracSiparisService>()
                .CreateAsync(new AracSiparisInput { Tedarikci = "Gizli Tedarikçi", Adet = 1, BirimFiyat = 100m });
        }
        using var b = host.ScopeFor(Guid.NewGuid());
        var svc = b.ServiceProvider.GetRequiredService<AracSiparisService>();
        Assert.Empty(await svc.ListAsync());
        // FAZ-17: filtreli yol da izole olmalı — süzgeç açıkken de başka tenant'ın satırı GÖRÜNMEZ.
        Assert.Empty(await svc.SearchAsync(new AracSiparisFilter { Ara = "Gizli" }));
    }

    // ------------------------------------------------------------------ FAZ-17 alan derinliği

    /// <summary>
    /// Yeni alanların tam round-trip'i. ORACLE: her alan ELLE yazılan sabitle geri okunmalı;
    /// resmi toplam 3 × 750.000 = 2.250.000 (fiyat katmanları toplama KATILMAZ).
    /// </summary>
    [Fact]
    public async Task Yeni_alanlar_round_trip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Kurumsal, Unvan = "Yetkili Bayi A.Ş." });
        var kredi = await sp.GetRequiredService<AracKrediService>()
            .CreateAsync(new AracKrediInput { BankaAdi = "Ziraat", KrediTutari = 900_000m, TaksitSayisi = 24 });
        var svc = sp.GetRequiredService<AracSiparisService>();

        var id = await svc.CreateAsync(new AracSiparisInput
        {
            Tedarikci = "Yetkili Bayi",
            TedarikciCariId = cari,
            SiparisTarihi = D(2026, 3, 10),
            ImzaTarih = D(2026, 3, 12),
            BeklenenTeslim = D(2026, 6, 15),
            DosyaNo = "DS-2026-17",
            SatisTemsilci = "Ahmet Yılmaz",
            OzelTemsilci = "Filo Masası",
            Marka = "Fiat", Tip = "Egea", Grup = "B",
            Versiyon = "1.5 Turbo Elite",
            Opsiyon = "Kış paketi + çeki demiri",
            Renk = "Beyaz", IcRenk = "Siyah",
            KaynakTip = "ÖzMal", SatisTipi = "Sıfır",
            TsbKayitNo = "TSB-4455",
            KrediId = kredi,
            Adet = 3, BirimFiyat = 750_000m,
            PiyasaFiyat = 810_000m, OpsFiyat = 790_000m, FiloFiyat = 735_000m,
            Doviz = "eur", Kur = 37.5m,
            Aciklama = "Filo alımı"
        });

        var s = (await svc.GetAsync(id))!;
        Assert.Equal(cari, s.TedarikciCariId);
        Assert.Equal(kredi, s.KrediId);
        Assert.Equal("DS-2026-17", s.DosyaNo);
        Assert.Equal(D(2026, 3, 12), s.ImzaTarih);
        Assert.Equal("Ahmet Yılmaz", s.SatisTemsilci);
        Assert.Equal("Filo Masası", s.OzelTemsilci);
        Assert.Equal("1.5 Turbo Elite", s.Versiyon);
        Assert.Equal("Kış paketi + çeki demiri", s.Opsiyon);
        Assert.Equal("Beyaz", s.Renk);
        Assert.Equal("Siyah", s.IcRenk);
        Assert.Equal("ÖzMal", s.KaynakTip);
        Assert.Equal("Sıfır", s.SatisTipi);
        Assert.Equal("TSB-4455", s.TsbKayitNo);
        Assert.Equal(810_000m, s.PiyasaFiyat);
        Assert.Equal(790_000m, s.OpsFiyat);
        Assert.Equal(735_000m, s.FiloFiyat);
        Assert.Equal("EUR", s.Currency);          // döviz kodu büyük harfe normalize
        Assert.Equal(37.5m, s.Kur);

        // RESMİ tutar DEĞİŞMEDİ: toplam yalnız Adet × BirimFiyat (elle: 3 × 750.000).
        Assert.Equal(750_000m, s.BirimFiyat);
        Assert.Equal(2_250_000m, s.Adet * s.BirimFiyat);
    }

    /// <summary>
    /// Fiyat katmanları girilmezse NULL kalır — 0'a çevrilmez ("girilmemiş" ile "bedelsiz" ayrı
    /// anlamlar; 0 yazmak listede ve export'ta yanlış bilgi olurdu).
    /// </summary>
    [Fact]
    public async Task Girilmeyen_fiyat_katmani_null_kalir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<AracSiparisService>();

        var id = await svc.CreateAsync(new AracSiparisInput { Tedarikci = "Bayi", Adet = 1, BirimFiyat = 100m });
        var s = (await svc.GetAsync(id))!;
        Assert.Null(s.PiyasaFiyat);
        Assert.Null(s.OpsFiyat);
        Assert.Null(s.FiloFiyat);
        Assert.Null(s.TedarikciCariId);
        Assert.Null(s.KrediId);
    }

    [Fact]
    public async Task Negatif_fiyat_katmani_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<AracSiparisService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new AracSiparisInput
        { Tedarikci = "Bayi", Adet = 1, BirimFiyat = 100m, PiyasaFiyat = -1m }));
    }

    /// <summary>
    /// Güncelleme round-trip'i + <b>iki kez kaydetme kayması yok</b>: aynı giriş ikinci kez
    /// uygulandığında hiçbir alan değişmemeli (form prefill/round-trip tuzağının servis-seviyesi
    /// kilidi). Durum güncellemeden ETKİLENMEZ.
    /// </summary>
    [Fact]
    public async Task Update_alanlari_gunceller_ve_ikinci_kayitta_kaymaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<AracSiparisService>();

        var id = await svc.CreateAsync(new AracSiparisInput
        {
            Tedarikci = "İlk Bayi", Adet = 1, BirimFiyat = 100_000m, Doviz = "USD", Kur = 34m,
            SiparisTarihi = D(2026, 2, 3), DosyaNo = "DS-A", Renk = "Kırmızı", PiyasaFiyat = 111_000m
        });
        await svc.OnaylaAsync(id);   // durum güncellemeden ETKİLENMEMELİ

        var yeni = new AracSiparisInput
        {
            Tedarikci = "İkinci Bayi", Adet = 4, BirimFiyat = 250_000m, Doviz = "EUR", Kur = 38.25m,
            SiparisTarihi = D(2026, 2, 3), ImzaTarih = D(2026, 2, 5), BeklenenTeslim = D(2026, 9, 1),
            DosyaNo = "DS-B", Renk = "Gri", IcRenk = "Bej", Versiyon = "Premium",
            PiyasaFiyat = 260_000m, OpsFiyat = 255_000m, FiloFiyat = 248_000m, TsbKayitNo = "TSB-1"
        };
        Assert.True(await svc.UpdateAsync(id, yeni));

        var s1 = (await svc.GetAsync(id))!;
        Assert.Equal("İkinci Bayi", s1.Tedarikci);
        Assert.Equal(4, s1.Adet);
        Assert.Equal(250_000m, s1.BirimFiyat);
        Assert.Equal("EUR", s1.Currency);
        Assert.Equal(38.25m, s1.Kur);
        Assert.Equal("Bej", s1.IcRenk);
        Assert.Equal(D(2026, 2, 5), s1.ImzaTarih);
        Assert.Equal(SiparisDurum.Onaylandi, s1.Durum);   // durum korunur
        Assert.Equal("DS-B", s1.DosyaNo);

        // İKİNCİ kez AYNI giriş: hiçbir alan kaymamalı (tarih ±1 gün, döviz TRY'ye düşme vb.).
        Assert.True(await svc.UpdateAsync(id, yeni));
        var s2 = (await svc.GetAsync(id))!;
        Assert.Equal(s1.SiparisTarihi, s2.SiparisTarihi);
        Assert.Equal(s1.ImzaTarih, s2.ImzaTarih);
        Assert.Equal(s1.BeklenenTeslim, s2.BeklenenTeslim);
        Assert.Equal(s1.BirimFiyat, s2.BirimFiyat);
        Assert.Equal(s1.PiyasaFiyat, s2.PiyasaFiyat);
        Assert.Equal(s1.Currency, s2.Currency);
        Assert.Equal(s1.Kur, s2.Kur);
        Assert.Equal(s1.No, s2.No);
    }

    /// <summary>Sipariş tarihi boş gelirse MEVCUT tarih korunur (güncellemede "bugün"e kaymaz).</summary>
    [Fact]
    public async Task Update_bos_siparis_tarihi_mevcut_degeri_korur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<AracSiparisService>();

        var id = await svc.CreateAsync(new AracSiparisInput
        { Tedarikci = "Bayi", Adet = 1, BirimFiyat = 10m, SiparisTarihi = D(2026, 1, 20) });
        await svc.UpdateAsync(id, new AracSiparisInput { Tedarikci = "Bayi", Adet = 1, BirimFiyat = 10m });

        Assert.Equal(D(2026, 1, 20), (await svc.GetAsync(id))!.SiparisTarihi);
    }

    [Fact]
    public async Task Iptal_siparis_duzenlenemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<AracSiparisService>();

        var id = await svc.CreateAsync(new AracSiparisInput { Tedarikci = "Bayi", Adet = 1, BirimFiyat = 10m });
        await svc.IptalAsync(id);

        await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(id,
            new AracSiparisInput { Tedarikci = "Yeni", Adet = 2, BirimFiyat = 20m }));
    }

    // ------------------------------------------------------------------ FK bağları

    /// <summary>
    /// Kredi/cari bağı: VAR OLAN id ile bağlanır ve geri okunur (oracle: seed edilen id'nin
    /// kendisi); OLMAYAN id ile temiz <see cref="ValidationException"/> (500 değil).
    /// </summary>
    [Fact]
    public async Task Kredi_ve_cari_bagi_var_olan_id_ile_baglanir_olmayanla_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var kredi = await sp.GetRequiredService<AracKrediService>()
            .CreateAsync(new AracKrediInput { BankaAdi = "Vakıf", KrediTutari = 500_000m, TaksitSayisi = 12 });
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Kurumsal, Unvan = "Bayi A.Ş." });
        var svc = sp.GetRequiredService<AracSiparisService>();

        var id = await svc.CreateAsync(new AracSiparisInput
        { Tedarikci = "Bayi", Adet = 1, BirimFiyat = 100m, KrediId = kredi, TedarikciCariId = cari });
        var s = (await svc.GetAsync(id))!;
        Assert.Equal(kredi, s.KrediId);
        Assert.Equal(cari, s.TedarikciCariId);

        // Olmayan kredi → FK ihlali ValidationException'a çevrilir.
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new AracSiparisInput
        { Tedarikci = "Bayi", Adet = 1, BirimFiyat = 100m, KrediId = Guid.NewGuid() }));

        // Olmayan cari → aynı şekilde.
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new AracSiparisInput
        { Tedarikci = "Bayi", Adet = 1, BirimFiyat = 100m, TedarikciCariId = Guid.NewGuid() }));

        // Güncelleme yolu da korunmalı (yalnız create'te yakalamak yarım savunma olurdu).
        await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(id, new AracSiparisInput
        { Tedarikci = "Bayi", Adet = 1, BirimFiyat = 100m, KrediId = Guid.NewGuid() }));
    }

    /// <summary>
    /// Çapraz-tenant bağ YAPISAL olarak imkânsız: A tenant'ının kredisi B tenant'ının siparişine
    /// bağlanamaz (composite tenant-FK). Bağlantı racar_app ile → RLS de devrede.
    /// </summary>
    [Fact]
    public async Task Baska_tenantin_kredisine_baglanamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var a = Guid.NewGuid();
        Guid krediA;
        using (var sa = host.ScopeFor(a))
        {
            krediA = await sa.ServiceProvider.GetRequiredService<AracKrediService>()
                .CreateAsync(new AracKrediInput { BankaAdi = "A Bank", KrediTutari = 100_000m, TaksitSayisi = 6 });
        }

        using var sb = host.ScopeFor(Guid.NewGuid());
        await Assert.ThrowsAsync<ValidationException>(() =>
            sb.ServiceProvider.GetRequiredService<AracSiparisService>().CreateAsync(new AracSiparisInput
            { Tedarikci = "B Bayi", Adet = 1, BirimFiyat = 10m, KrediId = krediA }));
    }

    // ------------------------------------------------------------------ liste süzgeci

    /// <summary>
    /// Liste süzgeci. BAĞIMSIZ ORACLE: 3 sipariş ELLE kurulur —
    /// (1) cari X'e bağlı "Alfa Otomotiv", 10.01.2026, TSB "34ABC01";
    /// (2) carisiz "Beta Motors", 20.02.2026, marka "Renault";
    /// (3) carisiz "Alfa Servis", 05.03.2026, dosya "DS-77".
    /// Beklenen satır sayıları elle sayılmıştır.
    /// </summary>
    [Fact]
    public async Task Search_filtreleri_sonucu_daraltir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cariX = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Kurumsal, Unvan = "Zümrüt Filo A.Ş." });
        var svc = sp.GetRequiredService<AracSiparisService>();

        await svc.CreateAsync(new AracSiparisInput
        {
            Tedarikci = "Alfa Otomotiv", TedarikciCariId = cariX, SiparisTarihi = D(2026, 1, 10),
            Adet = 1, BirimFiyat = 100m, TsbKayitNo = "34ABC01", Marka = "Fiat"
        });
        await svc.CreateAsync(new AracSiparisInput
        {
            Tedarikci = "Beta Motors", SiparisTarihi = D(2026, 2, 20),
            Adet = 1, BirimFiyat = 200m, Marka = "Renault"
        });
        await svc.CreateAsync(new AracSiparisInput
        {
            Tedarikci = "Alfa Servis", SiparisTarihi = D(2026, 3, 5),
            Adet = 1, BirimFiyat = 300m, DosyaNo = "DS-77"
        });

        Assert.Equal(3, (await svc.SearchAsync()).Count);                                        // süzgeçsiz
        Assert.Single(await svc.SearchAsync(new AracSiparisFilter { CariId = cariX }));          // 1: cari X
        Assert.Equal(2, (await svc.SearchAsync(new AracSiparisFilter { Ara = "alfa" })).Count);  // 1+3 (harf-duyarsız)
        // Cari ÜNVANINDAN arama: serbest metin tedarikçide "Zümrüt" geçmiyor, yalnız cari kartında.
        Assert.Single(await svc.SearchAsync(new AracSiparisFilter { Ara = "Zümrüt" }));
        Assert.Single(await svc.SearchAsync(new AracSiparisFilter { Arac = "34ABC" }));          // TSB/geçici plaka
        Assert.Single(await svc.SearchAsync(new AracSiparisFilter { Arac = "renault" }));        // marka
        Assert.Single(await svc.SearchAsync(new AracSiparisFilter { DosyaNo = "77" }));
        // Tarih aralığı: 01.02–28.02 penceresine yalnız 2. sipariş girer.
        Assert.Single(await svc.SearchAsync(new AracSiparisFilter { Bas = D(2026, 2, 1), Bit = D(2026, 2, 28) }));
        Assert.Equal(2, (await svc.SearchAsync(new AracSiparisFilter { Bas = D(2026, 2, 1) })).Count);
        Assert.Empty(await svc.SearchAsync(new AracSiparisFilter { Durum = SiparisDurum.TeslimAlindi }));
        Assert.Equal(3, (await svc.SearchAsync(new AracSiparisFilter { Durum = SiparisDurum.Bekliyor })).Count);
    }

    // ------------------------------------------------------------------ yetki

    [Fact]
    public async Task Yetki_yazma_OperationsWrite_okuma_serbest()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using (var admin = host.ScopeFor(tenant))
        {
            await admin.ServiceProvider.GetRequiredService<AracSiparisService>()
                .CreateAsync(new AracSiparisInput { Tedarikci = "Bayi", Adet = 1, BirimFiyat = 10m });
        }

        // Muhasebe'de OperationsWrite YOK → yazma reddedilir, okuma (ViewReports/FinanceWrite) serbest.
        using (var muhasebe = host.ScopeFor(tenant, role: UserRole.Muhasebe))
        {
            var svc = muhasebe.ServiceProvider.GetRequiredService<AracSiparisService>();
            Assert.Single(await svc.SearchAsync());
            await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
                new AracSiparisInput { Tedarikci = "X", Adet = 1, BirimFiyat = 1m }));
        }

        // Operatör'de OperationsWrite VAR → geçer.
        using (var op = host.ScopeFor(tenant, role: UserRole.Operator))
        {
            var svc = op.ServiceProvider.GetRequiredService<AracSiparisService>();
            Assert.Single(await svc.SearchAsync());
            var id = await svc.CreateAsync(new AracSiparisInput { Tedarikci = "Y", Adet = 1, BirimFiyat = 1m });
            Assert.True(await svc.UpdateAsync(id, new AracSiparisInput { Tedarikci = "Y2", Adet = 1, BirimFiyat = 1m }));
        }
    }

    // ------------------------------------------------------------------ "deftere yazmaz" kilidi

    /// <summary>
    /// KARARLAR.md genel politikası — kırılgan regresyon: UÇUK fiyat katmanları + cari/kredi bağı
    /// olan bir sipariş defteri ve cari bakiyesini KIPIRDATMAZ; resmi toplam da yalnız
    /// Adet × BirimFiyat olarak kalır (elle: 2 × 400.000 = 800.000).
    /// </summary>
    [Fact]
    public async Task Fiyat_katmanlari_ve_cari_bagi_DEFTERE_yazmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Kurumsal, Unvan = "Sipariş Bayisi A.Ş." });
        // Cariye GERÇEK bir para hareketi: defterin "önce" fotoğrafı bu olur.
        await sp.GetRequiredService<CashService>().CollectAsync(new CashInput
        { CariId = cari, Tutar = 700m, Hesap = LedgerAccountType.Kasa });

        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        async Task<int> SatirSayisiAsync()
        {
            await using var c = await factory.CreateDbContextAsync();
            return await c.AccountLedgerEntries.AsNoTracking().CountAsync();
        }
        async Task<decimal> BakiyeAsync()
            => (await sp.GetRequiredService<ReportService>().GetCariBalancesAsync())
                .Where(b => b.CariId == cari).Sum(b => b.Bakiye);

        var satirOnce = await SatirSayisiAsync();
        var bakiyeOnce = await BakiyeAsync();
        Assert.Equal(2, satirOnce);        // tahsilat = 1 borç + 1 alacak
        Assert.Equal(-700m, bakiyeOnce);   // müşteri alacaklı (Credit → negatif)

        var kredi = await sp.GetRequiredService<AracKrediService>()
            .CreateAsync(new AracKrediInput { BankaAdi = "Halk", KrediTutari = 1_000_000m, TaksitSayisi = 24 });
        var svc = sp.GetRequiredService<AracSiparisService>();
        var id = await svc.CreateAsync(new AracSiparisInput
        {
            Tedarikci = "Sipariş Bayisi", TedarikciCariId = cari, KrediId = kredi,
            Adet = 2, BirimFiyat = 400_000m,
            PiyasaFiyat = 987_654_321m, OpsFiyat = 123_456_789m, FiloFiyat = 555_555_555m
        });
        await svc.UpdateAsync(id, new AracSiparisInput
        {
            Tedarikci = "Sipariş Bayisi", TedarikciCariId = cari, KrediId = kredi,
            Adet = 2, BirimFiyat = 400_000m,
            PiyasaFiyat = 999_999_999m, OpsFiyat = 888_888_888m, FiloFiyat = 777_777_777m
        });

        Assert.Equal(satirOnce, await SatirSayisiAsync());   // defter büyümedi
        Assert.Equal(bakiyeOnce, await BakiyeAsync());       // cari bakiye kıpırdamadı

        var s = (await svc.GetAsync(id))!;
        Assert.Equal(800_000m, s.Adet * s.BirimFiyat);       // resmi toplam katmanlardan ETKİLENMEZ
    }
}
