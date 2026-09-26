using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Crm;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-42 — sözleşmeye bağlı çıkış/dönüş anketi + AnketCevap child tablosu.
///
/// <para><b>SORU METNİ CEVAPLA BİRLİKTE SAKLANIR (snapshot):</b> form soruları ileride değişse
/// bile geçmiş anket hangi soruya ne cevap verildiğini olduğu gibi taşır. Düz 8 kolon olsaydı
/// soru metni tek yerde durur ve geçmiş anketlerin soruları da değişmiş görünürdü.</para>
///
/// <para><b>"Yapılmadı" da bir kayıttır</b> — müşterinin anketi reddettiği bilgisi operasyonel
/// değer taşır; cevapsız anket geçerlidir.</para>
/// </summary>
[Collection("postgres")]
public sealed class AnketSozlesmeTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset T0 = TestZaman.Simdi().AddDays(-2);

    private static async Task<(Guid kira, Guid cari)> KiraAsync(IServiceProvider sp, string plaka, string? ofis)
    {
        var cari = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Kurumsal, Unvan = "Anket A.Ş." });
        var arac = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        var kira = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = cari, VehicleId = arac, BasTar = T0, BitTar = T0.AddDays(3), GunlukUcret = 1000m, CikisOfisi = ofis });
        return (kira, cari);
    }

    private static List<AnketCevapInput> Cevaplar(int adet) =>
        Enumerable.Range(1, adet).Select(i => new AnketCevapInput
        { SoruNo = i, Soru = $"Soru {i}", Cevap = $"Cevap {i}", Aciklama = i == 1 ? "not" : null }).ToList();

    [Fact]
    public async Task Sozlesmeye_bagli_anket_CIKIS_OFISINI_sozlesmeden_alir_ve_cevaplar_kaydedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var (kira, cari) = await KiraAsync(sp, "34 AN 01", "Merkez Ofis");
        var svc = sp.GetRequiredService<SurveyService>();

        var id = await svc.CreateAsync(new AnketInput
        {
            CariId = cari, RentalId = kira, AnketTuru = SurveyType.Donus, Durum = SurveyStatus.Yapildi,
            Puan = 9, Yorum = "Memnun", Tarih = T0.AddDays(3), Cevaplar = Cevaplar(8)
        });

        var d = await svc.GetDetailAsync(id);
        Assert.Equal(kira, d!.Anket.RentalId);
        Assert.Equal(SurveyType.Donus, d.Anket.AnketTuru);
        Assert.Equal(SurveyStatus.Yapildi, d.Anket.Durum);
        Assert.Equal("Merkez Ofis", d.Anket.CikisOfisi);      // sözleşmeden SNAPSHOT
        Assert.Equal(9, d.Anket.Puan);

        Assert.Equal(8, d.Cevaplar.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], d.Cevaplar.Select(c => c.SoruNo).ToArray());
        Assert.Equal("Soru 1", d.Cevaplar[0].Soru);
        Assert.Equal("Cevap 8", d.Cevaplar[7].Cevap);
        Assert.Equal("not", d.Cevaplar[0].Aciklama);
    }

    [Fact]
    public async Task ELLE_yazilan_cikis_ofisi_sozlesmeden_geleni_EZMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var (kira, _) = await KiraAsync(sp, "34 AN 02", "Merkez Ofis");
        var svc = sp.GetRequiredService<SurveyService>();

        var id = await svc.CreateAsync(new AnketInput
        { RentalId = kira, CikisOfisi = " Havalimanı Ofisi ", Puan = 5, Cevaplar = Cevaplar(1) });

        Assert.Equal("Havalimanı Ofisi", (await svc.GetAsync(id))!.CikisOfisi);
    }

    [Fact]
    public async Task YAPILMADI_kaydi_ve_SOZLESMESIZ_anket_gecerlidir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<SurveyService>();

        // Cevapsız "Yapılmadı" — müşteri anketi reddetti.
        var id = await svc.CreateAsync(new AnketInput
        { Durum = SurveyStatus.Yapilmadi, Puan = 0, Yorum = "Müşteri anket istemedi" });

        var d = await svc.GetDetailAsync(id);
        Assert.Equal(SurveyStatus.Yapilmadi, d!.Anket.Durum);
        Assert.Null(d.Anket.RentalId);
        Assert.Null(d.Anket.AnketTuru);
        Assert.Empty(d.Cevaplar);
    }

    [Fact]
    public async Task Soru_metni_SNAPSHOT_form_degisse_de_gecmis_anket_BOZULMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<SurveyService>();

        // Eski form soruları ile bir anket.
        var eski = await svc.CreateAsync(new AnketInput
        {
            Puan = 8,
            Cevaplar = [new AnketCevapInput { SoruNo = 1, Soru = "ESKİ SORU: Araç temiz miydi?", Cevap = "Evet" }]
        });

        // Yeni form soruları ile başka bir anket.
        var yeni = await svc.CreateAsync(new AnketInput
        {
            Puan = 7,
            Cevaplar = [new AnketCevapInput { SoruNo = 1, Soru = "YENİ SORU: Araç bakımlı mıydı?", Cevap = "Evet" }]
        });

        // ELLE: eski anket ESKİ soruyu taşımaya devam ediyor.
        Assert.Equal("ESKİ SORU: Araç temiz miydi?", (await svc.GetDetailAsync(eski))!.Cevaplar[0].Soru);
        Assert.Equal("YENİ SORU: Araç bakımlı mıydı?", (await svc.GetDetailAsync(yeni))!.Cevaplar[0].Soru);
    }

    [Fact]
    public async Task Guncelleme_cevaplari_TAMAMEN_degistirir_kalinti_birakmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<SurveyService>();
        var id = await svc.CreateAsync(new AnketInput { Puan = 5, Cevaplar = Cevaplar(8) });
        Assert.Equal(8, (await svc.GetDetailAsync(id))!.Cevaplar.Count);

        // Formdan 3 soru gönderiliyor → 8 satır KALMAMALI (kısmi güncelleme yapsaydık ekranda
        // görünmeyen 5 eski cevap satırı kalırdı).
        Assert.True(await svc.UpdateAsync(id, new AnketInput
        { Puan = 6, Durum = SurveyStatus.Yapildi, Cevaplar = Cevaplar(3) }));

        var d = await svc.GetDetailAsync(id);
        Assert.Equal(3, d!.Cevaplar.Count);
        Assert.Equal(6, d.Anket.Puan);
    }

    [Fact]
    public async Task Bos_soruli_satir_KAYIT_URETMEZ_ve_cift_sira_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<SurveyService>();

        // Soru metni boş → satır atılır.
        var id = await svc.CreateAsync(new AnketInput
        {
            Puan = 5,
            Cevaplar =
            [
                new AnketCevapInput { SoruNo = 1, Soru = "Soru 1", Cevap = "Evet" },
                new AnketCevapInput { SoruNo = 2, Soru = "   ", Cevap = "yazılmamalı" }
            ]
        });
        Assert.Single((await svc.GetDetailAsync(id))!.Cevaplar);

        // Aynı sıra iki kez → net mesajla red (DB unique'ine takılmadan).
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new AnketInput
        {
            Puan = 5,
            Cevaplar =
            [
                new AnketCevapInput { SoruNo = 1, Soru = "A" },
                new AnketCevapInput { SoruNo = 1, Soru = "B" }
            ]
        }));
        Assert.Contains("Aynı soru sırası", ex.Message);
    }

    [Fact]
    public async Task Gecersiz_girdi_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<SurveyService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new AnketInput { Puan = 11 }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new AnketInput { Puan = 5, RentalId = Guid.NewGuid() }));
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new AnketInput { Puan = 5, Tarih = DateTimeOffset.UtcNow.AddYears(2) }));
        Assert.Contains("gelecekte olamaz", ex.Message);

        Assert.Empty(await svc.ListAsync());
    }

    [Fact]
    public async Task Filtreler_ELLE_beklenen_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var (kira, cari) = await KiraAsync(sp, "34 AN 03", "Merkez");
        var svc = sp.GetRequiredService<SurveyService>();

        // ELLE: 3 anket.
        await svc.CreateAsync(new AnketInput
        { CariId = cari, RentalId = kira, AnketTuru = SurveyType.Cikis, Puan = 9, Tarih = T0 });
        await svc.CreateAsync(new AnketInput
        { CariId = cari, RentalId = kira, AnketTuru = SurveyType.Donus, Puan = 7, Tarih = T0.AddDays(1),
          Durum = SurveyStatus.Yapilmadi });
        await svc.CreateAsync(new AnketInput
        { Puan = 5, Tarih = T0.AddDays(2), CikisOfisi = "Şube 2" });

        Assert.Equal(3, (await svc.SearchAsync()).Count);
        Assert.Equal(2, (await svc.SearchAsync(new AnketFilter { CariId = cari })).Count);
        Assert.Single(await svc.SearchAsync(new AnketFilter { AnketTuru = SurveyType.Cikis }));
        Assert.Single(await svc.SearchAsync(new AnketFilter { Durum = SurveyStatus.Yapilmadi }));
        Assert.Equal(2, (await svc.SearchAsync(new AnketFilter { Durum = SurveyStatus.Yapildi })).Count);
        Assert.Equal(2, (await svc.SearchAsync(new AnketFilter { CikisOfisi = "Merkez" })).Count);
        Assert.Single(await svc.SearchAsync(new AnketFilter { CikisOfisi = "Şube 2" }));
        Assert.Equal(2, (await svc.SearchAsync(new AnketFilter { TarihMin = T0.AddDays(1) })).Count);
        Assert.Equal(2, (await svc.SearchAsync(new AnketFilter { TarihMax = T0.AddDays(1) })).Count);
        // Birleşik: cari + dönüş → 1
        Assert.Single(await svc.SearchAsync(new AnketFilter { CariId = cari, AnketTuru = SurveyType.Donus }));
    }

    [Fact]
    public async Task Anket_ve_cevaplari_tenant_izolasyonlu_silinince_cevaplar_da_gider()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        Guid id;
        using (var s1 = host.ScopeFor(t1))
        {
            var svc = s1.ServiceProvider.GetRequiredService<SurveyService>();
            id = await svc.CreateAsync(new AnketInput { Puan = 8, Cevaplar = Cevaplar(4) });
            Assert.Equal(4, (await svc.GetDetailAsync(id))!.Cevaplar.Count);
        }

        using (var s2 = host.ScopeFor(Guid.NewGuid()))
        {
            var svc2 = s2.ServiceProvider.GetRequiredService<SurveyService>();
            Assert.Empty(await svc2.SearchAsync());
            Assert.Null(await svc2.GetDetailAsync(id));      // cevaplar da sızmıyor
            Assert.False(await svc2.DeleteAsync(id));
        }

        // Silinince child cevaplar da gider (Cascade).
        using var s1b = host.ScopeFor(t1);
        var svc3 = s1b.ServiceProvider.GetRequiredService<SurveyService>();
        Assert.True(await svc3.DeleteAsync(id));
        Assert.Null(await svc3.GetDetailAsync(id));
    }
}
