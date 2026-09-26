using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.WebSite;
using RentACar.Domain.Entities;
using RentACar.Web.Api.Kira;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Sistem;

public static partial class WebsiteApi
{
    private const string ListingsRoot = UiApiExtensions.V1 + "/web-sitesi/ilanlar";

    /// <summary>Beraber modda gönderilebilecek en fazla küme imzası (200 araçlık filo ≈ onlarca küme).</summary>
    private const int MaxSelection = 1_000;

    /// <summary>
    /// İlan sihirbazı (Blazor <c>WebSiteHub</c>/<c>AracEkle</c>/<c>IlanFiyat</c>/<c>IlanOzellik</c>). İlan fotoğrafları üye
    /// ARAÇLARIN fotoğraflarıdır; içerikleri araç foto ucundan (<c>/araclar/{id}/fotograflar/{fotoId}</c>) okunur.
    /// </summary>
    private static void MapListings(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/web-sitesi").WithTags(SystemApiCommon.WebsiteTag)
            .RequirePermission(Permission.OperationsWrite).RequireWebSitesiModulu();

        g.MapGet("/ozet", async (WebListingService s, CancellationToken ct) =>
        {
            var rows = await s.ListAsync(ct);
            return TypedResults.Ok(new WebsiteSummaryDto(await s.UnlistedVehicleCountAsync(ct), rows.Count, rows.Count(r => r.Yayinda)));
        });

        g.MapGet("/havuz", async (WebListingService s, CancellationToken ct) =>
            TypedResults.Ok<IReadOnlyList<VehicleClusterDto>>((await s.PoolAsync(ct))
                .Select(k => new VehicleClusterDto(k.Imza, k.Baslik, k.YilAralik,
                    k.Araclar.Select(v => new ClusterVehicleDto(v.Id, v.Plaka, v.Sube)).ToList()))
                .ToList()));

        g.MapGet("/ilanlar", async (int? sayfa, int? boyut, string? sirala, WebListingService s, CancellationToken ct) =>
            TypedResults.Ok(F5Ortak.Sayfala((await s.ListAsync(ct)).Select(ToRow).ToList(), ListingSort, sayfa, boyut, sirala)))
            .AlanlariEsle(F5Ortak.SiralamaKurallari);

        g.MapPost("/ilanlar", CreateListing);

        g.MapGet("/ilanlar/{id:guid}", async Task<Results<Ok<ListingDetailDto>, ProblemHttpResult>> (
            Guid id, WebListingService s, CancellationToken ct)
            => await ListingDetailAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound("İlan bulunamadı."));

        g.MapPut("/ilanlar/{id:guid}/fiyat", async Task<Results<Ok<ListingPriceResultDto>, ProblemHttpResult>> (
            Guid id, ListingPriceRequest i, WebListingService s, CancellationToken ct) =>
        {
            if (await s.GetAsync(id, ct) is null) return SystemApiCommon.NotFound("İlan bulunamadı.");
            SystemApiCommon.RequireVersion(i.Surum);
            if (i.GunlukFiyat is null) throw new ValidationException("Günlük fiyat zorunludur.", "gunlukFiyat");
            Sinirlar.Tutar(i.GunlukFiyat, "gunlukFiyat", "Günlük fiyat");
            Sinirlar.Tutar(i.HaftalikToplam, "haftalikToplam", "Haftalık toplam");
            Sinirlar.Tutar(i.AylikToplam, "aylikToplam", "Aylık toplam");
            var copied = await s.StepTwoAsync(id, i.GunlukFiyat.Value, i.HaftalikToplam, i.AylikToplam, i.KdvDahil, i.Surum!, ct);
            return await ListingDetailAsync(id, s, ct) is { } d
                ? TypedResults.Ok(new ListingPriceResultDto(copied, d)) : SystemApiCommon.NotFound("İlan bulunamadı.");
        }).AlanlariEsle(PriceRules);

        g.MapPut("/ilanlar/{id:guid}/ozellikler", async Task<Results<Ok<ListingFeaturesResultDto>, ProblemHttpResult>> (
            Guid id, ListingFeaturesRequest i, WebListingService s, CancellationToken ct) =>
        {
            if (await s.GetAsync(id, ct) is null) return SystemApiCommon.NotFound("İlan bulunamadı.");
            SystemApiCommon.RequireVersion(i.Surum);
            var rows = (i.Satirlar ?? []).Select(r => new OzellikSatiri(r.Etiket ?? "", r.Deger ?? "", r.Gorunur)).ToList();
            var published = await s.StepThreeAsync(id, rows, i.Surum!, ct);
            return await ListingDetailAsync(id, s, ct) is { } d
                ? TypedResults.Ok(new ListingFeaturesResultDto(published, d)) : SystemApiCommon.NotFound("İlan bulunamadı.");
        }).AlanlariEsle(FeatureRules);

        g.MapPost("/ilanlar/{id:guid}/durum", async Task<Results<Ok<ListingDetailDto>, ProblemHttpResult>> (
            Guid id, ListingStatusRequest i, WebListingService s, CancellationToken ct) =>
        {
            if (await s.GetAsync(id, ct) is null) return SystemApiCommon.NotFound("İlan bulunamadı.");
            var status = F5Ortak.EnumAdi<WebIlanDurum>(i.Durum, "durum")
                         ?? throw new ValidationException("Durum zorunludur (Yayinda ya da Pasif).", "durum");
            await s.SetStatusAsync(id, status, ct);
            return await ListingDetailAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound("İlan bulunamadı.");
        }).AlanlariEsle([("İlan taslağa", "durum")]);

        g.MapDelete("/ilanlar/{id:guid}", async Task<Results<NoContent, ProblemHttpResult>> (
            Guid id, WebListingService s, CancellationToken ct) =>
        {
            if (await s.GetAsync(id, ct) is null) return SystemApiCommon.NotFound("İlan bulunamadı.");
            await s.DeleteAsync(id, ct);
            return TypedResults.NoContent();
        });

        // ---- fotoğraflar (üye araçlarda saklanır; tür/boyut/adet sınırı VehiclePhotoService'te)
        g.MapPost("/ilanlar/{id:guid}/fotograflar", UploadListingPhoto).DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(UploadRequestLimit))
            .AlanlariEsle(PhotoRules);

