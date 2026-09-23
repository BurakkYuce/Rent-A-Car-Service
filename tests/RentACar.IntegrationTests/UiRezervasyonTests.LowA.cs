using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// "Low temizliği A" kalıcı çitleri: KVKK <c>AnonimAd</c> kira listesi / Panel / <c>secim/musteri*</c> (görüntü +
/// arama), teklif kabul tekrarında 409 <c>mevcut</c> rezervasyon (#271 L3), kirada ya da filo sözleşmesinde kullanılan
/// aracın silinememesi (#271 L2). ORACLE: etiket metni elle yazılmış sabit ("Anonim müşteri"); üretim sabiti kullanılmaz.
/// </summary>
public sealed partial class UiRezervasyonTests
{
    private const string AnonimEtiket = "Anonim müşteri";

    private async Task<(Customer Anonim, Customer Acik, string Soyad)> AnonimCarilerAsync(Ortam o)
    {
        var soyad = "Saklı" + Guid.NewGuid().ToString("N")[..6];
        var anonim = new Customer { Tip = CariType.Bireysel, Ad = "Zeynep", Soyad = soyad, CepTel = "05320000011", AnonimAd = true };
        var acik = new Customer { Tip = CariType.Bireysel, Ad = "Zeynep", Soyad = soyad + "x", CepTel = "05320000012" };
        await VeriYazAsync(o.TenantId, db => db.Customers.AddRange(anonim, acik));
        return (anonim, acik, soyad);
    }

