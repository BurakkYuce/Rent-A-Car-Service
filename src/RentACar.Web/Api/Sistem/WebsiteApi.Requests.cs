using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Availability;
using RentACar.Application.Common;
using RentACar.Application.PublicSite;
using RentACar.Application.Vehicles;
using RentACar.Application.WebSite;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Web.Api.Kira;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Sistem;

public static partial class WebsiteApi
{
    /// <summary>
    /// Gelen site talepleri (Blazor <c>GelenTalepler</c>). Talep bir LEAD'dir: ziyaretçinin kendi girdiği ad/telefon/e-posta
    /// ekranda olduğu gibi gösterilir (TC yok). Var olmayan / başka kiracının talebi 404 (RLS kapsamlı okuma). Dönüştürmede
    /// seçilen araç GİRİŞ NOKTASINDA şube kapsamından geçer (kapsam dışı 403) — Blazor ekranı bunu yapmıyordu.
    /// </summary>
    private static void MapBookingRequests(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/gelen-talepler").WithTags(SystemApiCommon.WebsiteTag).RequirePermission(Permission.OperationsWrite);

        g.MapGet("", async (string? durum, string? ara, int? sayfa, int? boyut, PublicBookingRequestService s, CancellationToken ct) =>
        {
            RentalLimits.Text(ara, 100, "ara", "Arama");
            var status = F5Shared.EnumAdi<PublicBookingRequestDurum>(durum, "durum");
            var page = Math.Max(1, sayfa ?? 1);
            var size = Math.Clamp(boyut ?? 25, 1, 200);
            var (rows, total) = await s.ListRequestsAsync(new TalepFiltre(status, SystemApiCommon.Clean(ara), page, size), ct);
            var summary = await s.SummaryAsync(ct);
            return TypedResults.Ok(new BookingRequestPageDto(rows.Select(ToRequestRow).ToList(), total, page, size,
                new BookingRequestSummaryDto(summary.Yeni, summary.EnEskiGun)));
        }).MapFields([("Geçersiz durum", "durum")]);

        g.MapGet("/ozet", async (PublicBookingRequestService s, CancellationToken ct) =>
        {
            var summary = await s.SummaryAsync(ct);
            return TypedResults.Ok(new BookingRequestSummaryDto(summary.Yeni, summary.EnEskiGun));
        });

        g.MapGet("/{id:guid}/notlar", async Task<Results<Ok<IReadOnlyList<BookingRequestNoteDto>>, ProblemHttpResult>> (
            Guid id, PublicBookingRequestService s, IPublicBookingRequestRepository r, CancellationToken ct) =>
        {
            if (await r.FindAsync(id, ct) is null) return RequestNotFound();
            return TypedResults.Ok<IReadOnlyList<BookingRequestNoteDto>>((await s.NotesAsync(id, ct))
                .Select(n => new BookingRequestNoteDto(n.Id, n.Metin, n.Kullanici, n.ZamanUtc)).ToList());
        });

        g.MapGet("/{id:guid}/aday-araclar", CandidatesAsync);

        g.MapPost("/{id:guid}/durum", async Task<Results<NoContent, ProblemHttpResult>> (
            Guid id, BookingRequestStatusRequest i, PublicBookingRequestService s, IPublicBookingRequestRepository r, CancellationToken ct) =>
        {
            if (await r.FindAsync(id, ct) is null) return RequestNotFound();
            var status = F5Shared.EnumAdi<PublicBookingRequestDurum>(i.Durum, "durum")
                         ?? throw new ValidationException("Durum zorunludur.", "durum");
            await s.AssignStatusAsync(id, status, ct);
            return TypedResults.NoContent();
        }).MapFields([("\"Dönüştü\"", "durum")]);

        g.MapPost("/{id:guid}/ustlen", async Task<Results<NoContent, ProblemHttpResult>> (
            Guid id, BookingRequestClaimRequest i, PublicBookingRequestService s, IPublicBookingRequestRepository r, CancellationToken ct) =>
        {
            if (await r.FindAsync(id, ct) is null) return RequestNotFound();
            await s.ClaimAsync(id, i.Ustlen, ct); // yalnız KENDİNE atanır (kimlik oturumdan)
            return TypedResults.NoContent();
        });

        g.MapPost("/{id:guid}/notlar", async Task<Results<Created<IReadOnlyList<BookingRequestNoteDto>>, ProblemHttpResult>> (
            Guid id, BookingRequestNoteRequest i, PublicBookingRequestService s, IPublicBookingRequestRepository r, CancellationToken ct) =>
        {
            if (await r.FindAsync(id, ct) is null) return RequestNotFound();
            await s.AddNoteAsync(id, i.Metin ?? "", ct);
            return TypedResults.Created($"{UiApiExtensions.V1}/gelen-talepler/{id}/notlar",
                (IReadOnlyList<BookingRequestNoteDto>)(await s.NotesAsync(id, ct))
                    .Select(n => new BookingRequestNoteDto(n.Id, n.Metin, n.Kullanici, n.ZamanUtc)).ToList());
        }).MapFields([("Not ", "metin")]);

        g.MapPost("/{id:guid}/reddet", async Task<Results<NoContent, ProblemHttpResult>> (
            Guid id, PublicBookingRequestService s, IPublicBookingRequestRepository r, CancellationToken ct) =>
        {
            if (await r.FindAsync(id, ct) is null) return RequestNotFound();
            await s.RejectAsync(id, ct);
            return TypedResults.NoContent();
        });

        g.MapPost("/{id:guid}/donustur", async Task<Results<Ok<BookingRequestConvertedDto>, ProblemHttpResult>> (
            Guid id, BookingRequestConvertRequest i, PublicBookingRequestService s, IPublicBookingRequestRepository r,
            VehicleService vehicles, CancellationToken ct) =>
        {
            if (await r.FindAsync(id, ct) is null) return RequestNotFound();
            if (i.AracId is not { } vehicleId || vehicleId == Guid.Empty)
                throw new ValidationException("Dönüştürmek için bir araç seçin.", "aracId");
            // Kapsam GİRİŞ NOKTASINDA: VehicleService.GetAsync kapsam dışı araçta 403 (yetki_yok) fırlatır.
            if (await vehicles.GetAsync(vehicleId, ct) is null)
                throw new ValidationException("Araç bulunamadı.", "aracId");
            var reservationId = await s.ConvertAsync(id, vehicleId, ct);
            return TypedResults.Ok(new BookingRequestConvertedDto(reservationId));
        });
    }

