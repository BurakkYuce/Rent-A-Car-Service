using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.DamageFiles;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.AracFinans;

/// <summary>
/// <c>/api/ui/v1/hasar-dosyalari/*</c> (F6.1b) — hasar dosyası onay akışı (<see cref="DamageFileService"/>):
/// Açık → Onayda → Onaylandı/Reddedildi → Kapalı. MALİ BELGE DEĞİL; tahmini tutar bilgi.
/// <para><b>İzin:</b> OperationsWrite (Blazor grubu). <b>Kapsam:</b> dosya ARACIN şubesinden geçer (servis kapsam
/// uygulamıyor — kapı burası); tekil uçlarda durumdan ÖNCE 403. Geçişler satır kilidi altında (onayla + reddet yarışı
/// tek kazanır). Oluşturmada kira verilirse aynı araca ait ve kapsamda olmalı.</para>
/// </summary>
public static class HasarApi
{
    private const string Kok = UiApiExtensions.V1 + "/hasar-dosyalari";

    public static RouteGroupBuilder MapHasarApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/hasar-dosyalari").WithTags("Hasar Dosyası").RequirePermission(Permission.OperationsWrite);
        g.MapGet("", Liste).AlanlariEsle(F5Ortak.SiralamaKurallari);
        g.MapGet("/{id:guid}", Detay);
        g.MapPost("", Olustur);
        g.MapPost("/{id:guid}/onaya-gonder", (Guid id, DamageFileService s, IDbContextFactory<AppDbContext> d, ICurrentUser u, CancellationToken ct)
            => Gecis(id, s, d, u, x => s.OnayaGonderAsync(id, ct), ct));
        g.MapPost("/{id:guid}/onayla", (Guid id, HasarNotIstegi? i, DamageFileService s, IDbContextFactory<AppDbContext> d, ICurrentUser u, CancellationToken ct)
            => Gecis(id, s, d, u, x => s.OnaylaAsync(id, Not(i), ct), ct));
        g.MapPost("/{id:guid}/reddet", (Guid id, HasarNotIstegi? i, DamageFileService s, IDbContextFactory<AppDbContext> d, ICurrentUser u, CancellationToken ct)
            => Gecis(id, s, d, u, x => s.ReddetAsync(id, Not(i), ct), ct));
        g.MapPost("/{id:guid}/kapat", (Guid id, DamageFileService s, IDbContextFactory<AppDbContext> d, ICurrentUser u, CancellationToken ct)
            => Gecis(id, s, d, u, x => s.KapatAsync(id, ct), ct));
        return g;
    }

    private static ProblemHttpResult Bulunamadi() => F5Ortak.Bulunamadi("Hasar dosyası bulunamadı.");

    private static string? Not(HasarNotIstegi? i)
    {
        AracFinansOrtak.Metin(i?.Not, 512, "not");
        return AracFinansOrtak.Nz(i?.Not);
    }

    private static readonly SiralamaHaritasi<HasarDto> Harita = SiralamaHaritasi<HasarDto>
        .Olustur(f => f.Id)
        .Alan("no", f => f.No).Alan("plaka", f => f.Plaka).Alan("acilisTarihi", f => f.AcilisTarihi)
        .Alan("tahminiTutar", f => f.TahminiTutar).Alan("durum", f => f.Durum);

    private static async Task<Ok<Sayfa<HasarDto>>> Liste(
        DamageFileService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, string? durum,
        Guid? vehicleId, int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        var d = F5Ortak.EnumAdi<HasarDurum>(durum, "durum");
        var liste = (await svc.ListAsync(ct)).Where(f => (d is null || f.Durum == d) && (vehicleId is null || f.VehicleId == vehicleId)).ToList();
        var fl = BranchScope.EffectiveFilter(kullanici);
        if (!fl.Unrestricted)
        {
            var subeler = await AracFinansOrtak.AracSubeleriAsync(dbf, liste.Select(f => f.VehicleId), ct);
            liste = liste.Where(f => AracFinansOrtak.Gorunur(fl, f.VehicleId, subeler)).ToList();
        }
        return TypedResults.Ok(F5Ortak.Sayfala(await DtolarAsync(dbf, liste, ct), Harita, sayfa, boyut, sirala));
    }

    private static async Task<List<HasarDto>> DtolarAsync(IDbContextFactory<AppDbContext> dbf, IReadOnlyList<DamageFile> l,
        CancellationToken ct)
    {
        var plakalar = await F5Ortak.PlakalarAsync(dbf, l.Select(f => f.VehicleId), ct);
        var cariler = await F5Ortak.CarilerAsync(dbf, l.Where(f => f.CariId is not null).Select(f => f.CariId!.Value), ct);
        return l.Select(f => HasarDto.From(f, F5Ortak.Plaka(plakalar, f.VehicleId),
            f.CariId is { } c ? F5Ortak.CariAdi(cariler, c) : null)).ToList();
    }

    private static async Task<DamageFile?> KapsamliAsync(Guid id, DamageFileService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser kullanici, CancellationToken ct)
    {
        var f = await svc.GetAsync(id, ct);
        if (f is not null) await AracFinansOrtak.KayitKapsamiAsync(dbf, kullanici, f.VehicleId, ct); // durumdan ÖNCE
        return f;
    }

    private static async Task<Results<Ok<HasarDto>, ProblemHttpResult>> Detay(
        Guid id, DamageFileService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, CancellationToken ct)
        => await KapsamliAsync(id, svc, dbf, kullanici, ct) is { } f
            ? TypedResults.Ok((await DtolarAsync(dbf, [f], ct))[0]) : Bulunamadi();

    private static async Task<Results<Created<HasarDto>, ProblemHttpResult>> Olustur(
        HasarIstegi i, HttpContext http, DamageFileService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici,
        CancellationToken ct)
    {
        var anahtar = IdempotencyBasligi.Anahtar(http);
        if (anahtar is { } a && await svc.GetAsync(a, ct) is { } m) // (1) ÖNCE mevcut kayıt
            throw new MukerrerIslemException($"Bu hasar dosyası zaten kaydedildi (No {m.No}); yeni kayıt yazılmadı.",
                new MevcutIslem(m.Id, m.No, m.TahminiTutar ?? 0m, AracFinansOrtak.TemelDoviz,
                    m.VehicleId == i.VehicleId && m.TahminiTutar == i.TahminiTutar));
        AracFinansOrtak.BilgiTutari(i.TahminiTutar, "tahminiTutar");
        AracFinansOrtak.Metin(i.Aciklama, 1024, "aciklama");
        await AracFinansOrtak.AracYazimAsync(dbf, kullanici, i.VehicleId, "vehicleId", zorunlu: true, ct);
        await AracFinansOrtak.CariVarAsync(dbf, i.CariId, "cariId", zorunlu: false, ct);
        if (i.RentalId is { } r && r != Guid.Empty)
        {
            await using var db = await dbf.CreateDbContextAsync(ct);
            var kiraArac = await db.Rentals.AsNoTracking().Where(k => k.Id == r).Select(k => (Guid?)k.VehicleId).FirstOrDefaultAsync(ct);
            if (kiraArac is null) throw new ValidationException("Kira sözleşmesi bulunamadı.", "rentalId");
            if (kiraArac != i.VehicleId) throw new ValidationException("Kira sözleşmesi seçilen araca ait değil.", "rentalId");
        }
        var id = await svc.CreateAsync(new DamageFileInput
        {
            VehicleId = i.VehicleId, RentalId = i.RentalId is { } rr && rr != Guid.Empty ? rr : null,
            CariId = i.CariId is { } cc && cc != Guid.Empty ? cc : null, AcilisTarihi = F5Ortak.Utc(i.AcilisTarihi),
            Aciklama = AracFinansOrtak.Nz(i.Aciklama), TahminiTutar = i.TahminiTutar, IslemAnahtari = anahtar,
        }, ct);
        var f = await svc.GetAsync(id, ct);
        return TypedResults.Created($"{Kok}/{id}", (await DtolarAsync(dbf, [f!], ct))[0]);
    }

    private static async Task<Results<Ok<HasarDto>, ProblemHttpResult>> Gecis(
        Guid id, DamageFileService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici,
        Func<Guid, Task<bool>> gecis, CancellationToken ct)
    {
        if (await KapsamliAsync(id, svc, dbf, kullanici, ct) is null) return Bulunamadi(); // kapsam durumdan ÖNCE
        if (!await gecis(id)) return Bulunamadi();
        return await KapsamliAsync(id, svc, dbf, kullanici, ct) is { } f
            ? TypedResults.Ok((await DtolarAsync(dbf, [f], ct))[0]) : Bulunamadi();
    }
}
