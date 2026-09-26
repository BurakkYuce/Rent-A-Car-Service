using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Web.Identity;

namespace RentACar.IntegrationTests;

public sealed partial class UiRezervasyonTests
{
    private const string Term = V1 + "/rez-sartlari";
    private const string Fleet = V1 + "/filo-kiralama";

    [Fact]
    public async Task RezSart_yasam_dongusu_surum_ve_ust_kayit_kapsami()
    {
        var o = await SetUpEnvironmentAsync();
        var a = await LoginAsync(o, Kim.OperatorA);
        var resA = await OpenReservationAsync(a, o, await VehicleAsync(o), Tomorrow());

        var c = await Json(await Gonder(a, HttpMethod.Post, Term, new
        { musteriId = o.MusteriId, sart = "Bebek koltuğu", grup = "Ekipman", reservationId = resA }), HttpStatusCode.Created);
        var id = c.GetProperty("id").GetGuid();
        Assert.False(c.GetProperty("karsilandi").GetBoolean());
        var version = c.GetProperty("surum").GetString();

        var k = await Json(await Gonder(a, HttpMethod.Post, $"{Term}/{id}/karsilandi", new { teslimEden = "Ali" }));
        Assert.True(k.GetProperty("karsilandi").GetBoolean());
        Assert.Equal(1, (await Json(await a.C.GetAsync($"{Term}?durum=karsilanan"))).GetProperty("toplam").GetInt32());
        Assert.Equal(0, (await Json(await a.C.GetAsync($"{Term}?durum=bekleyen"))).GetProperty("toplam").GetInt32());
        // Karşılandı işareti satırı değiştirdi → ilk sürüm bayat (409).
        var put = new { musteriId = o.MusteriId, sart = "Bebek koltuğu (2)", grup = "Ekipman", reservationId = resA, surum = version };
        await ExpectProblem(await Gonder(a, HttpMethod.Put, $"{Term}/{id}", put), HttpStatusCode.Conflict, "cakisma");
        var current = k.GetProperty("surum").GetString();
        var u = await Json(await Gonder(a, HttpMethod.Put, $"{Term}/{id}", put with { surum = current }));
        Assert.Equal("Bebek koltuğu (2)", u.GetProperty("sart").GetString());
        Assert.False(await UndoAndCheck(a, id));

        // Operatör B: bağlı rezervasyon A şubesinde → tekil 403, listede görünmez, B'nin kaydına bağlayamaz.
        var b = await LoginAsync(o, Kim.OperatorB);
        await ExpectProblem(await b.C.GetAsync($"{Term}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
        await ExpectProblem(await Gonder(b, HttpMethod.Delete, $"{Term}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(0, (await Json(await b.C.GetAsync(Term))).GetProperty("toplam").GetInt32());
        await ExpectProblem(await Gonder(b, HttpMethod.Post, Term, new { musteriId = o.MusteriId, sart = "x", reservationId = resA }),
            HttpStatusCode.Forbidden, "yetki_yok");
        await ExpectProblem(await Gonder(a, HttpMethod.Post, Term, new { musteriId = o.MusteriId, sart = "x", grup = new string('g', 70) }),
            HttpStatusCode.BadRequest, "dogrulama", "grup");

        Assert.Equal(HttpStatusCode.NoContent, (await Gonder(a, HttpMethod.Delete, $"{Term}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.C.GetAsync($"{Term}/{id}")).StatusCode);
    }

    private async Task<bool> UndoAndCheck(Oturum s, Guid id)
        => (await Json(await Gonder(s, HttpMethod.Post, $"{Term}/{id}/geri-al"))).GetProperty("karsilandi").GetBoolean();

    [Fact]
    public async Task FiloKiralama_taksit_oracle_kunye_surum_ve_durum()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var s = await LoginAsync(o, Kim.OperatorA);
        var c = await Json(await Gonder(s, HttpMethod.Post, Fleet, new
        { musteriId = o.MusteriId, vehicleId = vehicle, basTar = Tomorrow(), sureAy = 3, aylikUcret = 1000m, kdvOrani = 0.20m }),
            HttpStatusCode.Created);
        var id = c.GetProperty("id").GetGuid();

        // ORACLE: 3 × (1.000 net + 200 KDV) = 3.600; damga yok.
        var d = await Json(await s.C.GetAsync($"{Fleet}/{id}"));
        var oz = d.GetProperty("ozet");
        Assert.Equal(3000m, oz.GetProperty("toplamNet").GetDecimal());
        Assert.Equal(600m, oz.GetProperty("toplamKdv").GetDecimal());
        Assert.Equal(3600m, oz.GetProperty("genelToplam").GetDecimal());
        Assert.All(oz.GetProperty("taksitler").EnumerateArray(), t => Assert.Equal(1200m, t.GetProperty("toplam").GetDecimal()));
        Assert.Equal(3600m, (await Json(await s.C.GetAsync(Fleet))).GetProperty("kayitlar")[0].GetProperty("genelToplam").GetDecimal());

        var version = d.GetProperty("surum").GetString();
        var k = await Json(await Gonder(s, HttpMethod.Put, $"{Fleet}/{id}/kunye", new { surum = version, sozlesmeNo = "S-1", vadeGun = 30 }));
        Assert.Equal("S-1", k.GetProperty("sozlesmeNo").GetString());
        Assert.Equal(3600m, k.GetProperty("ozet").GetProperty("genelToplam").GetDecimal()); // künye planı değiştirmez
        await ExpectProblem(await Gonder(s, HttpMethod.Put, $"{Fleet}/{id}/kunye", new { surum = version, sozlesmeNo = "S-2" }), HttpStatusCode.Conflict, "cakisma");
        await ExpectProblem(await Gonder(s, HttpMethod.Put, $"{Fleet}/{id}/kunye", new { sozlesmeNo = "S-2" }), HttpStatusCode.BadRequest, "dogrulama", "surum");

        // Sınırlar ve varlık.
        await ExpectProblem(await Gonder(s, HttpMethod.Post, Fleet, new { musteriId = o.MusteriId, vehicleId = vehicle, sureAy = 3, aylikUcret = 1000m, doviz = "EURO" }),
            HttpStatusCode.BadRequest, "dogrulama", "doviz");
        await ExpectProblem(await Gonder(s, HttpMethod.Post, Fleet, new { musteriId = o.MusteriId, vehicleId = Guid.NewGuid(), sureAy = 3, aylikUcret = 1000m }),
            HttpStatusCode.BadRequest, "dogrulama", "vehicleId");
        await ExpectProblem(await Gonder(s, HttpMethod.Post, Fleet, new { musteriId = o.MusteriId, vehicleId = vehicle, sureAy = 0, aylikUcret = 1000m }),
            HttpStatusCode.BadRequest, "dogrulama", "sureAy");

        // Operatör iptal edemez (OperationsDelete); tamamla → Tamamlandi.
        Assert.Equal(HttpStatusCode.Forbidden, (await Gonder(s, HttpMethod.Post, $"{Fleet}/{id}/iptal")).StatusCode);
        Assert.Equal("Tamamlandi", (await Json(await Gonder(s, HttpMethod.Post, $"{Fleet}/{id}/tamamla"))).GetProperty("durum").GetString());
        var x = await LoginAsync(await SetUpEnvironmentAsync(), Kim.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await x.C.GetAsync($"{Fleet}/{id}")).StatusCode);
    }

    /// <summary>
    /// F5.2b (#271 adversarial Low-1): belge tarihi sınırı yalnız tarih DEĞİŞİYORSA uygulanır. 1995 tarihli ESKİ
    /// sözleşme (sınır: 2000 öncesi reddedilir) doğrudan DB'ye yazılır; yalnız açıklaması değişen künye PUT'u 200,
    /// tarihi 1995'ten 1996'ya çeviren PUT 400 (alan <c>sozlesmeTarihi</c>).
    /// </summary>
    [Fact]
    public async Task FiloKunye_sinir_disi_eski_tarih_yalniz_degisirse_denetlenir()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var old = new DateTimeOffset(1995, 3, 10, 0, 0, 0, TimeSpan.Zero);
        var k = new Domain.Entities.FiloKiralama
        {
            TenantId = o.TenantId, No = "FK-ESKI-" + Guid.NewGuid().ToString("N")[..6], MusteriId = o.MusteriId,
            VehicleId = vehicle, BasTar = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), SureAy = 12,
            AylikUcret = 1000m, SozlesmeTarihi = old, ImzaTarih = old,
        };
        await WriteDataAsync(o.TenantId, db => db.FiloKiralamalar.Add(k));

        var s = await LoginAsync(o, Kim.OperatorA);
        var d = await Json(await s.C.GetAsync($"{Fleet}/{k.Id}"));
        var version = d.GetProperty("surum").GetString();

        var r = await Json(await Gonder(s, HttpMethod.Put, $"{Fleet}/{k.Id}/kunye",
            new { surum = version, sozlesmeTarihi = old, imzaTarih = old, aciklama = "yalnız açıklama" }));
        Assert.Equal("yalnız açıklama", r.GetProperty("aciklama").GetString());
        Assert.Equal(old, r.GetProperty("sozlesmeTarihi").GetDateTimeOffset());

        var newVersion = r.GetProperty("surum").GetString();
        await ExpectProblem(await Gonder(s, HttpMethod.Put, $"{Fleet}/{k.Id}/kunye",
                new { surum = newVersion, sozlesmeTarihi = old.AddYears(1), imzaTarih = old, aciklama = "yalnız açıklama" }),
            HttpStatusCode.BadRequest, "dogrulama", "sozlesmeTarihi");
    }

    [Fact]
    public void Izin_haritasi_Blazor_ile_ayni()
    {
        string[] prefixes = [TestReservation, TestQuotation, V1 + "/takvim", V1 + "/musaitlik", Term, Fleet];
        // Önek SEGMENT sınırında eşleşir: F11.1a'nın /takvim-abonelik (kişisel iCal bağlantısı, F5 değil) uçları
        // çıplak StartsWith ile "/takvim" önekine takılıp haritayı şişiriyordu.
        static bool Below(string path, string prefix)
            => path.StartsWith(prefix, StringComparison.Ordinal) && (path.Length == prefix.Length || path[prefix.Length] == '/');
        var endpoints = fx.Web.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => prefixes.Any(p => Below("/" + (e.RoutePattern.RawText ?? "").TrimStart('/'), p)))
            .ToDictionary(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Single() ?? "?") + " /" + e.RoutePattern.RawText!.Trim('/'),
                e => e.Metadata.GetMetadata<IzinMetadata>()?.Izin.ToString());
        var narrow = new[] { $"POST {TestReservation}/{{id:guid}}/iptal", $"POST {Fleet}/{{id:guid}}/iptal" };
        Assert.Equal(32, endpoints.Count);
        foreach (var (key, permission) in endpoints)
            Assert.True((narrow.Contains(key) ? "OperationsDelete" : "OperationsWrite") == permission, $"{key}: {permission}");
    }
}
