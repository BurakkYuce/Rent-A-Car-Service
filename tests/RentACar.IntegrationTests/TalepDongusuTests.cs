using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.PublicSite;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-17 — siteden gelen talebin (lead) yaşam döngüsü: ara durumlar, takip notları, üstlenme,
/// yaşlanma, filtre/sayfalama.
///
/// <para><b>EN KRİTİK TEST:</b> <c>Iletisimde_talep_HALA_donusturulebilir</c>. Ara durum eklemek,
/// atomik claim yüklemini (<c>WHERE Durum = Yeni</c>) sessizce bozuyordu: personel "İletişimde"
/// işaretlediği anda lead dönüştürülemez hale geliyordu ("Bu talep zaten işlenmiş"). Kural artık
/// <see cref="TalepDurumu.Terminal"/> ile tek yerden geliyor ve bu test onu kilitliyor.</para>
///
/// Bağımsız oracle: beklenenler senaryodan kurulur ("dönüştürdüm → artık kayıp işaretlenemez"),
/// servisin yükleminden türetilmez.
/// </summary>
[Collection("postgres")]
public sealed class TalepDongusuTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(5).AddHours(9);

    private static PublicBookingRequestService Svc(IServiceScope s)
        => s.ServiceProvider.GetRequiredService<PublicBookingRequestService>();

    /// <summary>Anonim talep oluşturur (public yol — guard'sız) ve id'sini döner.</summary>
    private static async Task<Guid> TalepAsync(IServiceScope s, string ad = "Ahmet Yılmaz",
        string tel = "0532 111 22 33")
    {
        var svc = Svc(s);
        await svc.CreateAsync(new PublicBookingRequestInput
        {
            AdSoyad = ad, Telefon = tel, BasTar = Bas, BitTar = Bas.AddDays(3),
        });
        var (satirlar, _) = await svc.ListeleAsync(new TalepFiltre(Ara: tel));
        return satirlar.First(x => x.Talep.AdSoyad == ad).Talep.Id;
    }

    private static async Task<Guid> AracAsync(IServiceScope s, string plaka)
        => await s.ServiceProvider.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plaka, Durum = VehicleStatus.Musait });

    // ---- DURUM KURALLARI (saf) ----

    [Fact]
    public void Terminal_ve_aktif_ayrimi_dogru()
    {
        Assert.True(TalepDurumu.Terminal(PublicBookingRequestDurum.Donustu));
        Assert.True(TalepDurumu.Terminal(PublicBookingRequestDurum.Reddedildi));
        Assert.True(TalepDurumu.Terminal(PublicBookingRequestDurum.Kayip));
        Assert.True(TalepDurumu.Aktif(PublicBookingRequestDurum.Yeni));
        Assert.True(TalepDurumu.Aktif(PublicBookingRequestDurum.Iletisimde));
        Assert.True(TalepDurumu.Aktif(PublicBookingRequestDurum.TeklifVerildi));
    }

    /// <summary>
    /// Enum numaraları APPEND-ONLY olmalı: kolon int saklanıyor, araya değer sokmak mevcut satırların
    /// anlamını kaydırır. Bu test sayıları çiviliyor — biri "düzgün sıralasın" diye yeniden
    /// numaralandırırsa canlı veri sessizce bozulacağı için kırmızıya döner.
    /// </summary>
    [Fact]
    public void Enum_numaralari_APPEND_ONLY_cakili()
    {
        Assert.Equal(0, (int)PublicBookingRequestDurum.Yeni);
        Assert.Equal(1, (int)PublicBookingRequestDurum.Donustu);
        Assert.Equal(2, (int)PublicBookingRequestDurum.Reddedildi);
        Assert.Equal(3, (int)PublicBookingRequestDurum.Iletisimde);
        Assert.Equal(4, (int)PublicBookingRequestDurum.TeklifVerildi);
        Assert.Equal(5, (int)PublicBookingRequestDurum.Kayip);
        // Gösterim sırası numaralardan BAĞIMSIZ ve TÜM değerleri kapsıyor.
        Assert.Equal(Enum.GetValues<PublicBookingRequestDurum>().Length, TalepDurumu.GosterimSirasi.Length);
        Assert.Equal(PublicBookingRequestDurum.Iletisimde, TalepDurumu.GosterimSirasi[1]);
    }

    // ---- YAŞAM DÖNGÜSÜ ----

    [Fact]
    public async Task Yeni_Iletisimde_Teklif_ilerlemesi_calisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = Svc(s);
        var id = await TalepAsync(s);

        await svc.DurumAtaAsync(id, PublicBookingRequestDurum.Iletisimde);
        Assert.Equal(PublicBookingRequestDurum.Iletisimde, await DurumAsync(svc, id));

        await svc.DurumAtaAsync(id, PublicBookingRequestDurum.TeklifVerildi);
        Assert.Equal(PublicBookingRequestDurum.TeklifVerildi, await DurumAsync(svc, id));

        await svc.DurumAtaAsync(id, PublicBookingRequestDurum.Kayip);
        Assert.Equal(PublicBookingRequestDurum.Kayip, await DurumAsync(svc, id));
    }

    /// <summary>
    /// BU PR'IN EN KRİTİK DAVRANIŞI. Ara durum eklemek claim yüklemini bozuyordu; "İletişimde"
    /// işaretlenen lead dönüştürülemez hale geliyordu.
    /// </summary>
    [Fact]
    public async Task Iletisimde_talep_HALA_donusturulebilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = Svc(s);
        var id = await TalepAsync(s);
        var arac = await AracAsync(s, "34 TD 01");

        await svc.DurumAtaAsync(id, PublicBookingRequestDurum.Iletisimde);
        await svc.DurumAtaAsync(id, PublicBookingRequestDurum.TeklifVerildi);

        var rez = await svc.DonusturAsync(id, arac);   // eski yüklemde "zaten işlenmiş" derdi
        Assert.NotEqual(Guid.Empty, rez);
        Assert.Equal(PublicBookingRequestDurum.Donustu, await DurumAsync(svc, id));
    }

    [Fact]
    public async Task Iletisimde_talep_REDDEDILEBILIR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = Svc(s);
        var id = await TalepAsync(s);

        await svc.DurumAtaAsync(id, PublicBookingRequestDurum.Iletisimde);
        await svc.ReddetAsync(id);                     // aynı claim yolu
        Assert.Equal(PublicBookingRequestDurum.Reddedildi, await DurumAsync(svc, id));
    }

    [Fact]
    public async Task TERMINAL_durumdan_cikis_YOK()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = Svc(s);
        var arac = await AracAsync(s, "34 TD 02");

        // (a) Dönüşmüş talep: ortada gerçek rezervasyon var → kayıp/iletişimde işaretlenemez.
        var donusen = await TalepAsync(s, "Dönüşen Müşteri", "0532 222 33 44");
        await svc.DonusturAsync(donusen, arac);
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => svc.DurumAtaAsync(donusen, PublicBookingRequestDurum.Kayip));
        // Mesaj terminal durumu söyler VE rezervasyona dönüştüğünü ekler (tek kontrol, iki bilgi).
        Assert.Contains("Dönüştü", ex.Message);
        Assert.Contains("Rezervasyona dönüşmüş", ex.Message);

        // (b) Reddedilmiş talep de kapalı.
        var red = await TalepAsync(s, "Reddedilen", "0532 333 44 55");
        await svc.ReddetAsync(red);
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.DurumAtaAsync(red, PublicBookingRequestDurum.Iletisimde));
        await Assert.ThrowsAsync<ValidationException>(() => svc.ReddetAsync(red));
    }

    [Fact]
    public async Task Donustu_durumu_ELLE_atanamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = Svc(s);
        var id = await TalepAsync(s);

        // Elle "Dönüştü" yazmak, rezervasyonu olmayan bir "dönüştü" satırı üretirdi.
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => svc.DurumAtaAsync(id, PublicBookingRequestDurum.Donustu));
        Assert.Contains("Dönüştür", ex.Message);
        Assert.Equal(PublicBookingRequestDurum.Yeni, await DurumAsync(svc, id));
    }

    // ---- NOTLAR ----

    [Fact]
    public async Task Notlar_eklenir_en_yeni_once_gelir_ve_SILINMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid(), userName: "umit");
        var svc = Svc(s);
        var id = await TalepAsync(s);

        await svc.NotEkleAsync(id, "Aradım, ulaşamadım.");
        await svc.NotEkleAsync(id, "Akşam tekrar arayacağım.");

        var notlar = await svc.NotlarAsync(id);
        Assert.Equal(2, notlar.Count);
        Assert.Equal("Akşam tekrar arayacağım.", notlar[0].Metin);   // en yeni önce
        Assert.Equal("umit", notlar[0].Kullanici);

        // Silme yüzeyi YOK — takip geçmişi kanıt (servis metodu sunmuyor).
        Assert.DoesNotContain("NotSil", typeof(PublicBookingRequestService)
            .GetMethods().Select(m => m.Name));

        // Liste satırı not sayısını taşır (N+1'siz).
        var (satirlar, _) = await svc.ListeleAsync(new TalepFiltre());
        Assert.Equal(2, satirlar.Single(x => x.Talep.Id == id).NotSayisi);
    }

    [Fact]
    public async Task Bos_ve_asiri_uzun_not_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = Svc(s);
        var id = await TalepAsync(s);

        await Assert.ThrowsAsync<ValidationException>(() => svc.NotEkleAsync(id, "   "));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.NotEkleAsync(id, new string('x', PublicBookingRequestService.MaxNot + 1)));
        await Assert.ThrowsAsync<ValidationException>(() => svc.NotEkleAsync(Guid.NewGuid(), "yok"));
        Assert.Empty(await svc.NotlarAsync(id));
    }

    // ---- ÜSTLENME ----

    [Fact]
    public async Task Ustlenme_kendine_yapilir_ve_birakilabilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var kullaniciId = Guid.NewGuid();
        Guid id;
        using (var s = host.ScopeFor(tenantId, userId: kullaniciId, userName: "umit"))
        {
            var svc = Svc(s);
            id = await TalepAsync(s);
            await svc.UstlenAsync(id, ustlen: true);

            var (satirlar, _) = await svc.ListeleAsync(new TalepFiltre());
            var t = satirlar.Single(x => x.Talep.Id == id).Talep;
            Assert.Equal("umit", t.AtananAd);           // ad DENORMALİZE (Users'a join yok)
            Assert.Equal(kullaniciId, t.AtananKullaniciId);
        }
        using (var s = host.ScopeFor(tenantId, userId: kullaniciId, userName: "umit"))
        {
            var svc = Svc(s);
            await svc.UstlenAsync(id, ustlen: false);
            var (satirlar, _) = await svc.ListeleAsync(new TalepFiltre());
            Assert.Null(satirlar.Single(x => x.Talep.Id == id).Talep.AtananAd);
        }
    }

    // ---- FİLTRE / SAYFALAMA / SAYAÇ ----

    [Fact]
    public async Task Filtre_durum_ve_arama_ile_daraltir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = Svc(s);
        var a = await TalepAsync(s, "Ahmet Yılmaz", "0532 111 11 11");
        await TalepAsync(s, "Mehmet Demir", "0533 222 22 22");
        await svc.DurumAtaAsync(a, PublicBookingRequestDurum.Iletisimde);

        var (yeni, yeniToplam) = await svc.ListeleAsync(new TalepFiltre(PublicBookingRequestDurum.Yeni));
        Assert.Equal(1, yeniToplam);
        Assert.Equal("Mehmet Demir", yeni[0].Talep.AdSoyad);

        var (iletisim, _) = await svc.ListeleAsync(new TalepFiltre(PublicBookingRequestDurum.Iletisimde));
        Assert.Equal("Ahmet Yılmaz", Assert.Single(iletisim).Talep.AdSoyad);

        // Ada göre arama Türkçe-duyarsız (ILIKE) ve telefona göre de çalışıyor.
        Assert.Single((await svc.ListeleAsync(new TalepFiltre(Ara: "ahmet"))).Satirlar);
        Assert.Single((await svc.ListeleAsync(new TalepFiltre(Ara: "0533"))).Satirlar);
        Assert.Empty((await svc.ListeleAsync(new TalepFiltre(Ara: "bulunmayan"))).Satirlar);
    }

    [Fact]
    public async Task Sayfalama_toplami_dogru_ve_sayfa_boyutu_kelepcelenir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = Svc(s);
        for (var i = 1; i <= 7; i++) await TalepAsync(s, $"Müşteri {i}", $"0532 000 00 {i:00}");

        var (ilk, toplam) = await svc.ListeleAsync(new TalepFiltre(Sayfa: 1, Boyut: 3));
        Assert.Equal(7, toplam);
        Assert.Equal(3, ilk.Count);

        var (ucuncu, _) = await svc.ListeleAsync(new TalepFiltre(Sayfa: 3, Boyut: 3));
        Assert.Single(ucuncu);

        // Boyut kelepçesi: 0/negatif istek tüm tabloyu çekmeye dönüşmemeli.
        Assert.Single((await svc.ListeleAsync(new TalepFiltre(Boyut: 0))).Satirlar);
        Assert.Equal(7, (await svc.ListeleAsync(new TalepFiltre(Boyut: 999))).Satirlar.Count);
    }

    [Fact]
    public async Task Ozet_yeni_sayisini_ve_en_eski_bekleyeni_verir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = Svc(s);

        Assert.Equal(new TalepOzet(0, null), await svc.OzetAsync());   // boşta sıfır

        var a = await TalepAsync(s, "Bir", "0532 111 11 11");
        await TalepAsync(s, "İki", "0532 222 22 22");
        var ozet = await svc.OzetAsync();
        Assert.Equal(2, ozet.Yeni);
        Assert.Equal(0, ozet.EnEskiGun);        // bugün oluştu

        // İşlenen talep sayaçtan düşer (kuyruk "cevaplanmamış"ı gösteriyor).
        await svc.DurumAtaAsync(a, PublicBookingRequestDurum.Iletisimde);
        Assert.Equal(1, (await svc.OzetAsync()).Yeni);
    }

    [Fact]
    public async Task Kapanan_talepte_yaslanma_SIFIR_gosterilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = Svc(s);
        var id = await TalepAsync(s);
        await svc.ReddetAsync(id);

        // Kapanmış lead'in "12 gündür bekliyor" yazması yanıltıcı olurdu.
        var (satirlar, _) = await svc.ListeleAsync(new TalepFiltre());
        Assert.Equal(0, satirlar.Single(x => x.Talep.Id == id).BekleyenGun);
    }

    // ---- YETKİ + İZOLASYON ----

    [Fact]
    public async Task Operasyon_yetkisi_olmayan_rol_TALEP_YONETEMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        Guid id;
        using (var s = host.ScopeFor(tenantId)) id = await TalepAsync(s);

        using var m = host.ScopeFor(tenantId, role: UserRole.Muhasebe);
        var svc = Svc(m);
        await Assert.ThrowsAsync<ValidationException>(() => svc.ListeleAsync(new TalepFiltre()));
        await Assert.ThrowsAsync<ValidationException>(() => svc.OzetAsync());
        await Assert.ThrowsAsync<ValidationException>(() => svc.NotEkleAsync(id, "x"));
        await Assert.ThrowsAsync<ValidationException>(() => svc.UstlenAsync(id, true));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.DurumAtaAsync(id, PublicBookingRequestDurum.Iletisimde));
    }

    [Fact]
    public async Task Baska_tenantin_talebi_ve_notu_gorunmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        Guid id;
        using (var s = host.ScopeFor(t1))
        {
            id = await TalepAsync(s, "T1 Müşterisi", "0532 111 11 11");
            await Svc(s).NotEkleAsync(id, "T1 notu");
        }

        using var s2 = host.ScopeFor(t2);
        var svc = Svc(s2);
        Assert.Empty((await svc.ListeleAsync(new TalepFiltre())).Satirlar);
        Assert.Equal(0, (await svc.OzetAsync()).Yeni);
        Assert.Empty(await svc.NotlarAsync(id));                       // yabancı notlar görünmez
        // Id bilinse bile durum değiştirilemez (satır bu tenant için YOK).
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.DurumAtaAsync(id, PublicBookingRequestDurum.Iletisimde));
    }

    private static async Task<PublicBookingRequestDurum> DurumAsync(
        PublicBookingRequestService svc, Guid id)
    {
        var (satirlar, _) = await svc.ListeleAsync(new TalepFiltre(Boyut: 200));
        return satirlar.Single(x => x.Talep.Id == id).Talep.Durum;
    }
}
