using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests;

/// <summary>F5.1 adversarial kalıcı çitleri, bölüm 2: filo şube kapsamı (M3), müsaitlik İstanbul saati (L4), anonim arama (L5).</summary>
public sealed partial class UiRezervasyonTests
{
    [Fact]
    public async Task M3_filo_kiralama_arac_subesi_kapsami()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicleA = await VehicleAsync(o, branch: "SubeA");
        var a = await LoginAsync(o, Kim.OperatorA);
        var b = await LoginAsync(o, Kim.OperatorB);
        object Body(Guid vehicle) => new { musteriId = o.MusteriId, vehicleId = vehicle, sureAy = 3, aylikUcret = 1000m };

        // Başka şubenin aracına sözleşme açılamaz (403, hiçbir şey yazılmaz).
        await ExpectProblem(await Gonder(b, HttpMethod.Post, Fleet, Body(vehicleA)), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(0, await ReadAsync(o.TenantId, db => db.FiloKiralamalar.CountAsync()));

        var id = (await Json(await Gonder(a, HttpMethod.Post, Fleet, Body(vehicleA)), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var version = (await Json(await a.C.GetAsync($"{Fleet}/{id}"))).GetProperty("surum").GetString();

        // Liste: A 1 kayıt, B 0; admin (kapsamsız) 1.
        Assert.Equal(1, (await Json(await a.C.GetAsync(Fleet))).GetProperty("toplam").GetInt32());
        Assert.Equal(0, (await Json(await b.C.GetAsync(Fleet))).GetProperty("toplam").GetInt32());
        Assert.Equal(1, (await Json(await (await LoginAsync(o, Kim.Admin)).C.GetAsync(Fleet))).GetProperty("toplam").GetInt32());

        // Tekil uçlar: kapsam durumdan ÖNCE → 403 (içerik/durum sızmaz), kayıt değişmez.
        await ExpectProblem(await b.C.GetAsync($"{Fleet}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
        await ExpectProblem(await Gonder(b, HttpMethod.Post, $"{Fleet}/{id}/tamamla"), HttpStatusCode.Forbidden, "yetki_yok");
        await ExpectProblem(await Gonder(b, HttpMethod.Put, $"{Fleet}/{id}/kunye", new { surum = version, sozlesmeNo = "B-EZER" }),
            HttpStatusCode.Forbidden, "yetki_yok");
        var k = await ReadAsync(o.TenantId, db => db.FiloKiralamalar.AsNoTracking().FirstAsync(x => x.Id == id));
        Assert.Equal(FleetRentalStatus.Aktif, k.Durum);
        Assert.Null(k.SozlesmeNo);
    }

    [Fact]
    public async Task L4_musaitlik_saati_Istanbul_saatidir()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var s = await LoginAsync(o, Kim.Admin);
        var ist = TimeSpan.FromHours(3); // Türkiye 2016'dan beri sabit UTC+3
        var now = DateTimeOffset.UtcNow.ToOffset(ist);
        var day = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, ist).AddDays(10);
        // Araç gün X 00:30 → 10:00 İstanbul dolu (kiraya çevrilmiş).
        var g = ReservationBody(o, vehicle, day.AddMinutes(30), 1); g["bitTar"] = day.AddHours(10);
        var id = (await Json(await Gonder(s, HttpMethod.Post, TestReservation, g), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await Json(await Gonder(s, HttpMethod.Post, $"{TestReservation}/{id}/kiraya-cevir"));

        async Task<(bool Gorunur, DateTimeOffset Bas)> Search(string hour)
        {
            var m = await Json(await s.C.GetAsync($"{V1}/musaitlik?basGun={day:yyyy-MM-dd}&basSaat={hour}&gun=1"));
            return (m.GetProperty("araclar").EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == vehicle),
                m.GetProperty("pencereBas").GetDateTimeOffset());
        }
        // 08:00 İstanbul = 05:00Z; araç 10:00'a kadar dolu → görünmez.
        var morning = await Search("08:00");
        Assert.False(morning.Gorunur);
        Assert.Equal(new DateTimeOffset(day.Year, day.Month, day.Day, 5, 0, 0, TimeSpan.Zero), morning.Bas.ToUniversalTime());
        // 11:00 İstanbul: dönüşten sonra → görünür (UTC sayılsaydı 14:00 olurdu; iki tarafın da doğru olduğu kontrol).
        Assert.True((await Search("11:00")).Gorunur);
    }

    [Fact]
    public async Task L5_anonim_musteri_gercek_adiyla_aranamaz()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var s = await LoginAsync(o, Kim.OperatorA);
        var soyad = "Gizli" + Guid.NewGuid().ToString("N")[..6];
        var anonymous = new Customer { Tip = CustomerType.Bireysel, Ad = "Zeynep", Soyad = soyad, CepTel = "05320000001", AnonimAd = true, AnonimTelefon = true };
        var open = new Customer { Tip = CustomerType.Bireysel, Ad = "Zeynep", Soyad = soyad + "x", CepTel = "05320000002" };
        await WriteDataAsync(o.TenantId, db => db.Customers.AddRange(anonymous, open));
        var g1 = ReservationBody(o, vehicle, Tomorrow(2)); g1["musteriId"] = anonymous.Id;
        var resNo = (await Json(await Gonder(s, HttpMethod.Post, TestReservation, g1), HttpStatusCode.Created)).GetProperty("no").GetString();
        var g2 = ReservationBody(o, await VehicleAsync(o), Tomorrow(2)); g2["musteriId"] = open.Id;
        await Json(await Gonder(s, HttpMethod.Post, TestReservation, g2), HttpStatusCode.Created);

        // Oracle: soyad terimi yalnız anonim OLMAYAN cariyi bulur (1); anonim rezervasyon numarasıyla bulunur.
        var l = await Json(await s.C.GetAsync($"{TestReservation}?q={soyad}"));
        Assert.Equal(1, l.GetProperty("toplam").GetInt32());
        Assert.NotEqual(soyad, l.GetProperty("kayitlar")[0].GetProperty("musteriAd").GetString()?.Split(' ').Last());
        Assert.Equal(1, (await Json(await s.C.GetAsync($"{TestReservation}?q={resNo}"))).GetProperty("toplam").GetInt32());
    }
}
