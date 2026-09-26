using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.RateMatrices;
using RentACar.Application.RentalRules;
using RentACar.Application.ServisTanimlari;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;
using S = RentACar.Web.Api.ServiceInsurance.ServiceInsuranceShared;

namespace RentACar.Web.Api.ServiceInsurance;

internal static partial class CatalogApi
{
    // ------------------------------------------------------------------ servis tanımları

    private static ServisTanimInput ServiceDefinitionInput(ServiceDefinitionRequest r) => new()
    {
        Kod = r.Kod ?? "", AracTipi = r.AracTipi ?? "", BakimKm = r.BakimKm ?? 0, Marka = r.Marka, Tip = r.Tip,
        Yakit = r.Yakit, Vites = r.Vites, Aciklama = r.Aciklama, Aktif = r.Aktif,
    };

    /// <summary>Read: every role of the Blazor page (OperationsWrite ∨ FinanceWrite ∨ ViewReports); write OperationsWrite.</summary>
    private static readonly CatalogSpec<ServiceDefinitionService, ServisTanim, ServiceDefinitionRequest, ServiceDefinitionDto> ServiceDefinitions = new()
    {
        Path = "/servis-tanimlari", Tag = "Servis", NotFoundText = "Servis tanımı bulunamadı.",
        ReadPermissions = [Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports],
        WritePermission = Permission.OperationsWrite,
        List = (s, ct) => s.ListAsync(ct), Get = (s, id, ct) => s.GetAsync(id, ct), Version = (s, id, ct) => s.GetVersionAsync(id, ct),
        ToDto = ServiceDefinitionDto.From,
        Create = (s, r, _, ct) => s.CreateAsync(ServiceDefinitionInput(r), ct),
        Update = (s, id, r, _, v, _, ct) => s.UpdateVersionedAsync(id, ServiceDefinitionInput(r), v, ct),
        Delete = (s, id, ct) => s.DeleteAsync(id, ct),
        Validate = (r, _) =>
        {
            S.Text(r.AracTipi, 100, "aracTipi"); S.Text(r.Marka, 100, "marka"); S.Text(r.Tip, 100, "tip");
            S.Text(r.Yakit, 32, "yakit"); S.Text(r.Vites, 32, "vites"); S.Text(r.Aciklama, 512, "aciklama");
            S.IntRange(r.BakimKm, 0, 10_000_000, "bakimKm");
        },
        Matches = (d, q) => Has(d.Kod, q) || Has(d.AracTipi, q) || Has(d.Marka, q) || Has(d.Tip, q),
        Sort = SortFieldMap<ServiceDefinitionDto>.Create(x => x.Id).Alan("kod", x => x.Kod).Alan("aracTipi", x => x.AracTipi)
            .Alan("bakimKm", x => x.BakimKm).Alan("marka", x => x.Marka).Alan("aktif", x => x.Aktif),
        FieldRules = [("Kod", "kod"), ("'", "kod"), ("Araç tipi", "aracTipi"), ("Bakım KM", "bakimKm")],
    };

    private static void MapServiceDefinitions(RouteGroupBuilder v1)
    {
        var g = v1.MapCatalog(ServiceDefinitions);
        // Öneri: filoda VAR ama tanımı OLMAYAN kombinasyonlar. Hiçbir şey yazmaz; kabul = normal POST.
        g.MapGet("/oneriler", async Task<Ok<IReadOnlyList<ServiceDefinitionSuggestionDto>>> (ServiceDefinitionService s, CancellationToken ct)
            => TypedResults.Ok<IReadOnlyList<ServiceDefinitionSuggestionDto>>((await s.SuggestionAsync(ct)).Select(o =>
                new ServiceDefinitionSuggestionDto(o.Kombinasyon.Marka, o.Kombinasyon.Tip, o.Kombinasyon.Yakit,
                    o.Kombinasyon.Vites, o.Kombinasyon.AracSayisi, o.Kombinasyon.Etiket, o.OnerilenKod)).ToList()))
            .RequirePermission(Permission.OperationsWrite);

        // Tarife matrisi ekranının fiyat-sorgu paneli (salt okuma; motorla aynı "yalnız onaylı + aktif" kuralı).
        v1.MapGroup("").MapGet("/tarife-matris/fiyat", async Task<Results<Ok<RateMatrixPriceDto>, ProblemHttpResult>> (
                string? kanal, string? sube, string? aracGrupKod, DateOnly? tarih, int? gun, string? paraBirimi,
                RateMatrixService s, CancellationToken ct) =>
            {
                if (gun is not { } g || g is < 1 or > 3650) throw new ValidationException("Gün sayısı 1 ile 3650 arasında olmalıdır.", "gun");
                var an = F5Ortak.GunBasi(tarih ?? DateOnly.FromDateTime(DateTime.UtcNow), "tarih");
                var r = await s.ResolveAsync(new RateMatrisSorgu(F5Ortak.Nz(kanal), F5Ortak.Nz(sube), F5Ortak.Nz(aracGrupKod), an, g,
                    F5Ortak.Nz(paraBirimi)), ct);
                return r is null
                    ? S.NotFound("Eşleşen onaylı tarife bulunamadı.")
                    : TypedResults.Ok(new RateMatrixPriceDto(r.Id, r.Kod, r.Ad, r.GunlukFiyat, r.ParaBirimi, r.ToplamFiyat, g));
            }).WithTags("Fiyat & Tarife").RequirePermission(Permission.OperationsWrite);

        // Kampanya / kural arama (canlı kampanya_ara): görünümü daraltır, motor bu yolu kullanmaz.
        v1.MapGroup("").MapGet("/kira-kurallari/ara", async Task<Ok<Sayfa<RentalRuleDto>>> (
                string? q, string? durum, string? tarihTipi, bool? kampanyaMi, string? kanal, DateOnly? gecerliBas,
                DateOnly? gecerliBit, int? sayfa, int? boyut, string? sirala, RentalRuleService s, CancellationToken ct) =>
            {
                var (bas, bit) = F5Ortak.GunAraligi(gecerliBas, gecerliBit, "gecerliBas", "gecerliBit");
                var rows = await s.SearchAsync(new RentalRuleFilter
                {
                    Terim = F5Ortak.Nz(q), Durum = F5Ortak.EnumAdi<CampaignStatus>(durum, "durum"),
                    TarihTipi = F5Ortak.EnumAdi<RuleDateType>(tarihTipi, "tarihTipi"), KampanyaMi = kampanyaMi,
                    Kanal = F5Ortak.Nz(kanal), GecerliBas = bas, GecerliBit = bit,
                }, ct);
                return TypedResults.Ok(F5Ortak.Sayfala(rows.Select(r => RentalRuleDto.From(r, null)).ToList(),
                    RentalRules.Sort, sayfa, boyut, sirala));
            }).AlanlariEsle(F5Ortak.SiralamaKurallari).WithTags("Fiyat & Tarife").RequirePermission(Permission.OperationsWrite);
    }
}

/// <summary>Tarife matrisi fiyat sorgusu sonucu (salt okuma).</summary>
public sealed record RateMatrixPriceDto(Guid Id, string Kod, string Ad, decimal GunlukFiyat, string? ParaBirimi,
    decimal ToplamFiyat, int Gun);
