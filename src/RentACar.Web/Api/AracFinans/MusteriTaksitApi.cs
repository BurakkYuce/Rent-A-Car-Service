using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.MusteriTaksitleri;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.AracFinans;

/// <summary>
/// <c>/api/ui/v1/musteri-taksitleri/*</c> (F6.1b) — müşteri taksit TAKİBİ (<see cref="MusteriTaksitService"/>).
/// DEFTERE YAZMAZ: "ödendi" takip bayrağıdır, cari bakiyeyi kasa/banka tahsilatı değiştirir.
/// <para><b>İzin:</b> okuma FinanceWrite ∨ ViewReports, yazma FinanceWrite (Blazor grubu). <b>Kapsam:</b> taksit ARACIN
/// şubesinden geçer; araçsız taksit yalnız şube kısıtsız kullanıcıya görünür.</para>
/// <para><b>Çift gönderim:</b> oluşturma ve plan <c>Idempotency-Key</c> ister (kimlik = anahtar / anahtardan türetilmiş;
/// ikinci gönderim 409 <c>mukerrer</c> + <c>mevcut</c>). "Ödendi" kilit altında: ödenmiş taksidin yeniden işaretlenmesi
/// 409 <c>mukerrer</c> (tarih sessizce ezilmez). PUT tam değiştirmedir: zorunlu <c>surum</c>, bayat → 409 <c>cakisma</c>.</para>
/// </summary>
public static partial class MusteriTaksitApi
{
    private const string Kok = UiApiExtensions.V1 + "/musteri-taksitleri";

