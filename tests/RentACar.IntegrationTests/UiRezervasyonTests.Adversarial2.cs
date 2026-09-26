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
        var o = await OrtamKurAsync();
        var aracA = await AracAsync(o, sube: "SubeA");
        var a = await GirisAsync(o, Kim.OperatorA);
        var b = await GirisAsync(o, Kim.OperatorB);
        object Govde(Guid arac) => new { musteriId = o.MusteriId, vehicleId = arac, sureAy = 3, aylikUcret = 1000m };

        // Başka şubenin aracına sözleşme açılamaz (403, hiçbir şey yazılmaz).
        await ProblemBekle(await Gonder(b, HttpMethod.Post, Filo, Govde(aracA)), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(0, await OkuAsync(o.TenantId, db => db.FiloKiralamalar.CountAsync()));

        var id = (await Json(await Gonder(a, HttpMethod.Post, Filo, Govde(aracA)), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var surum = (await Json(await a.C.GetAsync($"{Filo}/{id}"))).GetProperty("surum").GetString();

        // Liste: A 1 kayıt, B 0; admin (kapsamsız) 1.
        Assert.Equal(1, (await Json(await a.C.GetAsync(Filo))).GetProperty("toplam").GetInt32());
        Assert.Equal(0, (await Json(await b.C.GetAsync(Filo))).GetProperty("toplam").GetInt32());
        Assert.Equal(1, (await Json(await (await GirisAsync(o, Kim.Admin)).C.GetAsync(Filo))).GetProperty("toplam").GetInt32());

        // Tekil uçlar: kapsam durumdan ÖNCE → 403 (içerik/durum sızmaz), kayıt değişmez.
        await ProblemBekle(await b.C.GetAsync($"{Filo}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
        await ProblemBekle(await Gonder(b, HttpMethod.Post, $"{Filo}/{id}/tamamla"), HttpStatusCode.Forbidden, "yetki_yok");
        await ProblemBekle(await Gonder(b, HttpMethod.Put, $"{Filo}/{id}/kunye", new { surum, sozlesmeNo = "B-EZER" }),
            HttpStatusCode.Forbidden, "yetki_yok");
        var k = await OkuAsync(o.TenantId, db => db.FiloKiralamalar.AsNoTracking().FirstAsync(x => x.Id == id));
        Assert.Equal(FleetRentalStatus.Aktif, k.Durum);
        Assert.Null(k.SozlesmeNo);
    }

    [Fact]
    public async Task L4_musaitlik_saati_Istanbul_saatidir()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var s = await GirisAsync(o, Kim.Admin);
        var ist = TimeSpan.FromHours(3); // Türkiye 2016'dan beri sabit UTC+3
        var simdi = DateTimeOffset.UtcNow.ToOffset(ist);
        var gun = new DateTimeOffset(simdi.Year, simdi.Month, simdi.Day, 0, 0, 0, ist).AddDays(10);
        // Araç gün X 00:30 → 10:00 İstanbul dolu (kiraya çevrilmiş).
        var g = RezGovde(o, arac, gun.AddMinutes(30), 1); g["bitTar"] = gun.AddHours(10);
        var id = (await Json(await Gonder(s, HttpMethod.Post, Rez, g), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await Json(await Gonder(s, HttpMethod.Post, $"{Rez}/{id}/kiraya-cevir"));

        async Task<(bool Gorunur, DateTimeOffset Bas)> Ara(string saat)
        {
            var m = await Json(await s.C.GetAsync($"{V1}/musaitlik?basGun={gun:yyyy-MM-dd}&basSaat={saat}&gun=1"));
            return (m.GetProperty("araclar").EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == arac),
                m.GetProperty("pencereBas").GetDateTimeOffset());
        }
        // 08:00 İstanbul = 05:00Z; araç 10:00'a kadar dolu → görünmez.
        var sabah = await Ara("08:00");
        Assert.False(sabah.Gorunur);
        Assert.Equal(new DateTimeOffset(gun.Year, gun.Month, gun.Day, 5, 0, 0, TimeSpan.Zero), sabah.Bas.ToUniversalTime());
        // 11:00 İstanbul: dönüşten sonra → görünür (UTC sayılsaydı 14:00 olurdu; iki tarafın da doğru olduğu kontrol).
        Assert.True((await Ara("11:00")).Gorunur);
    }

    [Fact]
    public async Task L5_anonim_musteri_gercek_adiyla_aranamaz()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var s = await GirisAsync(o, Kim.OperatorA);
        var soyad = "Gizli" + Guid.NewGuid().ToString("N")[..6];
        var anonim = new Customer { Tip = CustomerType.Bireysel, Ad = "Zeynep", Soyad = soyad, CepTel = "05320000001", AnonimAd = true, AnonimTelefon = true };
        var acik = new Customer { Tip = CustomerType.Bireysel, Ad = "Zeynep", Soyad = soyad + "x", CepTel = "05320000002" };
        await VeriYazAsync(o.TenantId, db => db.Customers.AddRange(anonim, acik));
        var g1 = RezGovde(o, arac, Yarin(2)); g1["musteriId"] = anonim.Id;
        var rezNo = (await Json(await Gonder(s, HttpMethod.Post, Rez, g1), HttpStatusCode.Created)).GetProperty("no").GetString();
        var g2 = RezGovde(o, await AracAsync(o), Yarin(2)); g2["musteriId"] = acik.Id;
        await Json(await Gonder(s, HttpMethod.Post, Rez, g2), HttpStatusCode.Created);

        // Oracle: soyad terimi yalnız anonim OLMAYAN cariyi bulur (1); anonim rezervasyon numarasıyla bulunur.
        var l = await Json(await s.C.GetAsync($"{Rez}?q={soyad}"));
        Assert.Equal(1, l.GetProperty("toplam").GetInt32());
        Assert.NotEqual(soyad, l.GetProperty("kayitlar")[0].GetProperty("musteriAd").GetString()?.Split(' ').Last());
        Assert.Equal(1, (await Json(await s.C.GetAsync($"{Rez}?q={rezNo}"))).GetProperty("toplam").GetInt32());
    }
}