    [Fact]
    public async Task LowA_AnonimAd_kira_listesi_ve_panel_maskeler_gercek_adla_aranamaz()
    {
        var o = await OrtamKurAsync();
        var (anonim, _, soyad) = await AnonimCarilerAsync(o);
        var s = await GirisAsync(o, Kim.Admin);

        // Kira: bugün başlar, yarın biter (Panel "yarın dönüş" kovası) — anonim cari.
        var g = RezGovde(o, await AracAsync(o), DateTimeOffset.UtcNow.AddMinutes(30)); g["musteriId"] = anonim.Id;
        g["bitTar"] = Yarin(1);
        var rezId = (await Json(await Gonder(s, HttpMethod.Post, Rez, g), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await Json(await Gonder(s, HttpMethod.Post, $"{Rez}/{rezId}/kiraya-cevir"));
        // Açık rezervasyon: yarın çıkış (Panel "yarın çıkış" kovası) — aynı anonim cari.
        var g2 = RezGovde(o, await AracAsync(o), Yarin(1), 1); g2["musteriId"] = anonim.Id;
        await Json(await Gonder(s, HttpMethod.Post, Rez, g2), HttpStatusCode.Created);

        var liste = await Json(await s.C.GetAsync($"{V1}/kiralar"));
        var satir = Assert.Single(liste.GetProperty("kayitlar").EnumerateArray());
        Assert.Equal(AnonimEtiket, satir.GetProperty("musteriAd").GetString());
        var sozlesmeNo = satir.GetProperty("sozlesmeNo").GetString();
        // Gerçek soyadla arama boş; sözleşme numarasıyla bulunur; müşteri sıralaması da çalışır (etiketle).
        Assert.Equal(0, (await Json(await s.C.GetAsync($"{V1}/kiralar?q={soyad}"))).GetProperty("toplam").GetInt32());
        Assert.Equal(1, (await Json(await s.C.GetAsync($"{V1}/kiralar?q={sozlesmeNo}"))).GetProperty("toplam").GetInt32());
        Assert.Equal(AnonimEtiket, (await Json(await s.C.GetAsync($"{V1}/kiralar?sirala=musteri")))
            .GetProperty("kayitlar")[0].GetProperty("musteriAd").GetString());

        var panelMetni = await (await s.C.GetAsync($"{V1}/panel/ozet")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(soyad, panelMetni);
        var panel = JsonDocument.Parse(panelMetni).RootElement;
        Assert.Equal(AnonimEtiket, Assert.Single(panel.GetProperty("donusler").GetProperty("yarin").EnumerateArray())
            .GetProperty("musteriAd").GetString());
        Assert.Equal(AnonimEtiket, Assert.Single(panel.GetProperty("cikislar").GetProperty("yarin").EnumerateArray())
            .GetProperty("musteriAd").GetString());
    }

    [Fact]
    public async Task LowA_AnonimAd_secim_musteri_etiket_ve_arama()
    {
        var o = await OrtamKurAsync();
        var (anonim, acik, soyad) = await AnonimCarilerAsync(o);
        foreach (var kim in new[] { Kim.OperatorA, Kim.Muhasebe })
        {
            var s = await GirisAsync(o, kim);
            // Gerçek soyad: yalnız anonim OLMAYAN cari (soyad+"x") döner.
            var bul = await Json(await s.C.GetAsync($"{V1}/secim/musteri?q={soyad}"));
            var tek = Assert.Single(bul.EnumerateArray());
            Assert.Equal(acik.Id, tek.GetProperty("id").GetGuid());
            // Görünen etiketle ("anonim", Türkçe katlamalı) anonim cari bulunur ve etiketle döner.
            var etiketle = await Json(await s.C.GetAsync($"{V1}/secim/musteri?q=ANONİM"));
            var a = Assert.Single(etiketle.EnumerateArray(), x => x.GetProperty("id").GetGuid() == anonim.Id);
            Assert.Equal(AnonimEtiket, a.GetProperty("etiket").GetString());
            Assert.DoesNotContain(soyad, etiketle.GetRawText());
        }
        var op = await GirisAsync(o, Kim.OperatorA);
        var tekil = await Json(await op.C.GetAsync($"{V1}/secim/musteri/{anonim.Id}"));
        Assert.Equal(AnonimEtiket, tekil.GetProperty("etiket").GetString());
        Assert.Equal("Zeynep " + soyad + "x", (await Json(await op.C.GetAsync($"{V1}/secim/musteri/{acik.Id}")))
            .GetProperty("etiket").GetString());
    }

    /// <summary>#280 KVKK L-1: selection order follows the displayed name — two anonymised customers whose real
    /// names sort around an open one ("Aaaahmet" &lt; "Kkkenan" &lt; "Zzzzafer") must end up next to each other.</summary>
    [Fact]
    public async Task LowA_secim_musteri_siralamasi_anonimin_gercek_adini_sizdirmaz()
    {
        var o = await OrtamKurAsync();
        var anonimA = new Customer { Tip = CariType.Bireysel, Ad = "Aaaahmet", Soyad = "Test", AnonimAd = true };
        var acik = new Customer { Tip = CariType.Bireysel, Ad = "Kkkenan", Soyad = "Test" };
        var anonimZ = new Customer { Tip = CariType.Bireysel, Ad = "Zzzzafer", Soyad = "Test", AnonimAd = true };
        await VeriYazAsync(o.TenantId, db => db.Customers.AddRange(anonimA, acik, anonimZ));
        var s = await GirisAsync(o, Kim.OperatorA);

        var ids = (await Json(await s.C.GetAsync($"{V1}/secim/musteri?q=n"))).EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();

        Assert.Equal(3, ids.Count); // "Ece Kaya" (ortam carisi) "n" içermez
        Assert.Contains(acik.Id, ids);
        var a = ids.IndexOf(anonimA.Id);
        var z = ids.IndexOf(anonimZ.Id);
        Assert.True(a >= 0 && z >= 0, "anonim cariler etiketle bulunmalı");
        Assert.Equal(1, Math.Abs(a - z)); // yan yana: konumları gerçek addan bağımsız
    }

    [Fact]
    public async Task LowA_teklif_kabul_tekrari_409_mevcut_rezervasyonu_soyler()
    {
        var o = await OrtamKurAsync();
        var a = await GirisAsync(o, Kim.OperatorA);
        var bas = Yarin(2);
        var id = (await Json(await Gonder(a, HttpMethod.Post, Teklif, new
        {
            musteriId = o.MusteriId, vehicleId = await AracAsync(o), basTar = bas, bitTar = bas.AddDays(2),
            gunlukUcret = 150m, fiyatTuru = "KDV Dahil Günlük", cikisOfisi = "SubeA",
        }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var kabul = await Json(await Gonder(a, HttpMethod.Post, $"{Teklif}/{id}/kabul"));

        var p = await ProblemBekle(await Gonder(a, HttpMethod.Post, $"{Teklif}/{id}/kabul"), HttpStatusCode.Conflict, "cakisma");
        var m = p.GetProperty("mevcut");
        Assert.Equal(kabul.GetProperty("rezervasyonId").GetGuid(), m.GetProperty("rezervasyonId").GetGuid());
        Assert.Equal(kabul.GetProperty("rezervasyonNo").GetString(), m.GetProperty("rezervasyonNo").GetString());
        Assert.False(string.IsNullOrEmpty(m.GetProperty("rezervasyonNo").GetString()));
        Assert.Equal(1, (await Json(await a.C.GetAsync(Rez))).GetProperty("toplam").GetInt32()); // ikinci rez YOK
    }

    [Fact]
    public async Task LowA_kirada_ya_da_filo_sozlesmesinde_kullanilan_arac_silinemez()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Admin);
        var kiraAraci = await AracAsync(o);
        var rezId = await RezAcAsync(s, o, kiraAraci, Yarin(2));
        await Json(await Gonder(s, HttpMethod.Post, $"{Rez}/{rezId}/kiraya-cevir"));
        var filoAraci = await AracAsync(o);
        await Json(await Gonder(s, HttpMethod.Post, Filo,
            new { musteriId = o.MusteriId, vehicleId = filoAraci, sureAy = 3, aylikUcret = 1000m }), HttpStatusCode.Created);
        var bosArac = await AracAsync(o);

        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(o.TenantId, role: UserRole.Admin);
        var araclar = scope.ServiceProvider.GetRequiredService<VehicleService>();
        var e1 = await Assert.ThrowsAsync<ValidationException>(() => araclar.DeleteAsync(kiraAraci));
        Assert.Equal("arac", e1.Alan);
        Assert.Contains("kira sözleşmesinde", e1.Message);
        var e2 = await Assert.ThrowsAsync<ValidationException>(() => araclar.DeleteAsync(filoAraci));
        Assert.Equal("arac", e2.Alan);
        Assert.Contains("filo kiralama", e2.Message);
        Assert.True(await araclar.DeleteAsync(bosArac)); // kullanılmayan araç silinir (guard aşırı değil)

        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        Assert.Equal(2, await db.Vehicles.CountAsync(v => v.Id == kiraAraci || v.Id == filoAraci || v.Id == bosArac));
    }
}
