using System.Net;
using RentACar.Domain.Entities;
using RentACar.IntegrationTests.Infrastructure;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

public sealed partial class UiWebsiteApiTests
{
    private async Task<Guid> RequestAsync(Env e, string name, string? sube = "SubeA",
        PublicBookingRequestDurum status = PublicBookingRequestDurum.Yeni)
    {
        var t = new PublicBookingRequest
        {
            AdSoyad = name, Telefon = "0532" + System.Random.Shared.Next(1_000_000, 9_999_999), Email = "lead@example.com",
            BasTar = TestZaman.GunSonra(10), BitTar = TestZaman.GunSonra(13), Sube = sube, Not = "Bebek koltuğu",
            GosterilenGunlukUcretKdvDahil = 1200m, GosterilenKdvDahil = true, Durum = status,
        };
        await _kit.WriteAsync(e.TenantId, db => db.SiteTalepleri.Add(t));
        return t.Id;
    }

    [Fact]
    public async Task Booking_requests_lifecycle_and_list()
    {
        var e = await SetupAsync(module: false); // gelen talepler Blazor'da da modül kapısı taşımıyor
        var id = await RequestAsync(e, "Ayşe Yılmaz");
        await RequestAsync(e, "Kapanmış Talep", status: PublicBookingRequestDurum.Reddedildi);
        var admin = await _kit.LoginAsync(e, Who.Admin);

        var page = await Json(await Send(admin, HttpMethod.Get, V1 + "/gelen-talepler?durum=Yeni"));
        Assert.Equal(1, page.GetProperty("toplam").GetInt32());
        Assert.Equal(1, page.GetProperty("ozet").GetProperty("yeni").GetInt32());
        var row = page.GetProperty("kayitlar")[0];
        Assert.Equal("Ayşe Yılmaz", row.GetProperty("adSoyad").GetString());
        Assert.Equal(["Iletisimde", "Kayip"], row.GetProperty("ilerlemeler").EnumerateArray().Select(x => x.GetString()).ToList());
        Assert.Equal(2, (await Json(await Send(admin, HttpMethod.Get, V1 + "/gelen-talepler"))).GetProperty("toplam").GetInt32());
        await Problem(await Send(admin, HttpMethod.Get, V1 + "/gelen-talepler?durum=Bilinmeyen"), HttpStatusCode.BadRequest, "dogrulama", "durum");

        // durum: Dönüştü elle atanamaz (errors[durum]); İletişimde olur
        await Problem(await Send(admin, HttpMethod.Post, $"{V1}/gelen-talepler/{id}/durum", new { durum = "Donustu" }),
            HttpStatusCode.BadRequest, "dogrulama", "durum");
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(admin, HttpMethod.Post, $"{V1}/gelen-talepler/{id}/durum", new { durum = "Iletisimde" })).StatusCode);

        // not + üstlen (kimlik oturumdan)
        await Problem(await Send(admin, HttpMethod.Post, $"{V1}/gelen-talepler/{id}/notlar", new { metin = " " }),
            HttpStatusCode.BadRequest, "dogrulama", "metin");
        var notes = await Json(await Send(admin, HttpMethod.Post, $"{V1}/gelen-talepler/{id}/notlar", new { metin = "Arandı, dönüş bekleniyor." }),
            HttpStatusCode.Created);
        Assert.Equal("Arandı, dönüş bekleniyor.", notes[0].GetProperty("metin").GetString());
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(admin, HttpMethod.Post, $"{V1}/gelen-talepler/{id}/ustlen", new { ustlen = true })).StatusCode);
        row = (await Json(await Send(admin, HttpMethod.Get, V1 + "/gelen-talepler?durum=Iletisimde"))).GetProperty("kayitlar")[0];
        Assert.Equal(e.Users[Who.Admin], row.GetProperty("atananAd").GetString());
        Assert.Equal(1, row.GetProperty("notSayisi").GetInt32());

        // reddet → kapanır; ikinci red 400
        Assert.Equal(HttpStatusCode.NoContent, (await Send(admin, HttpMethod.Post, $"{V1}/gelen-talepler/{id}/reddet")).StatusCode);
        await Problem(await Send(admin, HttpMethod.Post, $"{V1}/gelen-talepler/{id}/reddet"), HttpStatusCode.BadRequest, "dogrulama");
        Assert.Empty((await Json(await Send(admin, HttpMethod.Get, $"{V1}/gelen-talepler/{id}/aday-araclar"))).EnumerateArray());

        // izinsiz rol 403, başka kiracı 404 (her alt uçta)
        var acc = await _kit.LoginAsync(e, Who.Accounting);
        await Problem(await Send(acc, HttpMethod.Get, V1 + "/gelen-talepler"), HttpStatusCode.Forbidden, "yetki_yok");
        var other = await SetupAsync(module: false);
        var oa = await _kit.LoginAsync(other, Who.Admin);
        Assert.Equal(0, (await Json(await Send(oa, HttpMethod.Get, V1 + "/gelen-talepler"))).GetProperty("toplam").GetInt32());
        foreach (var (m, path, body) in new (HttpMethod, string, object?)[]
                 {
                     (HttpMethod.Get, "notlar", null), (HttpMethod.Get, "aday-araclar", null),
                     (HttpMethod.Post, "durum", new { durum = "Kayip" }), (HttpMethod.Post, "ustlen", new { ustlen = true }),
                     (HttpMethod.Post, "notlar", new { metin = "x" }), (HttpMethod.Post, "reddet", null),
                     (HttpMethod.Post, "donustur", new { aracId = Guid.NewGuid() }),
                 })
            await Problem(await Send(oa, m, $"{V1}/gelen-talepler/{id}/{path}", body), HttpStatusCode.NotFound, null);
    }

    [Fact]
    public async Task Booking_request_conversion_checks_vehicle_scope_at_entry()
    {
        var e = await SetupAsync(module: false);
        var id = await RequestAsync(e, "Mehmet Demir", sube: null);
        var vehicleA = await VehicleAsync(e, sube: "SubeA");
        var vehicleB = await VehicleAsync(e, sube: "SubeB");

        // operatör (SubeA) aday listesinde yalnız kendi şubesinin aracını görür
        var op = await _kit.LoginAsync(e, Who.OperatorA);
        var candidates = await Json(await Send(op, HttpMethod.Get, $"{V1}/gelen-talepler/{id}/aday-araclar"));
        Assert.Equal([vehicleA], candidates.EnumerateArray().Select(v => v.GetProperty("id").GetGuid()).ToList());

        // kapsam dışı araçla dönüştürme GİRİŞTE 403; talep açık kalır (claim alınmadı)
        await Problem(await Send(op, HttpMethod.Post, $"{V1}/gelen-talepler/{id}/donustur", new { aracId = vehicleB }),
            HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(op, HttpMethod.Post, $"{V1}/gelen-talepler/{id}/donustur", new { aracId = Guid.NewGuid() }),
            HttpStatusCode.BadRequest, "dogrulama", "aracId");
        await Problem(await Send(op, HttpMethod.Post, $"{V1}/gelen-talepler/{id}/donustur", new { }),
            HttpStatusCode.BadRequest, "dogrulama", "aracId");
        var admin = await _kit.LoginAsync(e, Who.Admin);
        Assert.Equal(1, (await Json(await Send(admin, HttpMethod.Get, V1 + "/gelen-talepler?durum=Yeni"))).GetProperty("toplam").GetInt32());

        // kapsam içi araçla dönüşür: gerçek rezervasyon + talep "Donustu"
        var converted = await Json(await Send(op, HttpMethod.Post, $"{V1}/gelen-talepler/{id}/donustur", new { aracId = vehicleA }));
        var reservationId = converted.GetProperty("rezervasyonId").GetGuid();
        var row = (await Json(await Send(admin, HttpMethod.Get, V1 + "/gelen-talepler?durum=Donustu"))).GetProperty("kayitlar")[0];
        Assert.Equal(reservationId, row.GetProperty("donusenRezervasyonId").GetGuid());
        Assert.Equal(vehicleA, await _kit.ReadAsync(e.TenantId, async db =>
            (await db.Reservations.FindAsync(reservationId))!.VehicleId));
    }
}
