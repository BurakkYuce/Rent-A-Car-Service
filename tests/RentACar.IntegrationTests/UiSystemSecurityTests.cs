using System.Net;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

/// <summary>
/// F11.1b bağımsız güvenlik incelemesi (#288) bulgularının kalıcı probe'ları. Beklenen değerler elle kurulmuş
/// senaryodan (şube/ofis/kullanıcı adları test içinde sabitlenir).
/// </summary>
[Collection("web")]
public sealed partial class UiSystemSecurityTests(WebFixture fx)
{
    private readonly SystemApiTestKit _kit = new(fx);

    private async Task<(Guid A, Guid B)> BranchesAsync(Env e)
    {
        var a = new Branch { Kod = "SA", Ad = "SubeA", Aktif = true };
        var b = new Branch { Kod = "SB", Ad = "SubeB", Aktif = true };
        await _kit.WriteAsync(e.TenantId, db => { db.Branches.Add(a); db.Branches.Add(b); });
        return (a.Id, b.Id);
    }

    // H1 — ofis adı çakışmasıyla başka şubenin ofisini ele geçirme
    [Fact]
    public async Task Office_name_collision_is_rejected_and_other_branch_records_stay()
    {
        var e = await _kit.SetupAsync();
        var (_, branchB) = await BranchesAsync(e);
        var admin = await _kit.LoginAsync(e, Who.Admin);
        await Json(await Send(admin, HttpMethod.Post, V1 + "/lokasyonlar", new { kod = "ZZZ", ad = "Otogar B", sube = "SubeB", aktif = true }),
            HttpStatusCode.Created);
        var res = new Reservation { ReservationNo = Random("RZ"), CikisOfisi = "Otogar B", Durum = ReservationStatus.Rezerv };
        await _kit.WriteAsync(e.TenantId, db => db.Reservations.Add(res));
        Assert.Equal(branchB, await _kit.ReadAsync(e.TenantId, db => db.Reservations.AsNoTracking().Where(r => r.Id == res.Id).Select(r => r.CikisSubeId).FirstAsync()));

        var op = await _kit.LoginAsync(e, Who.OperatorA);
        await Problem(await Send(op, HttpMethod.Post, V1 + "/lokasyonlar", new { kod = "AAA", ad = "otogar b ", sube = "SubeA", aktif = true }),
            HttpStatusCode.BadRequest, "dogrulama", "ad");
        Assert.Equal(1, await _kit.ReadAsync(e.TenantId, db => db.Locations.AsNoTracking().CountAsync()));

        // Rezervasyon yeniden kaydedilince türetilmiş şube B'de kalır.
        await _kit.WriteAsync(e.TenantId, db =>
        {
            var r = db.Reservations.First(x => x.Id == res.Id);
            r.Aciklama = "dokunuldu";
        });
        Assert.Equal(branchB, await _kit.ReadAsync(e.TenantId, db => db.Reservations.AsNoTracking().Where(r => r.Id == res.Id).Select(r => r.CikisSubeId).FirstAsync()));
    }

    [Fact]
    public async Task Office_rename_into_existing_name_is_rejected()
    {
        var e = await _kit.SetupAsync();
        await BranchesAsync(e);
        var admin = await _kit.LoginAsync(e, Who.Admin);
        await Json(await Send(admin, HttpMethod.Post, V1 + "/lokasyonlar", new { kod = "ZZZ", ad = "Otogar B", sube = "SubeB", aktif = true }), HttpStatusCode.Created);
        var mine = await Json(await Send(admin, HttpMethod.Post, V1 + "/lokasyonlar", new { kod = "AAA", ad = "Merkez A", sube = "SubeA", aktif = true }), HttpStatusCode.Created);
        var id = mine.GetProperty("id").GetGuid();
        var surum = (await Json(await admin.C.GetAsync($"{V1}/lokasyonlar/{id}"))).GetProperty("surum").GetString();
        await Problem(await Send(admin, HttpMethod.Put, $"{V1}/lokasyonlar/{id}", new { kod = "AAA", ad = " OTOGAR B", sube = "SubeA", aktif = true, surum }),
            HttpStatusCode.BadRequest, "dogrulama", "ad");
    }

    [Fact]
    public async Task Historical_duplicate_office_names_resolve_to_no_branch()
    {
        var e = await _kit.SetupAsync();
        var (branchA, branchB) = await BranchesAsync(e);
        // Tarihsel veri: servisi atlayarak iki şubede aynı ad (kural öncesi kayıt).
        await _kit.WriteAsync(e.TenantId, db =>
        {
            db.Locations.Add(new Location { Kod = "AAA", Ad = "otogar b", Sube = "SubeA", SubeId = branchA });
            db.Locations.Add(new Location { Kod = "ZZZ", Ad = "Otogar B", Sube = "SubeB", SubeId = branchB });
        });
        var res = new Reservation { ReservationNo = Random("RZ"), CikisOfisi = "Otogar B", Durum = ReservationStatus.Rezerv };
        await _kit.WriteAsync(e.TenantId, db => db.Reservations.Add(res));
        Assert.Null(await _kit.ReadAsync(e.TenantId, db => db.Reservations.AsNoTracking().Where(r => r.Id == res.Id).Select(r => r.CikisSubeId).FirstAsync()));
    }
}
