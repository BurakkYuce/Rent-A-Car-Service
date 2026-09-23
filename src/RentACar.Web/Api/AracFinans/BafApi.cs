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
    private const string Kok = UiApiExtensions.V1 + "/baflar";

    public static RouteGroupBuilder MapBafApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/baflar").WithTags("BAF").RequirePermission(Permission.OperationsWrite);
        g.MapGet("", Liste).AlanlariEsle(F5Ortak.SiralamaKurallari);
        g.MapGet("/{id:guid}", Detay);
        g.MapPost("", Olustur);
        g.MapPost("/{id:guid}/teslim-al", TeslimAl);
        g.MapPost("/{id:guid}/iptal", Iptal).RequirePermission(Permission.OperationsDelete);
        return g;
    }

    private static ProblemHttpResult Bulunamadi() => F5Ortak.Bulunamadi("Tahsis bulunamadı.");

    private static readonly SiralamaHaritasi<BafDto> Harita = SiralamaHaritasi<BafDto>
        .Olustur(b => b.Id)
        .Alan("no", b => b.No).Alan("plaka", b => b.Plaka).Alan("personel", b => b.PersonelAd)
        .Alan("cikisTarihi", b => b.CikisTarihi).Alan("donusTarihi", b => b.DonusTarihi).Alan("durum", b => b.Durum);

    private static async Task<Ok<Sayfa<BafDto>>> Liste(
        BafService svc, IDbContextFactory<AppDbContext> dbf, Guid? personelId, string? plaka, string? durum,
        string? kullanimAmaci, string? lokasyon, string? ofis, DateOnly? bas, DateOnly? bit, int? sayfa, int? boyut,
        string? sirala, CancellationToken ct)
    {
        var (min, max) = F5Ortak.GunAraligi(bas, bit);
        var liste = await svc.SearchAsync(new BafFilter // şube kapsamı serviste (filtre genişletemez)
        {
            PersonelId = personelId, Plaka = F5Ortak.Nz(plaka), Durum = F5Ortak.EnumAdi<BafDurum>(durum, "durum"),
            KullanimAmaci = F5Ortak.EnumAdi<BafKullanimAmaci>(kullanimAmaci, "kullanimAmaci"),
            Lokasyon = F5Ortak.EnumAdi<BafLokasyon>(lokasyon, "lokasyon"), Ofis = F5Ortak.Nz(ofis), Bas = min, Bit = max,
        }, ct);
        var satirlar = await DtolarAsync(dbf, liste, ct);
        return TypedResults.Ok(F5Ortak.Sayfala(satirlar, Harita, sayfa, boyut, sirala));
    }

    private static async Task<List<BafDto>> DtolarAsync(IDbContextFactory<AppDbContext> dbf, IReadOnlyList<Baf> liste,
        CancellationToken ct)
    {
        var plakalar = await F5Ortak.PlakalarAsync(dbf, liste.Select(b => b.VehicleId), ct);
        var pids = liste.Select(b => b.PersonelId).Concat(liste.Where(b => b.Onaylayan is not null).Select(b => b.Onaylayan!.Value))
            .Distinct().ToList();
        await using var db = await dbf.CreateDbContextAsync(ct);
        var adlar = await db.Personeller.AsNoTracking().Where(p => pids.Contains(p.Id))
            .Select(p => new { p.Id, Ad = p.Ad + " " + p.Soyad }).ToDictionaryAsync(p => p.Id, p => p.Ad, ct);
        return liste.Select(b => BafDto.From(b, F5Ortak.Plaka(plakalar, b.VehicleId), adlar.GetValueOrDefault(b.PersonelId, "—"),
            b.Onaylayan is { } o ? adlar.GetValueOrDefault(o) : null)).ToList();
    }

    private static async Task<Results<Ok<BafDto>, ProblemHttpResult>> Detay(
        Guid id, BafService svc, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
        => await DtoAsync(id, svc, dbf, ct) is { } d ? TypedResults.Ok(d) : Bulunamadi();

    private static async Task<BafDto?> DtoAsync(Guid id, BafService svc, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
        => await svc.GetAsync(id, ct) is { } b ? (await DtolarAsync(dbf, [b], ct))[0] : null; // kapsam dışı → 403

    private static async Task<Results<Created<BafDto>, ProblemHttpResult>> Olustur(
        BafIstegi i, HttpContext http, BafService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici,
        CancellationToken ct)
    {
        var anahtar = IdempotencyBasligi.Anahtar(http);
        if (anahtar is { } a && await svc.GetAsync(a, ct) is { } m) // (1) ÖNCE mevcut kayıt
            throw new MukerrerIslemException($"Bu tahsis zaten kaydedildi (No {m.No}); yeni kayıt yazılmadı.",
                new MevcutIslem(m.Id, m.No, 0m, AracFinansOrtak.TemelDoviz,
                    m.VehicleId == i.VehicleId && m.PersonelId == i.PersonelId && m.CikisKm == i.CikisKm));
        if (i.CikisKm is < 0 or > 10_000_000) throw new ValidationException("Çıkış KM 0 ile 10.000.000 arasında olmalıdır.", "cikisKm");
        if (i.CikisYakit is < 0 or > 100) throw new ValidationException("Çıkış yakıtı 0 ile 100 arasında olmalıdır.", "cikisYakit");
        AracFinansOrtak.Metin(i.Sube, 100, "sube");
        AracFinansOrtak.Metin(i.Aciklama, 512, "aciklama");
        await AracFinansOrtak.AracYazimAsync(dbf, kullanici, i.VehicleId, "vehicleId", zorunlu: true, ct);
        var sube = AracFinansOrtak.Nz(i.Sube);
        if (!BranchScope.EffectiveFilter(kullanici).Unrestricted)
        {
            if (sube is null) throw new ValidationException("Şube zorunludur (şubeye bağlı kullanıcı kendi şubesini seçmelidir).", "sube");
            BranchScope.RequireInScope(kullanici, sube); // başka şubeye tahsis yazılamaz (kendisi de göremezdi)
        }
        await PersonelVarAsync(dbf, i.PersonelId, "personelId", ct);
        if (i.Onaylayan is { } on && on != Guid.Empty) await PersonelVarAsync(dbf, on, "onaylayan", ct);
        var input = new BafInput
        {
            PersonelId = i.PersonelId, VehicleId = i.VehicleId, CikisTarihi = F5Ortak.Utc(i.CikisTarihi), CikisKm = i.CikisKm,
            CikisYakit = i.CikisYakit, Sube = sube, Aciklama = AracFinansOrtak.Nz(i.Aciklama),
            KullanimAmaci = F5Ortak.EnumAdi<BafKullanimAmaci>(i.KullanimAmaci, "kullanimAmaci"), Onaylayan = i.Onaylayan,
            KirayaVer = i.KirayaVer, CikisSaat = i.CikisSaat, IslemAnahtari = anahtar,
        };
        var id = await svc.CreateAsync(input, ct);
        var d = await DtoAsync(id, svc, dbf, ct);
        return TypedResults.Created($"{Kok}/{id}", d!);
    }

    private static async Task PersonelVarAsync(IDbContextFactory<AppDbContext> dbf, Guid id, string alan, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new ValidationException("Personel seçilmelidir.", alan);
        await using var db = await dbf.CreateDbContextAsync(ct);
        if (!await db.Personeller.AsNoTracking().AnyAsync(p => p.Id == id, ct))
            throw new ValidationException("Personel bulunamadı.", alan);
    }

    private static async Task<Results<Ok<BafDto>, ProblemHttpResult>> TeslimAl(
        Guid id, BafTeslimIstegi i, BafService svc, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await svc.GetAsync(id, ct) is null) return Bulunamadi(); // kapsam durumdan ÖNCE (403)
        if (i.DonusKm is < 0 or > 10_000_000) throw new ValidationException("Dönüş KM 0 ile 10.000.000 arasında olmalıdır.", "donusKm");
        if (i.DonusYakit is < 0 or > 100) throw new ValidationException("Dönüş yakıtı 0 ile 100 arasında olmalıdır.", "donusYakit");
        AracFinansOrtak.Metin(i.DonusSube, 100, "donusSube");
        try
        {
            if (!await svc.TeslimAlKilitliAsync(id, i.DonusKm, i.DonusYakit, F5Ortak.Utc(i.DonusTarihi), i.DonusSube,
                    i.DonusSaat, ct)) return Bulunamadi();
        }
        catch (ValidationException ex) when (ex.GetType() == typeof(ValidationException) && ex.Message.StartsWith("Dönüş KM", StringComparison.Ordinal))
        { throw new ValidationException(ex.Message, "donusKm"); }
        return await DtoAsync(id, svc, dbf, ct) is { } d ? TypedResults.Ok(d) : Bulunamadi();
    }

    private static async Task<Results<Ok<BafDto>, ProblemHttpResult>> Iptal(
        Guid id, BafService svc, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await svc.GetAsync(id, ct) is null) return Bulunamadi();
        if (!await svc.IptalKilitliAsync(id, ct)) return Bulunamadi();
        return await DtoAsync(id, svc, dbf, ct) is { } d ? TypedResults.Ok(d) : Bulunamadi();
    }
}
