using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Web.Identity;

namespace RentACar.IntegrationTests;

public sealed partial class UiRezervasyonTests
{
    private const string Sart = V1 + "/rez-sartlari";
    private const string Filo = V1 + "/filo-kiralama";

    [Fact]
    public async Task RezSart_yasam_dongusu_surum_ve_ust_kayit_kapsami()
    {
        var o = await OrtamKurAsync();
        var a = await GirisAsync(o, Kim.OperatorA);
        var rezA = await RezAcAsync(a, o, await AracAsync(o), Yarin());

        var c = await Json(await Gonder(a, HttpMethod.Post, Sart, new
        { musteriId = o.MusteriId, sart = "Bebek koltuğu", grup = "Ekipman", reservationId = rezA }), HttpStatusCode.Created);
        var id = c.GetProperty("id").GetGuid();
        Assert.False(c.GetProperty("karsilandi").GetBoolean());
        var surum = c.GetProperty("surum").GetString();

        var k = await Json(await Gonder(a, HttpMethod.Post, $"{Sart}/{id}/karsilandi", new { teslimEden = "Ali" }));
        Assert.True(k.GetProperty("karsilandi").GetBoolean());
        Assert.Equal(1, (await Json(await a.C.GetAsync($"{Sart}?durum=karsilanan"))).GetProperty("toplam").GetInt32());
        Assert.Equal(0, (await Json(await a.C.GetAsync($"{Sart}?durum=bekleyen"))).GetProperty("toplam").GetInt32());
        // Karşılandı işareti satırı değiştirdi → ilk sürüm bayat (409).
        var put = new { musteriId = o.MusteriId, sart = "Bebek koltuğu (2)", grup = "Ekipman", reservationId = rezA, surum };
        await ProblemBekle(await Gonder(a, HttpMethod.Put, $"{Sart}/{id}", put), HttpStatusCode.Conflict, "cakisma");
        var guncel = k.GetProperty("surum").GetString();
        var u = await Json(await Gonder(a, HttpMethod.Put, $"{Sart}/{id}", put with { surum = guncel }));
        Assert.Equal("Bebek koltuğu (2)", u.GetProperty("sart").GetString());
        Assert.False(await GeriAlVeKontrol(a, id));

        // Operatör B: bağlı rezervasyon A şubesinde → tekil 403, listede görünmez, B'nin kaydına bağlayamaz.
        var b = await GirisAsync(o, Kim.OperatorB);
        await ProblemBekle(await b.C.GetAsync($"{Sart}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
        await ProblemBekle(await Gonder(b, HttpMethod.Delete, $"{Sart}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(0, (await Json(await b.C.GetAsync(Sart))).GetProperty("toplam").GetInt32());
        await ProblemBekle(await Gonder(b, HttpMethod.Post, Sart, new { musteriId = o.MusteriId, sart = "x", reservationId = rezA }),
            HttpStatusCode.Forbidden, "yetki_yok");
        await ProblemBekle(await Gonder(a, HttpMethod.Post, Sart, new { musteriId = o.MusteriId, sart = "x", grup = new string('g', 70) }),
            HttpStatusCode.BadRequest, "dogrulama", "grup");

        Assert.Equal(HttpStatusCode.NoContent, (await Gonder(a, HttpMethod.Delete, $"{Sart}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.C.GetAsync($"{Sart}/{id}")).StatusCode);
    }

    private async Task<bool> GeriAlVeKontrol(Oturum s, Guid id)
        => (await Json(await Gonder(s, HttpMethod.Post, $"{Sart}/{id}/geri-al"))).GetProperty("karsilandi").GetBoolean();

    [Fact]
    public async Task FiloKiralama_taksit_oracle_kunye_surum_ve_durum()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var s = await GirisAsync(o, Kim.OperatorA);
        var c = await Json(await Gonder(s, HttpMethod.Post, Filo, new
        { musteriId = o.MusteriId, vehicleId = arac, basTar = Yarin(), sureAy = 3, aylikUcret = 1000m, kdvOrani = 0.20m }),
            HttpStatusCode.Created);
        var id = c.GetProperty("id").GetGuid();

        // ORACLE: 3 × (1.000 net + 200 KDV) = 3.600; damga yok.
        var d = await Json(await s.C.GetAsync($"{Filo}/{id}"));
        var oz = d.GetProperty("ozet");
        Assert.Equal(3000m, oz.GetProperty("toplamNet").GetDecimal());
        Assert.Equal(600m, oz.GetProperty("toplamKdv").GetDecimal());
        Assert.Equal(3600m, oz.GetProperty("genelToplam").GetDecimal());
        Assert.All(oz.GetProperty("taksitler").EnumerateArray(), t => Assert.Equal(1200m, t.GetProperty("toplam").GetDecimal()));
        Assert.Equal(3600m, (await Json(await s.C.GetAsync(Filo))).GetProperty("kayitlar")[0].GetProperty("genelToplam").GetDecimal());

        var surum = d.GetProperty("surum").GetString();
        var k = await Json(await Gonder(s, HttpMethod.Put, $"{Filo}/{id}/kunye", new { surum, sozlesmeNo = "S-1", vadeGun = 30 }));
        Assert.Equal("S-1", k.GetProperty("sozlesmeNo").GetString());
        Assert.Equal(3600m, k.GetProperty("ozet").GetProperty("genelToplam").GetDecimal()); // künye planı değiştirmez
        await ProblemBekle(await Gonder(s, HttpMethod.Put, $"{Filo}/{id}/kunye", new { surum, sozlesmeNo = "S-2" }), HttpStatusCode.Conflict, "cakisma");
        await ProblemBekle(await Gonder(s, HttpMethod.Put, $"{Filo}/{id}/kunye", new { sozlesmeNo = "S-2" }), HttpStatusCode.BadRequest, "dogrulama", "surum");

        // Sınırlar ve varlık.
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Filo, new { musteriId = o.MusteriId, vehicleId = arac, sureAy = 3, aylikUcret = 1000m, doviz = "EURO" }),
            HttpStatusCode.BadRequest, "dogrulama", "doviz");
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Filo, new { musteriId = o.MusteriId, vehicleId = Guid.NewGuid(), sureAy = 3, aylikUcret = 1000m }),
            HttpStatusCode.BadRequest, "dogrulama", "vehicleId");
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Filo, new { musteriId = o.MusteriId, vehicleId = arac, sureAy = 0, aylikUcret = 1000m }),
            HttpStatusCode.BadRequest, "dogrulama", "sureAy");

        // Operatör iptal edemez (OperationsDelete); tamamla → Tamamlandi.
        Assert.Equal(HttpStatusCode.Forbidden, (await Gonder(s, HttpMethod.Post, $"{Filo}/{id}/iptal")).StatusCode);
        Assert.Equal("Tamamlandi", (await Json(await Gonder(s, HttpMethod.Post, $"{Filo}/{id}/tamamla"))).GetProperty("durum").GetString());
        var x = await GirisAsync(await OrtamKurAsync(), Kim.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await x.C.GetAsync($"{Filo}/{id}")).StatusCode);
    }

    /// <summary>
    /// F5.2b (#271 adversarial Low-1): belge tarihi sınırı yalnız tarih DEĞİŞİYORSA uygulanır. 1995 tarihli ESKİ
    /// sözleşme (sınır: 2000 öncesi reddedilir) doğrudan DB'ye yazılır; yalnız açıklaması değişen künye PUT'u 200,
    /// tarihi 1995'ten 1996'ya çeviren PUT 400 (alan <c>sozlesmeTarihi</c>).
    /// </summary>
    [Fact]
    public async Task FiloKunye_sinir_disi_eski_tarih_yalniz_degisirse_denetlenir()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var eski = new DateTimeOffset(1995, 3, 10, 0, 0, 0, TimeSpan.Zero);
        var k = new Domain.Entities.FiloKiralama
        {
            TenantId = o.TenantId, No = "FK-ESKI-" + Guid.NewGuid().ToString("N")[..6], MusteriId = o.MusteriId,
            VehicleId = arac, BasTar = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), SureAy = 12,
            AylikUcret = 1000m, SozlesmeTarihi = eski, ImzaTarih = eski,
        };
        await VeriYazAsync(o.TenantId, db => db.FiloKiralamalar.Add(k));

        var s = await GirisAsync(o, Kim.OperatorA);
        var d = await Json(await s.C.GetAsync($"{Filo}/{k.Id}"));
        var surum = d.GetProperty("surum").GetString();

        var r = await Json(await Gonder(s, HttpMethod.Put, $"{Filo}/{k.Id}/kunye",
            new { surum, sozlesmeTarihi = eski, imzaTarih = eski, aciklama = "yalnız açıklama" }));
        Assert.Equal("yalnız açıklama", r.GetProperty("aciklama").GetString());
        Assert.Equal(eski, r.GetProperty("sozlesmeTarihi").GetDateTimeOffset());

        var yeniSurum = r.GetProperty("surum").GetString();
        await ProblemBekle(await Gonder(s, HttpMethod.Put, $"{Filo}/{k.Id}/kunye",
                new { surum = yeniSurum, sozlesmeTarihi = eski.AddYears(1), imzaTarih = eski, aciklama = "yalnız açıklama" }),
            HttpStatusCode.BadRequest, "dogrulama", "sozlesmeTarihi");
    }

    [Fact]
    public void Izin_haritasi_Blazor_ile_ayni()
    {
        string[] onekler = [Rez, Teklif, V1 + "/takvim", V1 + "/musaitlik", Sart, Filo];
        // Önek SEGMENT sınırında eşleşir: F11.1a'nın /takvim-abonelik (kişisel iCal bağlantısı, F5 değil) uçları
        // çıplak StartsWith ile "/takvim" önekine takılıp haritayı şişiriyordu.
        static bool Altinda(string yol, string onek)
            => yol.StartsWith(onek, StringComparison.Ordinal) && (yol.Length == onek.Length || yol[onek.Length] == '/');
        var uclar = fx.Web.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => onekler.Any(p => Altinda("/" + (e.RoutePattern.RawText ?? "").TrimStart('/'), p)))
            .ToDictionary(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Single() ?? "?") + " /" + e.RoutePattern.RawText!.Trim('/'),
                e => e.Metadata.GetMetadata<IzinMetadata>()?.Izin.ToString());
        var dar = new[] { $"POST {Rez}/{{id:guid}}/iptal", $"POST {Filo}/{{id:guid}}/iptal" };
        Assert.Equal(32, uclar.Count);
        foreach (var (anahtar, izin) in uclar)
            Assert.True((dar.Contains(anahtar) ? "OperationsDelete" : "OperationsWrite") == izin, $"{anahtar}: {izin}");
    }
}