        g.MapDelete("/ilanlar/{id:guid}/fotograflar/{aracId:guid}/{fotoId:guid}",
            async Task<Results<Ok<IReadOnlyList<ListingPhotoDto>>, ProblemHttpResult>> (
                Guid id, Guid aracId, Guid fotoId, WebListingService s, CancellationToken ct) =>
            {
                if (await s.GetAsync(id, ct) is null) return SystemApiCommon.NotFound("İlan bulunamadı.");
                // Fotoğraf BU ilanın üye aracına ait olmalı (başka aracın/ilanın foto kimliği 404).
                if ((await s.PhotosAsync(id, ct)).All(p => p.VehicleId != aracId || p.PhotoId != fotoId))
                    return SystemApiCommon.NotFound("Fotoğraf bulunamadı.");
                await s.DeletePhotoAsync(id, aracId, fotoId, ct);
                return TypedResults.Ok(await PhotosAsync(id, s, ct));
            });
    }

    private static async Task<Results<Created<ListingCreatedDto>, ProblemHttpResult>> CreateListing(
        ListingCreateRequest i, WebListingService s, CancellationToken ct)
    {
        var separate = string.Equals(SystemApiCommon.Clean(i.Mod), "ayri", StringComparison.OrdinalIgnoreCase);
        if (i.Mod is { } mod && !separate && !string.Equals(mod.Trim(), "beraber", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Geçersiz mod. İzin verilenler: beraber, ayri.", "mod");
        var field = separate ? "aracIdler" : "imzalar";
        if ((separate ? i.AracIdler?.Count : i.Imzalar?.Count) > MaxSelection)
            throw new ValidationException($"En fazla {MaxSelection} seçim gönderilebilir.", field);
        try
        {
            var id = separate
                ? await s.StepOneAsync([.. (i.AracIdler ?? []).Where(g => g != Guid.Empty)], together: false, ct)
                : await s.StepOneSignatureAsync([.. (i.Imzalar ?? []).Where(x => !string.IsNullOrWhiteSpace(x))], ct);
            return TypedResults.Created($"{ListingsRoot}/{id}", new ListingCreatedDto(id));
        }
        catch (ValidationException ex) when (ex.GetType() == typeof(ValidationException) && ex.Alan is null)
        {
            throw new ValidationException(ex.Message, field);
        }
    }

    private static async Task<Results<Created<IReadOnlyList<ListingPhotoDto>>, ProblemHttpResult>> UploadListingPhoto(
        Guid id, IFormFile? foto, WebListingService s, CancellationToken ct)
    {
        if (await s.GetAsync(id, ct) is null) return SystemApiCommon.NotFound("İlan bulunamadı.");
        if (foto is null || foto.Length == 0) throw new ValidationException("Fotoğraf seçilmedi.", "foto");
        if (foto.Length > UploadRequestLimit) throw new ValidationException("Fotoğraf en fazla 2 MB olabilir.", "foto");
        using var ms = new MemoryStream();
        await foto.CopyToAsync(ms, ct);
        await s.AddPhotoAsync(id, ms.ToArray(), ct); // içerikten tür tespiti + 2 MB + adet sınırı servistedir
        return TypedResults.Created($"{ListingsRoot}/{id}", await PhotosAsync(id, s, ct));
    }

    private static readonly (string, string)[] PriceRules =
        [("Günlük fiyat", "gunlukFiyat"), ("Haftalık toplam", "haftalikToplam"), ("Aylık toplam", "aylikToplam")];

    private static readonly (string, string)[] FeatureRules =
        [("En fazla", "satirlar"), ("Özellik adı", "satirlar"), ("Özellik değeri", "satirlar")];

    private static readonly (string, string)[] PhotoRules =
    [
        ("PNG, JPEG veya WebP", "foto"), ("Fotoğraf en fazla", "foto"), ("Araç başına en fazla", "foto"),
        ("Fotoğraf seçilmedi", "foto"), ("İlana bağlı araç yok", "foto"),
    ];

    private static ListingRowDto ToRow(WebIlanSatiri r)
        => new(r.Id, r.Baslik, r.Durum.ToString(), r.Yayinda, r.AracSayisi, r.Adet, r.GunlukFiyat, r.KdvDahil, r.Eksikler, r.OzellikBayat);

    private static async Task<IReadOnlyList<ListingPhotoDto>> PhotosAsync(Guid id, WebListingService s, CancellationToken ct)
        => (await s.PhotosAsync(id, ct)).Select(p => new ListingPhotoDto(p.VehicleId, p.PhotoId, p.Plaka, p.Sira)).ToList();

    /// <summary>Sürüm alanlardan ÖNCE okunur (arada yazım olursa istemci bayat sürümle 409 alır, sessiz ezme yok).</summary>
    private static async Task<ListingDetailDto?> ListingDetailAsync(Guid id, WebListingService s, CancellationToken ct)
    {
        var version = await s.VersionAsync(id, ct);
        if (await s.GetAsync(id, ct) is not { } d) return null;
        var features = await s.SuggestedFeaturesAsync(id, ct);
        return new ListingDetailDto(d.Ilan.Id, d.Ilan.Baslik, d.Ilan.Slug, d.Ilan.Durum.ToString(), d.Ilan.GunlukFiyat,
            d.Ilan.HaftalikToplam, d.Ilan.AylikToplam, d.Ilan.KdvDahil, await s.SiblingDraftCountAsync(id, ct),
            d.Araclar.Select(v => new ListingVehicleDto(v.Id, v.Plaka, v.Sube)).ToList(),
            features.Select(f => new ListingFeatureDto(f.Etiket, f.Deger, f.Gorunur)).ToList(),
            await PhotosAsync(id, s, ct), version);
    }

    private static readonly SortFieldMap<ListingRowDto> ListingSort = SortFieldMap<ListingRowDto>
        .Create(x => x.Id).Alan("baslik", x => x.Baslik).Alan("durum", x => x.Durum).Alan("gunlukFiyat", x => x.GunlukFiyat)
        .Alan("aracSayisi", x => x.AracSayisi).Alan("yayinda", x => x.Yayinda);
}
