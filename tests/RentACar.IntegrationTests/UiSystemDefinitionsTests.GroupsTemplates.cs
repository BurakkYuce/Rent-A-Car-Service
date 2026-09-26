using System.Net;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

public sealed partial class UiSystemDefinitionsTests
{
    private static string Plate() => "34T" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();

    [Fact]
    public async Task Vehicle_group_crud_rename_cascade_assign_and_version()
    {
        var e = await _kit.SetupAsync();
        // filo ÖNCE yazılır (araç listesi kiracı önbelleğinde; doğrudan DB yazımı önbelleği tazelemez):
        // 2 araç "Ekonomi" (biri farklı yazımla), 1 araç "EKNM" (eşleşmeyen), 1 araç grupsuz
        await _kit.WriteAsync(e.TenantId, db =>
        {
            db.Vehicles.Add(new Vehicle { Plaka = Plate(), Grup = "Ekonomi", Sube = "SubeA", Durum = VehicleStatus.Musait });
            db.Vehicles.Add(new Vehicle { Plaka = Plate(), Grup = "EKONOMİ", Sube = "SubeA", Durum = VehicleStatus.Musait });
            db.Vehicles.Add(new Vehicle { Plaka = Plate(), Grup = "EKNM", Sube = "SubeA", Durum = VehicleStatus.Musait });
            db.Vehicles.Add(new Vehicle { Plaka = Plate(), Grup = null, Sube = "SubeA", Durum = VehicleStatus.Musait });
        });
        var admin = await _kit.LoginAsync(e, Who.Admin);
        const string path = V1 + "/arac-gruplari";

        var eko =await Json(await Send(admin, HttpMethod.Post, path, new
        {
            kod = "eko", ad = "Ekonomi", sipp = "cdmr", vites = "Manuel", yakitTuru = "Dizel", provizyon = 2500m, surucuMinYas = 21,
        }), HttpStatusCode.Created);
        var ekoId = eko.GetProperty("id").GetGuid();
        Assert.Equal("CDMR", eko.GetProperty("sipp").GetString());
        Assert.Equal("Manuel", eko.GetProperty("vites").GetString());
        Assert.Equal(2500m, eko.GetProperty("provizyon").GetDecimal());

        await Problem(await Send(admin, HttpMethod.Post, path, new { kod = "LUX", ad = "Lüks", sipp = "ABC" }), HttpStatusCode.BadRequest, "dogrulama", "sipp");
        await Problem(await Send(admin, HttpMethod.Post, path, new { kod = "LUX", ad = "Lüks", vites = "Uçan" }), HttpStatusCode.BadRequest, "dogrulama", "vites");
        await Problem(await Send(admin, HttpMethod.Post, path, new { kod = "LUX", ad = "Lüks", provizyon = -1m }), HttpStatusCode.BadRequest, "dogrulama", "provizyon");
        await Problem(await Send(admin, HttpMethod.Post, path, new { kod = "EKO", ad = "Başka" }), HttpStatusCode.BadRequest, "dogrulama", "kod");

        Assert.Equal(2, (await Json(await admin.C.GetAsync($"{path}/{ekoId}"))).GetProperty("aracSayisi").GetInt32());
        var unmatched = (await Json(await admin.C.GetAsync($"{path}/eslesmeyen"))).EnumerateArray()
            .Select(x => (x.GetProperty("grup").GetString(), x.GetProperty("aracSayisi").GetInt32(), x.GetProperty("bos").GetBoolean())).ToList();
        Assert.Equal([("EKNM", 1, false), ("(boş)", 1, true)], unmatched);

        // ata: EKNM → Ekonomi (1 araç), boşlar → Ekonomi (1 araç)
        Assert.Equal(1, (await Json(await Send(admin, HttpMethod.Post, $"{path}/ata", new { hedefGrupId = ekoId, kaynak = "EKNM" }))).GetProperty("tasinan").GetInt32());
        Assert.Equal(1, (await Json(await Send(admin, HttpMethod.Post, $"{path}/ata", new { hedefGrupId = ekoId, bos = true }))).GetProperty("tasinan").GetInt32());
        await Problem(await Send(admin, HttpMethod.Post, $"{path}/ata", new { hedefGrupId = Guid.NewGuid(), kaynak = "X" }), HttpStatusCode.NotFound, null);

        // sürümlü rename: 4 aracın hepsi yeni ada taşınır (aynı işlem)
        var current = await Json(await admin.C.GetAsync($"{path}/{ekoId}"));
        var renamed = await Json(await Send(admin, HttpMethod.Put, $"{path}/{ekoId}", new { kod = "EKO", ad = "Ekonomik", surum = VersionOf(current) }));
        Assert.Equal(4, renamed.GetProperty("aracSayisi").GetInt32());
        var groups = await _kit.ReadAsync(e.TenantId, db => db.Vehicles.Select(v => v.Grup).ToListAsync());
        Assert.All(groups, g => Assert.Equal("Ekonomik", g));
        // bayat sürümle rename: hiçbir araç taşınmaz
        await Problem(await Send(admin, HttpMethod.Put, $"{path}/{ekoId}", new { kod = "EKO", ad = "Bayat", surum = VersionOf(current) }), HttpStatusCode.Conflict, "cakisma");
        groups = await _kit.ReadAsync(e.TenantId, db => db.Vehicles.Select(v => v.Grup).ToListAsync());
        Assert.All(groups, g => Assert.Equal("Ekonomik", g));

        var acc = await _kit.LoginAsync(e, Who.Accounting);
        await Problem(await acc.C.GetAsync(path), HttpStatusCode.Forbidden, "yetki_yok");
        var other = await _kit.SetupAsync();
        var otherAdmin = await _kit.LoginAsync(other, Who.Admin);
        await Problem(await otherAdmin.C.GetAsync($"{path}/{ekoId}"), HttpStatusCode.NotFound, null);
        await Problem(await Send(otherAdmin, HttpMethod.Post, $"{path}/ata", new { hedefGrupId = ekoId, bos = true }), HttpStatusCode.NotFound, null);
    }