    public static RouteGroupBuilder MapMusteriTaksitApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/musteri-taksitleri").WithTags("Müşteri Taksit");
        g.MapGet("", Liste).AlanlariEsle(F5Ortak.SiralamaKurallari)
            .RequireAnyPermission(Permission.FinanceWrite, Permission.ViewReports);
        g.MapGet("/ozet", Ozet).RequireAnyPermission(Permission.FinanceWrite, Permission.ViewReports);
        g.MapGet("/{id:guid}", Detay).RequireAnyPermission(Permission.FinanceWrite, Permission.ViewReports);
        g.MapPost("", Olustur).RequirePermission(Permission.FinanceWrite)
            .Produces<UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        g.MapPost("/plan", Plan).RequirePermission(Permission.FinanceWrite)
            .Produces<UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        g.MapPut("/{id:guid}", Guncelle).RequirePermission(Permission.FinanceWrite);
        g.MapPost("/{id:guid}/odendi", Odendi).RequirePermission(Permission.FinanceWrite)
            .Produces<UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        g.MapPost("/{id:guid}/geri-al", GeriAl).RequirePermission(Permission.FinanceWrite);
        g.MapDelete("/{id:guid}", Sil).RequirePermission(Permission.FinanceWrite);
        return g;
    }

    private static ProblemHttpResult Bulunamadi() => F5Ortak.Bulunamadi("Taksit bulunamadı.");

    private static readonly SiralamaHaritasi<MusteriTaksitSatiri> Harita = SiralamaHaritasi<MusteriTaksitSatiri>
        .Olustur(t => t.Id)
        .Alan("vade", t => t.Vade).Alan("sira", t => t.Sira).Alan("cari", t => t.CariAd).Alan("plaka", t => t.Plaka)
        .Alan("taksitTutari", t => t.TaksitTutari).Alan("tutarBaz", t => t.TutarBaz).Alan("durum", t => t.Durum);

    private static async Task<List<MusteriTaksit>> KumeAsync(MusteriTaksitService svc, IDbContextFactory<AppDbContext> dbf,
        ICurrentUser kullanici, Guid? cariId, Guid? vehicleId, string? durum, DateOnly? vadeMin, DateOnly? vadeMax,
        bool? gecikmis, CancellationToken ct)
    {
        var (min, max) = F5Ortak.GunAraligi(vadeMin, vadeMax);
        var liste = await svc.SearchAsync(new MusteriTaksitFilter
        {
            CariId = cariId, VehicleId = vehicleId, Durum = F5Ortak.EnumAdi<TaksitDurum>(durum, "durum"),
            VadeMin = min, VadeMax = max, SadeceGecikmis = gecikmis,
        }, ct);
        var f = BranchScope.EffectiveFilter(kullanici);
        if (f.Unrestricted) return liste.ToList();
        var subeler = await AracFinansOrtak.AracSubeleriAsync(dbf,
            liste.Where(t => t.VehicleId is not null).Select(t => t.VehicleId!.Value), ct);
        return liste.Where(t => AracFinansOrtak.Gorunur(f, t.VehicleId, subeler)).ToList();
    }

    private static async Task<Ok<Sayfa<MusteriTaksitSatiri>>> Liste(
        MusteriTaksitService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, Guid? cariId,
        Guid? vehicleId, string? durum, DateOnly? vadeMin, DateOnly? vadeMax, bool? gecikmis, int? sayfa, int? boyut,
        string? sirala, CancellationToken ct)
    {
        var liste = await KumeAsync(svc, dbf, kullanici, cariId, vehicleId, durum, vadeMin, vadeMax, gecikmis, ct);
        var cariler = await F5Ortak.CarilerAsync(dbf, liste.Select(t => t.CariId), ct);
        var plakalar = await F5Ortak.PlakalarAsync(dbf, liste.Where(t => t.VehicleId is not null).Select(t => t.VehicleId!.Value), ct);
        var satirlar = liste.Select(t => MusteriTaksitSatiri.From(t, F5Ortak.CariAdi(cariler, t.CariId),
            t.VehicleId is { } v ? F5Ortak.Plaka(plakalar, v) : null)).ToList();
        return TypedResults.Ok(F5Ortak.Sayfala(satirlar, Harita, sayfa, boyut, sirala));
    }

    /// <summary>Özet (tutarlar BAZ para — karışık dövizde toplanabilsin). Filtreli + kapsamlı küme.</summary>
    private static async Task<Ok<TaksitOzet>> Ozet(
        MusteriTaksitService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, Guid? cariId,
        Guid? vehicleId, string? durum, DateOnly? vadeMin, DateOnly? vadeMax, bool? gecikmis, CancellationToken ct)
        => TypedResults.Ok(MusteriTaksitService.Ozet(
            await KumeAsync(svc, dbf, kullanici, cariId, vehicleId, durum, vadeMin, vadeMax, gecikmis, ct)));

    private static async Task<MusteriTaksit?> KapsamliAsync(Guid id, MusteriTaksitService svc,
        IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, CancellationToken ct)
    {
        var t = await svc.GetAsync(id, ct);
        if (t is null) return null;
        await AracFinansOrtak.KayitKapsamiAsync(dbf, kullanici, t.VehicleId, ct); // durumdan ÖNCE (403)
        return t;
    }

    private static async Task<MusteriTaksitSatiri?> DtoAsync(Guid id, MusteriTaksitService svc,
        IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, CancellationToken ct)
    {
        var surum = await svc.SurumAsync(id, ct); // alanlardan ÖNCE
        var t = await KapsamliAsync(id, svc, dbf, kullanici, ct);
        if (t is null) return null;
        var cari = F5Ortak.CariAdi(await F5Ortak.CarilerAsync(dbf, [t.CariId], ct), t.CariId);
        var plaka = t.VehicleId is { } v ? F5Ortak.Plaka(await F5Ortak.PlakalarAsync(dbf, [v], ct), v) : null;
        return MusteriTaksitSatiri.From(t, cari, plaka, surum);
    }

    private static async Task<Results<Ok<MusteriTaksitSatiri>, ProblemHttpResult>> Detay(
        Guid id, MusteriTaksitService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, CancellationToken ct)
        => await DtoAsync(id, svc, dbf, kullanici, ct) is { } d ? TypedResults.Ok(d) : Bulunamadi();

    private static async Task<Results<NoContent, ProblemHttpResult>> Sil(
        Guid id, MusteriTaksitService svc, IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, CancellationToken ct)
    {
        if (await KapsamliAsync(id, svc, dbf, kullanici, ct) is null) return Bulunamadi();
        return await svc.DeleteAsync(id, ct) ? TypedResults.NoContent() : Bulunamadi();
    }
}
