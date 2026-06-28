using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.ServiceRecords;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.ServiceRecords;

/// <summary>Servis/bakım form post uçları (kayıt + akış + kalem). Tenant claim'inden (RLS).</summary>
public static class ServiceRecordEndpoints
{
    public static IEndpointRouteBuilder MapServiceRecordEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/servisler").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (ServiceRecordService svc,
            [FromForm] Guid vehicleId, [FromForm] ServisTipi tip, [FromForm] string? atolyeAdi,
            [FromForm] int girisKm, [FromForm] HasarSorumlu hasarSorumlu, [FromForm] string? kusurOrani,
            [FromForm] string? aciklama) =>
        {
            var input = new ServiceRecordInput
            {
                VehicleId = vehicleId, Tip = tip, AtolyeAdi = atolyeAdi, GirisKm = girisKm,
                HasarSorumlu = hasarSorumlu, KusurOrani = FormParse.Dec(kusurOrani), Aciklama = aciklama
            };
            return await Run(() => svc.CreateAsync(input));
        });

        grp.MapPost("/baslat", (ServiceRecordService svc, [FromForm] Guid id) => Run(() => svc.BaslatAsync(id)));
        grp.MapPost("/tamamla", (ServiceRecordService svc, [FromForm] Guid id, [FromForm] int cikisKm, [FromForm] string? sonrakiBakimKm)
            => Run(() => svc.TamamlaAsync(id, cikisKm, FormParse.Int(sonrakiBakimKm))));
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