    private static ProblemHttpResult RequestNotFound() => SystemApiCommon.NotFound("Talep bulunamadı.");

    /// <summary>
    /// Dönüştürme adayları (Blazor'daki <c>?donustur=</c> açılımı): talep tarihlerinde müsait araçlar, kullanıcının şube
    /// kapsamına süzülmüş. Talebin geldiği ilanın üye araçları <c>ilanAraci</c> ile işaretlenir. Talep kapanmışsa boş liste.
    /// </summary>
    private static async Task<Results<Ok<IReadOnlyList<CandidateVehicleDto>>, ProblemHttpResult>> CandidatesAsync(
        Guid id, IPublicBookingRequestRepository r, AvailabilityService availability, IWebListingRepository listings,
        ICurrentUser user, CancellationToken ct)
    {
        PermissionGuard.Require(user, Permission.OperationsWrite);
        if (await r.FindAsync(id, ct) is not { } t) return RequestNotFound();
        if (!TalepDurumu.IsActive(t.Durum)) return TypedResults.Ok<IReadOnlyList<CandidateVehicleDto>>([]);
        var scope = BranchScope.EffectiveFilter(user);
        var available = (await availability.FindAvailableAsync(t.BasTar, t.BitTar, null, t.Sube, ct))
            .Where(v => scope.Unrestricted || BranchScope.InScope(scope, v.SubeId, v.Sube)).ToList();
        var members = t.IlanId is { } listingId && await listings.FindAsync(listingId, ct) is { } d
            ? d.Araclar.Select(v => v.Id).ToHashSet()
            : [];
        return TypedResults.Ok<IReadOnlyList<CandidateVehicleDto>>(available
            .OrderByDescending(v => members.Contains(v.Id)).ThenBy(v => v.Plaka, StringComparer.Ordinal)
            .Select(v => new CandidateVehicleDto(v.Id, v.Plaka, v.Marka, v.Tip, v.Grup, v.Sube, members.Contains(v.Id)))
            .ToList());
    }

    private static BookingRequestRowDto ToRequestRow(TalepSatiri row)
    {
        var t = row.Talep;
        return new BookingRequestRowDto(t.Id, t.AdSoyad, t.Telefon, t.Email, t.IlanId, t.IlanBaslik, t.AracGrupKod,
            t.BasTar, t.BitTar, t.Sube, t.Not, t.GosterilenGunlukUcretKdvDahil, t.GosterilenKdvDahil,
            t.Durum.ToString(), TalepDurumu.Label(t.Durum), TalepDurumu.IsActive(t.Durum),
            NextStatuses(t.Durum).Select(d => d.ToString()).ToList(),
            t.DonusenReservationId, t.AtananKullaniciId, t.AtananAd, t.CreatedAtUtc, row.NotSayisi, row.BekleyenGun);
    }

    /// <summary>Elle geçilebilecek hedefler (Blazor <c>GelenTalepler.Ilerlemeler</c> ile aynı). "Dönüştü" yalnız
    /// dönüştürme akışıyla atanır.</summary>
    private static PublicBookingRequestDurum[] NextStatuses(PublicBookingRequestDurum d) => d switch
    {
        PublicBookingRequestDurum.Yeni => [PublicBookingRequestDurum.Iletisimde, PublicBookingRequestDurum.Kayip],
        PublicBookingRequestDurum.Iletisimde => [PublicBookingRequestDurum.TeklifVerildi, PublicBookingRequestDurum.Kayip],
        PublicBookingRequestDurum.TeklifVerildi => [PublicBookingRequestDurum.Kayip],
        _ => [],
    };
}
