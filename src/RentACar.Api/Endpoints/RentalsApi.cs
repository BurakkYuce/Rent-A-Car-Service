using RentACar.Api.Common;
using RentACar.Api.Dtos;
using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Common;

namespace RentACar.Api.Endpoints;

/// <summary>Kira sözleşmesi JSON API: doğrudan oluştur + teslim/dönüş + iptal. Double-booking
/// DB exclusion constraint ile garanti → çakışma 409. Yazma OperationsWrite; tenant izolasyonu JWT→RLS.</summary>
public static class RentalsApi
{
    public static IEndpointRouteBuilder MapRentalsApi(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/v1/rentals").WithTags("Rentals").RequireAuthorization();

        grp.MapGet("/", async (RentalService svc, CancellationToken ct) =>
            Results.Ok((await svc.ListAsync(ct)).Select(RentalResponse.From)));

        grp.MapGet("/{id:guid}", async (Guid id, RentalService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } r ? Results.Ok(RentalResponse.From(r)) : NotFound());

        grp.MapPost("/", async (BookingRequest req, RentalService svc, CancellationToken ct) =>
        {
            // Müşteri/araç varlık kontrolü RentalService.CreateDirectAsync girişinde (tüm yollar için tek kural).
            var id = await svc.CreateDirectAsync(req.ToInput(), ct);
            return Results.Created($"/api/v1/rentals/{id}", RentalResponse.From((await svc.GetAsync(id, ct))!));
        }).RequirePermission(Permission.OperationsWrite);

        grp.MapPost("/{id:guid}/deliver", async (Guid id, DeliverRequest req, RentalService svc, CancellationToken ct) =>
            await svc.DeliverAsync(id, req.CikisKm, YuzdeYakit(req.CikisYakit, "cikisYakit"), ct)
                ? Results.Ok(RentalResponse.From((await svc.GetAsync(id, ct))!)) : NotFound())
            .RequirePermission(Permission.OperationsWrite);

        grp.MapPost("/{id:guid}/return", async (Guid id, ReturnRequest req, RentalService svc, CancellationToken ct) =>
            await svc.ReturnAsync(id, req.DonusKm, YuzdeYakit(req.DonusYakit, "donusYakit"), req.GercekDonus, ct: ct)
                ? Results.Ok(RentalResponse.From((await svc.GetAsync(id, ct))!)) : NotFound())
            .RequirePermission(Permission.OperationsWrite);

        grp.MapPost("/{id:guid}/cancel", async (Guid id, RentalService svc, CancellationToken ct) =>
            await svc.CancelAsync(id, ct) ? Results.Ok(RentalResponse.From((await svc.GetAsync(id, ct))!)) : NotFound())
            .RequirePermission(Permission.OperationsWrite);

        return app;
    }

    /// <summary>
    /// Rezervasyon uçları için (kira yolu artık <see cref="RentalService.CreateDirectAsync"/> içinde denetler).
    /// Low-B (DEVIR §5 "Varlık kontrolü"): Rentals/Reservations'ın Customers/Vehicles'a bileşik FK'si yok → var
    /// olmayan ya da BAŞKA KİRACININ müşteri/araç kimliğiyle kira/rezervasyon (ve sonradan fatura/defter)
    /// yazılabiliyordu. RLS + tenant query filter kapsamlı FindAsync: yabancı kiracının kaydı "yok" görünür (varlık
    /// sızmaz) → 400 <c>validation</c>. Kalıcı çözüm (bileşik FK) ayrı iş.
    /// </summary>
    internal static async Task VarlikKontroluAsync(
        BookingRequest req, ICustomerRepository cariler, IVehicleRepository araclar, CancellationToken ct)
    {
        if (req.MusteriId == Guid.Empty || await cariler.FindAsync(req.MusteriId, ct) is null)
            throw new ValidationException("Müşteri bulunamadı.", "musteriId");
        if (req.VehicleId == Guid.Empty || await araclar.FindAsync(req.VehicleId, ct) is null)
            throw new ValidationException("Araç bulunamadı.", "vehicleId");
    }

    /// <summary>
    /// Yakıt ölçeği sınırı (Karar (3), 2026-09-25): harici sözleşme YÜZDE (0–100) korunur, iç ölçek 0–12.
    /// Aralık dışı → 400 (sessiz kıstırma yok — 101 bir istemci hatasıdır, 100 sayılmaz); geçerli değer en
    /// yakın on ikide bire çevrilir (50 → 6, 80 → 10).
    /// </summary>
    internal static int YuzdeYakit(int yuzde, string alan)
    {
        if (yuzde is < 0 or > YakitOlcegi.YuzdeEnFazla)
            throw new ValidationException($"Yakıt yüzdesi 0-{YakitOlcegi.YuzdeEnFazla} aralığında olmalıdır.", alan);
        return YakitOlcegi.YuzdedenOnIkiye(yuzde);
    }

    private static IResult NotFound()
        => Results.Json(new ApiError("not_found", "Kira sözleşmesi bulunamadı."), statusCode: StatusCodes.Status404NotFound);
}