    [Fact]
    public async Task Document_template_crud_default_uniqueness_and_manage_users()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        const string path = V1 + "/belge-sablonlari";

        var a = await Json(await Send(admin, HttpMethod.Post, path, new
        {
            belgeTuru = "KiraSozlesmesi", ad = "Standart", varsayilanMi = true, hukukiMetinSol = "<script>x</script> madde 1",
        }), HttpStatusCode.Created);
        var aId = a.GetProperty("id").GetGuid();
        Assert.Equal("KiraSozlesmesi", a.GetProperty("belgeTuru").GetString());
        Assert.Equal("<script>x</script> madde 1", a.GetProperty("hukukiMetinSol").GetString()); // düz metin, yorumlanmaz
        var b = await Json(await Send(admin, HttpMethod.Post, path, new { belgeTuru = "KiraSozlesmesi", ad = "Kurumsal", varsayilanMi = true }), HttpStatusCode.Created);
        // tür başına tek varsayılan: yeni varsayılan eskisini düşürür
        Assert.False((await Json(await admin.C.GetAsync($"{path}/{aId}"))).GetProperty("varsayilanMi").GetBoolean());
        Assert.True(b.GetProperty("varsayilanMi").GetBoolean());

        await Problem(await Send(admin, HttpMethod.Post, path, new { belgeTuru = "KiraSozlesmesi", ad = "Standart" }), HttpStatusCode.BadRequest, "dogrulama", "ad");
        await Problem(await Send(admin, HttpMethod.Post, path, new { ad = "Türsüz" }), HttpStatusCode.BadRequest, "dogrulama", "belgeTuru");
        await Problem(await Send(admin, HttpMethod.Post, path, new { belgeTuru = "Yok", ad = "X" }), HttpStatusCode.BadRequest, "dogrulama", "belgeTuru");
        await Problem(await Send(admin, HttpMethod.Post, path, new { belgeTuru = "KiraSozlesmesi", ad = "Uzun", altBilgi = new string('a', 513) }), HttpStatusCode.BadRequest, "dogrulama", "altBilgi");

