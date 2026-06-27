using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Common;
using RentACar.Application.ServiceRecords;
using RentACar.Domain.Enums;

namespace RentACar.Web.ServiceRecords;

/// <summary>Servis/bakım form post uçları (kayıt + akış + kalem). Tenant claim'inden (RLS).</summary>
public static class ServiceRecordEndpoints
{
    public static IEndpointRouteBuilder MapServiceRecordEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/servisler").RequireAuthorization().DisableAntiforgery();

        grp.MapPost("/create", async (ServiceRecordService svc,
            [FromForm] Guid vehicleId, [FromForm] ServisTipi tip, [FromForm] string? atolyeAdi,
            [FromForm] int girisKm, [FromForm] HasarSorumlu hasarSorumlu, [FromForm] decimal? kusurOrani,
            [FromForm] string? aciklama) =>
        {
            var input = new ServiceRecordInput
            {
                VehicleId = vehicleId, Tip = tip, AtolyeAdi = atolyeAdi, GirisKm = girisKm,
                HasarSorumlu = hasarSorumlu, KusurOrani = kusurOrani, Aciklama = aciklama
            };
            return await Run(() => svc.CreateAsync(input));
        });

        grp.MapPost("/baslat", (ServiceRecordService svc, [FromForm] Guid id) => Run(() => svc.BaslatAsync(id)));
        grp.MapPost("/tamamla", (ServiceRecordService svc, [FromForm] Guid id, [FromForm] int cikisKm, [FromForm] int? sonrakiBakimKm)
            => Run(() => svc.TamamlaAsync(id, cikisKm, sonrakiBakimKm)));
        grp.MapPost("/iptal", (ServiceRecordService svc, [FromForm] Guid id) => Run(() => svc.IptalAsync(id)));
        grp.MapPost("/kalem", (ServiceRecordService svc, [FromForm] Guid id, [FromForm] string aciklama, [FromForm] decimal tutar)
            => Run(() => svc.KalemEkleAsync(id, aciklama, tutar)));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try
        {
            await action();
            return Results.Redirect("/servisler");
        }
        catch (ValidationException ex)
        {
            return Results.Redirect($"/servisler?hata={Uri.EscapeDataString(ex.Message)}");
        }
    }
}
