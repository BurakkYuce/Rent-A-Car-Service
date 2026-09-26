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
    private static readonly DateTimeOffset Start =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(5).AddHours(9);

    private static PublicBookingRequestService Svc(IServiceScope s)
        => s.ServiceProvider.GetRequiredService<PublicBookingRequestService>();

    /// <summary>Anonim talep oluşturur (public yol — guard'sız) ve id'sini döner.</summary>
    private static async Task<Guid> RequestAsync(IServiceScope s, string name = "Ahmet Yılmaz",
        string tel = "0532 111 22 33")
    {
        var svc = Svc(s);
        await svc.CreateAsync(new PublicBookingRequestInput
        {
            AdSoyad = name, Telefon = tel, BasTar = Start, BitTar = Start.AddDays(3),
        });
        var (rows, _) = await svc.ListRequestsAsync(new TalepFiltre(Ara: tel));
        return rows.First(x => x.Talep.AdSoyad == name).Talep.Id;
    }

    private static async Task<Guid> VehicleAsync(IServiceScope s, string plate)
        => await s.ServiceProvider.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plate, Durum = VehicleStatus.Musait });

    // ---- DURUM KURALLARI (saf) ----

    [Fact]
    public void Terminal_ve_aktif_ayrimi_dogru()
    {
        Assert.True(TalepDurumu.Terminal(PublicBookingRequestDurum.Donustu));
        Assert.True(TalepDurumu.Terminal(PublicBookingRequestDurum.Reddedildi));
        Assert.True(TalepDurumu.Terminal(PublicBookingRequestDurum.Kayip));
        Assert.True(TalepDurumu.IsActive(PublicBookingRequestDurum.Yeni));
        Assert.True(TalepDurumu.IsActive(PublicBookingRequestDurum.Iletisimde));
        Assert.True(TalepDurumu.IsActive(PublicBookingRequestDurum.TeklifVerildi));
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
        var id = await RequestAsync(s);

        await svc.AssignStatusAsync(id, PublicBookingRequestDurum.Iletisimde);
        Assert.Equal(PublicBookingRequestDurum.Iletisimde, await StatusAsync(svc, id));

        await svc.AssignStatusAsync(id, PublicBookingRequestDurum.TeklifVerildi);
        Assert.Equal(PublicBookingRequestDurum.TeklifVerildi, await StatusAsync(svc, id));

        await svc.AssignStatusAsync(id, PublicBookingRequestDurum.Kayip);
        Assert.Equal(PublicBookingRequestDurum.Kayip, await StatusAsync(svc, id));
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
        var id = await RequestAsync(s);
        var vehicle = await VehicleAsync(s, "34 TD 01");

        await svc.AssignStatusAsync(id, PublicBookingRequestDurum.Iletisimde);
        await svc.AssignStatusAsync(id, PublicBookingRequestDurum.TeklifVerildi);

        var res = await svc.ConvertAsync(id, vehicle);   // eski yüklemde "zaten işlenmiş" derdi
        Assert.NotEqual(Guid.Empty, res);
        Assert.Equal(PublicBookingRequestDurum.Donustu, await StatusAsync(svc, id));
    }

    [Fact]
    public async Task Iletisimde_talep_REDDEDILEBILIR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = Svc(s);
        var id = await RequestAsync(s);

        await svc.AssignStatusAsync(id, PublicBookingRequestDurum.Iletisimde);
        await svc.RejectAsync(id);                     // aynı claim yolu
        Assert.Equal(PublicBookingRequestDurum.Reddedildi, await StatusAsync(svc, id));
    }

    [Fact]
    public async Task TERMINAL_durumdan_cikis_YOK()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = Svc(s);
        var vehicle = await VehicleAsync(s, "34 TD 02");

        // (a) Dönüşmüş talep: ortada gerçek rezervasyon var → kayıp/iletişimde işaretlenemez.
        var converted = await RequestAsync(s, "Dönüşen Müşteri", "0532 222 33 44");
        await svc.ConvertAsync(converted, vehicle);
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => svc.AssignStatusAsync(converted, PublicBookingRequestDurum.Kayip));
        // Mesaj terminal durumu söyler VE rezervasyona dönüştüğünü ekler (tek kontrol, iki bilgi).
        Assert.Contains("Dönüştü", ex.Message);
        Assert.Contains("Rezervasyona dönüşmüş", ex.Message);

        // (b) Reddedilmiş talep de kapalı.
        var red = await RequestAsync(s, "Reddedilen", "0532 333 44 55");
        await svc.RejectAsync(red);
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.AssignStatusAsync(red, PublicBookingRequestDurum.Iletisimde));
        await Assert.ThrowsAsync<ValidationException>(() => svc.RejectAsync(red));
    }

    [Fact]
    public async Task Donustu_durumu_ELLE_atanamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = Svc(s);
        var id = await RequestAsync(s);

        // Elle "Dönüştü" yazmak, rezervasyonu olmayan bir "dönüştü" satırı üretirdi.
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => svc.AssignStatusAsync(id, PublicBookingRequestDurum.Donustu));
        Assert.Contains("Dönüştür", ex.Message);
        Assert.Equal(PublicBookingRequestDurum.Yeni, await StatusAsync(svc, id));
    }

    // ---- NOTLAR ----

    [Fact]
    public async Task Notlar_eklenir_en_yeni_once_gelir_ve_SILINMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid(), userName: "umit");
        var svc = Svc(s);
        var id = await RequestAsync(s);

        await svc.AddNoteAsync(id, "Aradım, ulaşamadım.");
        await svc.AddNoteAsync(id, "Akşam tekrar arayacağım.");

        var notes = await svc.NotesAsync(id);
        Assert.Equal(2, notes.Count);
        Assert.Equal("Akşam tekrar arayacağım.", notes[0].Metin);   // en yeni önce
        Assert.Equal("umit", notes[0].Kullanici);

        // Silme yüzeyi YOK — takip geçmişi kanıt (servis metodu sunmuyor).
        Assert.DoesNotContain("NotSil", typeof(PublicBookingRequestService)
            .GetMethods().Select(m => m.Name));

        // Liste satırı not sayısını taşır (N+1'siz).
        var (rows, _) = await svc.ListRequestsAsync(new TalepFiltre());
        Assert.Equal(2, rows.Single(x => x.Talep.Id == id).NotSayisi);
    }

    [Fact]
    public async Task Bos_ve_asiri_uzun_not_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = Svc(s);
        var id = await RequestAsync(s);

        await Assert.ThrowsAsync<ValidationException>(() => svc.AddNoteAsync(id, "   "));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.AddNoteAsync(id, new string('x', PublicBookingRequestService.MaxNot + 1)));
        await Assert.ThrowsAsync<ValidationException>(() => svc.AddNoteAsync(Guid.NewGuid(), "yok"));
        Assert.Empty(await svc.NotesAsync(id));
    }

    // ---- ÜSTLENME ----

    [Fact]
    public async Task Ustlenme_kendine_yapilir_ve_birakilabilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        Guid id;
        using (var s = host.ScopeFor(tenantId, userId: userId, userName: "umit"))
        {
            var svc = Svc(s);
            id = await RequestAsync(s);
            await svc.ClaimAsync(id, claim: true);

            var (rows, _) = await svc.ListRequestsAsync(new TalepFiltre());
            var t = rows.Single(x => x.Talep.Id == id).Talep;
            Assert.Equal("umit", t.AtananAd);           // ad DENORMALİZE (Users'a join yok)
            Assert.Equal(userId, t.AtananKullaniciId);
        }
        using (var s = host.ScopeFor(tenantId, userId: userId, userName: "umit"))
        {
            var svc = Svc(s);
            await svc.ClaimAsync(id, claim: false);
            var (rows, _) = await svc.ListRequestsAsync(new TalepFiltre());
            Assert.Null(rows.Single(x => x.Talep.Id == id).Talep.AtananAd);
        }
    }

    // ---- FİLTRE / SAYFALAMA / SAYAÇ ----

    [Fact]
    public async Task Filtre_durum_ve_arama_ile_daraltir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = Svc(s);
        var a = await RequestAsync(s, "Ahmet Yılmaz", "0532 111 11 11");
        await RequestAsync(s, "Mehmet Demir", "0533 222 22 22");
        await svc.AssignStatusAsync(a, PublicBookingRequestDurum.Iletisimde);

        var (newItem, newTotal) = await svc.ListRequestsAsync(new TalepFiltre(PublicBookingRequestDurum.Yeni));
        Assert.Equal(1, newTotal);
        Assert.Equal("Mehmet Demir", newItem[0].Talep.AdSoyad);

        var (contact, _) = await svc.ListRequestsAsync(new TalepFiltre(PublicBookingRequestDurum.Iletisimde));
        Assert.Equal("Ahmet Yılmaz", Assert.Single(contact).Talep.AdSoyad);

        // Ada göre arama Türkçe-duyarsız (ILIKE) ve telefona göre de çalışıyor.
        Assert.Single((await svc.ListRequestsAsync(new TalepFiltre(Ara: "ahmet"))).Satirlar);
        Assert.Single((await svc.ListRequestsAsync(new TalepFiltre(Ara: "0533"))).Satirlar);
        Assert.Empty((await svc.ListRequestsAsync(new TalepFiltre(Ara: "bulunmayan"))).Satirlar);
    }

    [Fact]
    public async Task Sayfalama_toplami_dogru_ve_sayfa_boyutu_kelepcelenir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = Svc(s);
        for (var i = 1; i <= 7; i++) await RequestAsync(s, $"Müşteri {i}", $"0532 000 00 {i:00}");

        var (first, total) = await svc.ListRequestsAsync(new TalepFiltre(Sayfa: 1, Boyut: 3));
        Assert.Equal(7, total);
        Assert.Equal(3, first.Count);

        var (third, _) = await svc.ListRequestsAsync(new TalepFiltre(Sayfa: 3, Boyut: 3));
        Assert.Single(third);

        // Boyut kelepçesi: 0/negatif istek tüm tabloyu çekmeye dönüşmemeli.
        Assert.Single((await svc.ListRequestsAsync(new TalepFiltre(Boyut: 0))).Satirlar);
        Assert.Equal(7, (await svc.ListRequestsAsync(new TalepFiltre(Boyut: 999))).Satirlar.Count);
    }

    [Fact]
    public async Task Ozet_yeni_sayisini_ve_en_eski_bekleyeni_verir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = Svc(s);

        Assert.Equal(new TalepOzet(0, null), await svc.SummaryAsync());   // boşta sıfır

        var a = await RequestAsync(s, "Bir", "0532 111 11 11");
        await RequestAsync(s, "İki", "0532 222 22 22");
        var summary = await svc.SummaryAsync();
        Assert.Equal(2, summary.Yeni);
        Assert.Equal(0, summary.EnEskiGun);        // bugün oluştu

        // İşlenen talep sayaçtan düşer (kuyruk "cevaplanmamış"ı gösteriyor).
        await svc.AssignStatusAsync(a, PublicBookingRequestDurum.Iletisimde);
        Assert.Equal(1, (await svc.SummaryAsync()).Yeni);
    }

    [Fact]
    public async Task Kapanan_talepte_yaslanma_SIFIR_gosterilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = Svc(s);
        var id = await RequestAsync(s);
        await svc.RejectAsync(id);

        // Kapanmış lead'in "12 gündür bekliyor" yazması yanıltıcı olurdu.
        var (rows, _) = await svc.ListRequestsAsync(new TalepFiltre());
        Assert.Equal(0, rows.Single(x => x.Talep.Id == id).BekleyenGun);
    }

    // ---- YETKİ + İZOLASYON ----

    [Fact]
    public async Task Operasyon_yetkisi_olmayan_rol_TALEP_YONETEMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        Guid id;
        using (var s = host.ScopeFor(tenantId)) id = await RequestAsync(s);

        using var m = host.ScopeFor(tenantId, role: UserRole.Muhasebe);
        var svc = Svc(m);
        await Assert.ThrowsAsync<NoPermissionException>(() => svc.ListRequestsAsync(new TalepFiltre()));
        await Assert.ThrowsAsync<NoPermissionException>(() => svc.SummaryAsync());
        await Assert.ThrowsAsync<NoPermissionException>(() => svc.AddNoteAsync(id, "x"));
        await Assert.ThrowsAsync<NoPermissionException>(() => svc.ClaimAsync(id, true));
        await Assert.ThrowsAsync<NoPermissionException>(
            () => svc.AssignStatusAsync(id, PublicBookingRequestDurum.Iletisimde));
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
            id = await RequestAsync(s, "T1 Müşterisi", "0532 111 11 11");
            await Svc(s).AddNoteAsync(id, "T1 notu");
        }

        using var s2 = host.ScopeFor(t2);
        var svc = Svc(s2);
        Assert.Empty((await svc.ListRequestsAsync(new TalepFiltre())).Satirlar);
        Assert.Equal(0, (await svc.SummaryAsync()).Yeni);
        Assert.Empty(await svc.NotesAsync(id));                       // yabancı notlar görünmez
        // Id bilinse bile durum değiştirilemez (satır bu tenant için YOK).
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.AssignStatusAsync(id, PublicBookingRequestDurum.Iletisimde));
    }

    private static async Task<PublicBookingRequestDurum> StatusAsync(
        PublicBookingRequestService svc, Guid id)
    {
        var (rows, _) = await svc.ListRequestsAsync(new TalepFiltre(Boyut: 200));
        return rows.Single(x => x.Talep.Id == id).Talep.Durum;
    }
}
