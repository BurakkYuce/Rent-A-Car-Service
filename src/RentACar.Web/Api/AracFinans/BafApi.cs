using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Baflar;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.AracFinans;

/// <summary>
/// <c>/api/ui/v1/baflar/*</c> (F6.1b) — BAF: personele araç tahsisi (<see cref="BafService"/>). DEFTERE YAZMAZ.
/// <para><b>İzin:</b> OperationsWrite (Blazor grubu); iptal OperationsDelete. <b>Kapsam:</b> çıkış şubesi (<c>Sube</c>
/// metni, servis kuralı) — liste servisçe süzülür, tekil uçlar durumdan ÖNCE 403. Oluşturmada araç kapsamda olmalı
/// ve şubeye bağlı kullanıcı yalnız KENDİ şubesine tahsis yazabilir.</para>
/// <para><b>Çift gönderim:</b> <c>Idempotency-Key</c> verilirse Id = anahtar (ikinci oluşturma 409 <c>mukerrer</c>);
/// teslim/iptal kilit altında durum çitli (ikinci teslim 400).</para>
/// </summary>
public static class BafApi
{
    private const string Root = UiApiExtensions.V1 + "/baflar";

    public static RouteGroupBuilder MapBafApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/baflar").WithTags("BAF").RequirePermission(Permission.OperationsWrite);
        g.MapGet("", GetList).MapFields(F5Shared.SortRules);
        g.MapGet("/{id:guid}", Detail);
        g.MapPost("", Create);
        g.MapPost("/{id:guid}/teslim-al", Receive);
        g.MapPost("/{id:guid}/iptal", Cancel).RequirePermission(Permission.OperationsDelete);
        return g;
    }

    private static ProblemHttpResult NotFoundProblem() => F5Shared.NotFound("Tahsis bulunamadı.");

    private static readonly SortFieldMap<BafDto> Map = SortFieldMap<BafDto>
        .Create(b => b.Id)
        .Alan("no", b => b.No).Alan("plaka", b => b.Plaka).Alan("personel", b => b.PersonelAd)
        .Alan("cikisTarihi", b => b.CikisTarihi).Alan("donusTarihi", b => b.DonusTarihi).Alan("durum", b => b.Durum);

    private static async Task<Ok<Sayfa<BafDto>>> GetList(
        BafService svc, IDbContextFactory<AppDbContext> dbf, Guid? personelId, string? plaka, string? durum,
        string? kullanimAmaci, string? lokasyon, string? ofis, DateOnly? bas, DateOnly? bit, int? sayfa, int? boyut,
        string? sirala, CancellationToken ct)
    {
        var (min, max) = F5Shared.DayRange(bas, bit);
        var list = await svc.SearchAsync(new BafFilter // şube kapsamı serviste (filtre genişletemez)
        {
            PersonelId = personelId, Plaka = F5Shared.Nz(plaka), Durum = F5Shared.EnumAdi<BafStatus>(durum, "durum"),
            KullanimAmaci = F5Shared.EnumAdi<BafUsagePurpose>(kullanimAmaci, "kullanimAmaci"),
            Lokasyon = F5Shared.EnumAdi<BafLocation>(lokasyon, "lokasyon"), Ofis = F5Shared.Nz(ofis), Bas = min, Bit = max,
        }, ct);
        var rows = await DtosAsync(dbf, list, ct);
        return TypedResults.Ok(F5Shared.Paginate(rows, Map, sayfa, boyut, sirala));
    }

    private static async Task<List<BafDto>> DtosAsync(IDbContextFactory<AppDbContext> dbf, IReadOnlyList<Baf> list,
        CancellationToken ct)
    {
        var plates = await F5Shared.PlatesAsync(dbf, list.Select(b => b.VehicleId), ct);
        var pids = list.Select(b => b.PersonelId).Concat(list.Where(b => b.Onaylayan is not null).Select(b => b.Onaylayan!.Value))
            .Distinct().ToList();
        await using var db = await dbf.CreateDbContextAsync(ct);
        var names = await db.Personeller.AsNoTracking().Where(p => pids.Contains(p.Id))
            .Select(p => new { p.Id, Ad = p.Ad + " " + p.Soyad }).ToDictionaryAsync(p => p.Id, p => p.Ad, ct);
        return list.Select(b => BafDto.From(b, F5Shared.Plate(plates, b.VehicleId), names.GetValueOrDefault(b.PersonelId, "—"),
            b.Onaylayan is { } o ? names.GetValueOrDefault(o) : null)).ToList();
    }

    private static async Task<Results<Ok<BafDto>, ProblemHttpResult>> Detail(
        Guid id, BafService svc, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
        => await DtoAsync(id, svc, dbf, ct) is { } d ? TypedResults.Ok(d) : NotFoundProblem();

    private static async Task<BafDto?> DtoAsync(Guid id, BafService svc, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
        => await svc.GetAsync(id, ct) is { } b ? (await DtosAsync(dbf, [b], ct))[0] : null; // kapsam dışı → 403

    private static async Task<Results<Created<BafDto>, ProblemHttpResult>> Create(
        BafIstegi i, HttpContext http, BafService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici,
        CancellationToken ct)
    {
        var key = IdempotencyHeader.Key(http);
        if (key is { } a && await svc.GetAsync(a, ct) is { } m) // (1) ÖNCE mevcut kayıt
            throw new DuplicateOperationException($"Bu tahsis zaten kaydedildi (No {m.No}); yeni kayıt yazılmadı.",
                new MevcutIslem(m.Id, m.No, 0m, VehicleFinanceShared.BaseCurrency,
                    m.VehicleId == i.VehicleId && m.PersonelId == i.PersonelId && m.CikisKm == i.CikisKm));
        if (i.CikisKm is < 0 or > 10_000_000) throw new ValidationException("Çıkış KM 0 ile 10.000.000 arasında olmalıdır.", "cikisKm");
        if (i.CikisYakit is { } cy && !FuelScale.IsValid(cy))
            throw new ValidationException($"Çıkış yakıtı 0 ile {FuelScale.Max} arasında olmalıdır.", "cikisYakit");
        VehicleFinanceShared.Text(i.Sube, 100, "sube");
        VehicleFinanceShared.Text(i.Aciklama, 512, "aciklama");
        await VehicleFinanceShared.VehicleWriteAsync(dbf, kullanici, i.VehicleId, "vehicleId", required: true, ct);
        var branch = VehicleFinanceShared.Nz(i.Sube);
        if (!BranchScope.EffectiveFilter(kullanici).Unrestricted)
        {
            if (branch is null) throw new ValidationException("Şube zorunludur (şubeye bağlı kullanıcı kendi şubesini seçmelidir).", "sube");
            BranchScope.RequireInScope(kullanici, branch); // başka şubeye tahsis yazılamaz (kendisi de göremezdi)
        }
        await StaffExistsAsync(dbf, i.PersonelId, "personelId", ct);
        if (i.Onaylayan is { } on && on != Guid.Empty) await StaffExistsAsync(dbf, on, "onaylayan", ct);
        var input = new BafInput
        {
            PersonelId = i.PersonelId, VehicleId = i.VehicleId, CikisTarihi = F5Shared.Utc(i.CikisTarihi), CikisKm = i.CikisKm,
            CikisYakit = i.CikisYakit, Sube = branch, Aciklama = VehicleFinanceShared.Nz(i.Aciklama),
            KullanimAmaci = F5Shared.EnumAdi<BafUsagePurpose>(i.KullanimAmaci, "kullanimAmaci"), Onaylayan = i.Onaylayan,
            KirayaVer = i.KirayaVer, CikisSaat = i.CikisSaat, IslemAnahtari = key,
        };
        var id = await svc.CreateAsync(input, ct);
        var d = await DtoAsync(id, svc, dbf, ct);
        return TypedResults.Created($"{Root}/{id}", d!);
    }

    private static async Task StaffExistsAsync(IDbContextFactory<AppDbContext> dbf, Guid id, string alan, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new ValidationException("Personel seçilmelidir.", alan);
        await using var db = await dbf.CreateDbContextAsync(ct);
        if (!await db.Personeller.AsNoTracking().AnyAsync(p => p.Id == id, ct))
            throw new ValidationException("Personel bulunamadı.", alan);
    }

    private static async Task<Results<Ok<BafDto>, ProblemHttpResult>> Receive(
        Guid id, BafTeslimIstegi i, BafService svc, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await svc.GetAsync(id, ct) is null) return NotFoundProblem(); // kapsam durumdan ÖNCE (403)
        if (i.DonusKm is < 0 or > 10_000_000) throw new ValidationException("Dönüş KM 0 ile 10.000.000 arasında olmalıdır.", "donusKm");
        if (i.DonusYakit is { } dy && !FuelScale.IsValid(dy))
            throw new ValidationException($"Dönüş yakıtı 0 ile {FuelScale.Max} arasında olmalıdır.", "donusYakit");
        VehicleFinanceShared.Text(i.DonusSube, 100, "donusSube");
        try
        {
            if (!await svc.ReceiveLockedAsync(id, i.DonusKm, i.DonusYakit, F5Shared.Utc(i.DonusTarihi), i.DonusSube,
                    i.DonusSaat, ct)) return NotFoundProblem();
        }
        catch (ValidationException ex) when (ex.GetType() == typeof(ValidationException) && ex.Message.StartsWith("Dönüş KM", StringComparison.Ordinal))
        { throw new ValidationException(ex.Message, "donusKm"); }
        return await DtoAsync(id, svc, dbf, ct) is { } d ? TypedResults.Ok(d) : NotFoundProblem();
    }

    private static async Task<Results<Ok<BafDto>, ProblemHttpResult>> Cancel(
        Guid id, BafService svc, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await svc.GetAsync(id, ct) is null) return NotFoundProblem();
        if (!await svc.IsCancelLockedAsync(id, ct)) return NotFoundProblem();
        return await DtoAsync(id, svc, dbf, ct) is { } d ? TypedResults.Ok(d) : NotFoundProblem();
    }
}
