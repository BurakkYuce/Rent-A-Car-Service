using System.Net;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

/// <summary>
/// Güvenlik tur 3 (probe <c>Probe374c_operator_manageusers_branch_merge_widens_scope</c> kalıcı teste çevrildi):
/// ManageUsers kullanıcı bazlı istisnayla devredilebildiği için şube yazmaları (oluştur/güncelle/sil/hizmet/birleştir)
/// yalnız Admin ROLÜNE. İstisnalı operatör başka şubeyi kendi şubesine birleştirip kapsamını genişletebiliyordu.
/// ORACLE elle: SubeA operatörü, Kadıköy'de bir araç; birleştirme 403, araç hâlâ kapsam dışı, Kadıköy aktif.
/// </summary>
public sealed partial class UiSystemAdminTests
{
    private const string Branches = V1 + "/subeler";

    [Fact]
    public async Task Sube_yazmalari_yalniz_Admin_rolu_ManageUsers_istisnali_operator_birlestiremez()
    {
        var e = await _kit.SetupAsync();
        var subeA = new Branch { Kod = "SA", Ad = "SubeA", Aktif = true };
        var kdk = new Branch { Kod = "KD", Ad = "Kadikoy", Aktif = true };
        var v = new Vehicle
        {
            Plaka = "34PR" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(), Marka = "F", Tip = "E", Grup = "C",
            Sube = "Kadikoy", Durum = VehicleStatus.Musait, Km = 1,
        };
        await _kit.WriteAsync(e.TenantId, db => db.Branches.AddRange(subeA, kdk));
        await _kit.WriteAsync(e.TenantId, db => db.Vehicles.Add(v));
        var admin = await _kit.LoginAsync(e, Who.Admin);
        await Json(await Send(admin, HttpMethod.Put, $"{Users}/{e.UserIds[Who.OperatorA]}/istisnalar/ManageUsers", new { ver = true }));

        var op = await _kit.LoginAsync(e, Who.OperatorA);
        Assert.Equal(HttpStatusCode.OK, (await op.C.GetAsync(Branches)).StatusCode); // okuma ManageUsers ile açık
        Assert.NotEqual(HttpStatusCode.OK, (await op.C.GetAsync($"{V1}/araclar/{v.Id}")).StatusCode);

        await Problem(await Send(op, HttpMethod.Post, $"{Branches}/birlestir", new { kaynakId = kdk.Id, hedefId = subeA.Id, onay = true }),
            HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(op, HttpMethod.Post, Branches, new { kod = "YENI", ad = "Yeni Şube" }), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(op, HttpMethod.Post, $"{Branches}/{subeA.Id}/hizmetler", new { hizmetAdi = "Çay" }),
            HttpStatusCode.Forbidden, "yetki_yok");
        Assert.NotEqual(HttpStatusCode.OK, (await op.C.GetAsync($"{V1}/araclar/{v.Id}")).StatusCode); // kapsam genişlemedi
        Assert.True(await _kit.ReadAsync(e.TenantId, db => db.Branches.AsNoTracking().Where(b => b.Id == kdk.Id).Select(b => b.Aktif).SingleAsync()));

        // Admin rolü birleştirir.
        var merged = await Json(await Send(admin, HttpMethod.Post, $"{Branches}/birlestir", new { kaynakId = kdk.Id, hedefId = subeA.Id, onay = true }));
        Assert.True(merged.ValueKind == System.Text.Json.JsonValueKind.Object);
        Assert.False(await _kit.ReadAsync(e.TenantId, db => db.Branches.AsNoTracking().Where(b => b.Id == kdk.Id).Select(b => b.Aktif).SingleAsync()));
    }
}