        var s1 = VersionOf(await Json(await admin.C.GetAsync($"{path}/{aId}")));
        var upd = await Json(await Send(admin, HttpMethod.Put, $"{path}/{aId}", new { belgeTuru = "KiraSozlesmesi", ad = "Standart v2", imzaAlaniGoster = false, surum = s1 }));
        Assert.False(upd.GetProperty("imzaAlaniGoster").GetBoolean());
        await Problem(await Send(admin, HttpMethod.Put, $"{path}/{aId}", new { belgeTuru = "KiraSozlesmesi", ad = "Bayat", surum = s1 }), HttpStatusCode.Conflict, "cakisma");
        Assert.Equal(2, (await Json(await admin.C.GetAsync($"{path}?tur=KiraSozlesmesi"))).GetProperty("toplam").GetInt32());

        // ManageUsers olmayan Yönetici → 403 (Blazor uç + servis paritesi)
        var manager = await _kit.LoginAsync(e, Who.Manager);
        await Problem(await manager.C.GetAsync(path), HttpStatusCode.Forbidden, "yetki_yok");
        var other = await _kit.SetupAsync();
        var otherAdmin = await _kit.LoginAsync(other, Who.Admin);
        await Problem(await otherAdmin.C.GetAsync($"{path}/{aId}"), HttpStatusCode.NotFound, null);
    }

    [Fact]
    public async Task Reservation_source_reflect_rates_copies_to_active_only()
    {
        var e = await _kit.SetupAsync();
        var src = new ReservationSource { Kod = Code("S"), Ad = "Kaynak", Aktif = true, KiraOrani = 12.5m, HizmetOrani = 5m, DropOrani = null };
        var active = new ReservationSource { Kod = Code("A"), Ad = "Aktif", Aktif = true, KiraOrani = 1m, HizmetOrani = 1m, DropOrani = 1m };
        var passive = new ReservationSource { Kod = Code("P"), Ad = "Pasif", Aktif = false, KiraOrani = 2m, HizmetOrani = 2m, DropOrani = 2m };
        await _kit.WriteAsync(e.TenantId, db => db.AddRange(src, active, passive));

        var op = await _kit.LoginAsync(e, Who.OperatorA);
        var r = await Json(await Send(op, HttpMethod.Post, $"{V1}/rezervasyon-kaynaklari/{src.Id}/yansit"));
        Assert.Equal(1, r.GetProperty("guncellenen").GetInt32());
        var rows = await _kit.ReadAsync(e.TenantId, db => db.ReservationSources.AsNoTracking().ToListAsync());
        var a = rows.Single(x => x.Id == active.Id);
        Assert.Equal((12.5m, 5m, (decimal?)null), (a.KiraOrani!.Value, a.HizmetOrani!.Value, a.DropOrani));
        var p = rows.Single(x => x.Id == passive.Id);
        Assert.Equal(2m, p.KiraOrani);

        var acc = await _kit.LoginAsync(e, Who.Accounting);
        await Problem(await Send(acc, HttpMethod.Post, $"{V1}/rezervasyon-kaynaklari/{src.Id}/yansit"), HttpStatusCode.Forbidden, "yetki_yok");
        var other = await _kit.SetupAsync();
        var otherAdmin = await _kit.LoginAsync(other, Who.Admin);
        await Problem(await Send(otherAdmin, HttpMethod.Post, $"{V1}/rezervasyon-kaynaklari/{src.Id}/yansit"), HttpStatusCode.NotFound, null);
    }
}
