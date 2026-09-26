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
    private const string AnonymousLabel = "Anonim müşteri";

    private async Task<(Customer Anonim, Customer Acik, string Soyad)> AnonymousAccountsAsync(Ortam o)
    {
        var soyad = "Saklı" + Guid.NewGuid().ToString("N")[..6];
        var anonymous = new Customer { Tip = CustomerType.Bireysel, Ad = "Zeynep", Soyad = soyad, CepTel = "05320000011", AnonimAd = true };
        var open = new Customer { Tip = CustomerType.Bireysel, Ad = "Zeynep", Soyad = soyad + "x", CepTel = "05320000012" };
        await WriteDataAsync(o.TenantId, db => db.Customers.AddRange(anonymous, open));
        return (anonymous, open, soyad);
    }

    [Fact]
    public async Task LowA_AnonimAd_kira_listesi_ve_panel_maskeler_gercek_adla_aranamaz()
    {
        var o = await SetUpEnvironmentAsync();
        var (anonymous, _, soyad) = await AnonymousAccountsAsync(o);
        var s = await LoginAsync(o, Kim.Admin);

        // Kira: bugün başlar, yarın biter (Panel "yarın dönüş" kovası) — anonim cari.
        var g = ReservationBody(o, await VehicleAsync(o), DateTimeOffset.UtcNow.AddMinutes(30)); g["musteriId"] = anonymous.Id;
        g["bitTar"] = Tomorrow(1);
        var resId = (await Json(await Gonder(s, HttpMethod.Post, TestReservation, g), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await Json(await Gonder(s, HttpMethod.Post, $"{TestReservation}/{resId}/kiraya-cevir"));
        // Açık rezervasyon: yarın çıkış (Panel "yarın çıkış" kovası) — aynı anonim cari.
        var g2 = ReservationBody(o, await VehicleAsync(o), Tomorrow(1), 1); g2["musteriId"] = anonymous.Id;
        await Json(await Gonder(s, HttpMethod.Post, TestReservation, g2), HttpStatusCode.Created);

        var list = await Json(await s.C.GetAsync($"{V1}/kiralar"));
        var row = Assert.Single(list.GetProperty("kayitlar").EnumerateArray());
        Assert.Equal(AnonymousLabel, row.GetProperty("musteriAd").GetString());
        var contractNo = row.GetProperty("sozlesmeNo").GetString();
        // Gerçek soyadla arama boş; sözleşme numarasıyla bulunur; müşteri sıralaması da çalışır (etiketle).
        Assert.Equal(0, (await Json(await s.C.GetAsync($"{V1}/kiralar?q={soyad}"))).GetProperty("toplam").GetInt32());
        Assert.Equal(1, (await Json(await s.C.GetAsync($"{V1}/kiralar?q={contractNo}"))).GetProperty("toplam").GetInt32());
        Assert.Equal(AnonymousLabel, (await Json(await s.C.GetAsync($"{V1}/kiralar?sirala=musteri")))
            .GetProperty("kayitlar")[0].GetProperty("musteriAd").GetString());

        var panelText = await (await s.C.GetAsync($"{V1}/panel/ozet")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(soyad, panelText);
        var panel = JsonDocument.Parse(panelText).RootElement;
        Assert.Equal(AnonymousLabel, Assert.Single(panel.GetProperty("donusler").GetProperty("yarin").EnumerateArray())
            .GetProperty("musteriAd").GetString());
        Assert.Equal(AnonymousLabel, Assert.Single(panel.GetProperty("cikislar").GetProperty("yarin").EnumerateArray())
            .GetProperty("musteriAd").GetString());
    }

    [Fact]
    public async Task LowA_AnonimAd_secim_musteri_etiket_ve_arama()
    {
        var o = await SetUpEnvironmentAsync();
        var (anonymous, open, soyad) = await AnonymousAccountsAsync(o);
        foreach (var kim in new[] { Kim.OperatorA, Kim.Muhasebe })
        {
            var s = await LoginAsync(o, kim);
            // Gerçek soyad: yalnız anonim OLMAYAN cari (soyad+"x") döner.
            var find = await Json(await s.C.GetAsync($"{V1}/secim/musteri?q={soyad}"));
            var tek = Assert.Single(find.EnumerateArray());
            Assert.Equal(open.Id, tek.GetProperty("id").GetGuid());
            // Görünen etiketle ("anonim", Türkçe katlamalı) anonim cari bulunur ve etiketle döner.
            var labeled = await Json(await s.C.GetAsync($"{V1}/secim/musteri?q=ANONİM"));
            var a = Assert.Single(labeled.EnumerateArray(), x => x.GetProperty("id").GetGuid() == anonymous.Id);
            Assert.Equal(AnonymousLabel, a.GetProperty("etiket").GetString());
            Assert.DoesNotContain(soyad, labeled.GetRawText());
        }
        var op = await LoginAsync(o, Kim.OperatorA);
        var unique = await Json(await op.C.GetAsync($"{V1}/secim/musteri/{anonymous.Id}"));
        Assert.Equal(AnonymousLabel, unique.GetProperty("etiket").GetString());
        Assert.Equal("Zeynep " + soyad + "x", (await Json(await op.C.GetAsync($"{V1}/secim/musteri/{open.Id}")))
            .GetProperty("etiket").GetString());
    }

    /// <summary>#280 KVKK L-1: selection order follows the displayed name — two anonymised customers whose real
    /// names sort around an open one ("Aaaahmet" &lt; "Kkkenan" &lt; "Zzzzafer") must end up next to each other.</summary>
    [Fact]
    public async Task LowA_secim_musteri_siralamasi_anonimin_gercek_adini_sizdirmaz()
    {
        var o = await SetUpEnvironmentAsync();
        var anonymousA = new Customer { Tip = CustomerType.Bireysel, Ad = "Aaaahmet", Soyad = "Test", AnonimAd = true };
        var open = new Customer { Tip = CustomerType.Bireysel, Ad = "Kkkenan", Soyad = "Test" };
        var anonymousZ = new Customer { Tip = CustomerType.Bireysel, Ad = "Zzzzafer", Soyad = "Test", AnonimAd = true };
        await WriteDataAsync(o.TenantId, db => db.Customers.AddRange(anonymousA, open, anonymousZ));
        var s = await LoginAsync(o, Kim.OperatorA);

        var ids = (await Json(await s.C.GetAsync($"{V1}/secim/musteri?q=n"))).EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();

        Assert.Equal(3, ids.Count); // "Ece Kaya" (ortam carisi) "n" içermez
        Assert.Contains(open.Id, ids);
        var a = ids.IndexOf(anonymousA.Id);
        var z = ids.IndexOf(anonymousZ.Id);
        Assert.True(a >= 0 && z >= 0, "anonim cariler etiketle bulunmalı");
        Assert.Equal(1, Math.Abs(a - z)); // yan yana: konumları gerçek addan bağımsız
    }

    [Fact]
    public async Task LowA_teklif_kabul_tekrari_409_mevcut_rezervasyonu_soyler()
    {
        var o = await SetUpEnvironmentAsync();
        var a = await LoginAsync(o, Kim.OperatorA);
        var start = Tomorrow(2);
        var id = (await Json(await Gonder(a, HttpMethod.Post, TestQuotation, new
        {
            musteriId = o.MusteriId, vehicleId = await VehicleAsync(o), basTar = start, bitTar = start.AddDays(2),
            gunlukUcret = 150m, fiyatTuru = "KDV Dahil Günlük", cikisOfisi = "SubeA",
        }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var accept = await Json(await Gonder(a, HttpMethod.Post, $"{TestQuotation}/{id}/kabul"));

        var p = await ExpectProblem(await Gonder(a, HttpMethod.Post, $"{TestQuotation}/{id}/kabul"), HttpStatusCode.Conflict, "cakisma");
        var m = p.GetProperty("mevcut");
        Assert.Equal(accept.GetProperty("rezervasyonId").GetGuid(), m.GetProperty("rezervasyonId").GetGuid());
        Assert.Equal(accept.GetProperty("rezervasyonNo").GetString(), m.GetProperty("rezervasyonNo").GetString());
        Assert.False(string.IsNullOrEmpty(m.GetProperty("rezervasyonNo").GetString()));
        Assert.Equal(1, (await Json(await a.C.GetAsync(TestReservation))).GetProperty("toplam").GetInt32()); // ikinci rez YOK
    }

    [Fact]
    public async Task LowA_kirada_ya_da_filo_sozlesmesinde_kullanilan_arac_silinemez()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Admin);
        var rentalVehicle = await VehicleAsync(o);
        var resId = await OpenReservationAsync(s, o, rentalVehicle, Tomorrow(2));
        await Json(await Gonder(s, HttpMethod.Post, $"{TestReservation}/{resId}/kiraya-cevir"));
        var fleetVehicle = await VehicleAsync(o);
        await Json(await Gonder(s, HttpMethod.Post, Fleet,
            new { musteriId = o.MusteriId, vehicleId = fleetVehicle, sureAy = 3, aylikUcret = 1000m }), HttpStatusCode.Created);
        var idleVehicle = await VehicleAsync(o);

        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(o.TenantId, role: UserRole.Admin);
        var vehicles = scope.ServiceProvider.GetRequiredService<VehicleService>();
        var e1 = await Assert.ThrowsAsync<ValidationException>(() => vehicles.DeleteAsync(rentalVehicle));
        Assert.Equal("arac", e1.Alan);
        Assert.Contains("kira sözleşmesinde", e1.Message);
        var e2 = await Assert.ThrowsAsync<ValidationException>(() => vehicles.DeleteAsync(fleetVehicle));
        Assert.Equal("arac", e2.Alan);
        Assert.Contains("filo kiralama", e2.Message);
        Assert.True(await vehicles.DeleteAsync(idleVehicle)); // kullanılmayan araç silinir (guard aşırı değil)

        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        Assert.Equal(2, await db.Vehicles.CountAsync(v => v.Id == rentalVehicle || v.Id == fleetVehicle || v.Id == idleVehicle));
    }
}
