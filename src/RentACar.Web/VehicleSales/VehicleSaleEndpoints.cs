using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Web.Identity;
using RentACar.Application.VehicleSales;

namespace RentACar.Web.VehicleSales;

/// <summary>Araç satış form post ucu. Tenant HttpContext claim'inden (RLS).</summary>
public static class VehicleSaleEndpoints
{
    public static IEndpointRouteBuilder MapVehicleSaleEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/satislar").RequirePermission(Permission.FinanceWrite).AntiforgeryByEnv();

        // hedefFiyat/satisKm string? olarak alınır: opsiyonel sayısal alanlar boş string ("") gelince
        // [FromForm] decimal?/int? bağlama 400 verir (CLAUDE.md §5 tuzağı) → FormParse ile çevrilir.
        grp.MapPost("/create", async (VehicleSaleService svc,
            [FromForm] Guid vehicleId, [FromForm] Guid aliciCariId, [FromForm] decimal satisNet,
            [FromForm] decimal kdvOrani, [FromForm] string? noterNo, [FromForm] string? doviz,
            [FromForm] string? kur, [FromForm] string? aciklama,
            [FromForm] string? hedefFiyat, [FromForm] string? satisKm,
            [FromForm] string? satisKanali, [FromForm] string? devir) =>
        {
            var input = new VehicleSaleInput
            {
                VehicleId = vehicleId, AliciCariId = aliciCariId, SatisNet = satisNet, KdvOrani = kdvOrani,
                NoterNo = noterNo, Doviz = string.IsNullOrWhiteSpace(doviz) ? "TRY" : doviz,
                Kur = FormParse.Dec(kur), Aciklama = aciklama, // boş → otomatik kur (1.1)
                // wire-in: entity + input + servis hazırdı, yalnız uç bu dördünü okumuyordu
                HedefFiyat = FormParse.Dec(hedefFiyat), SatisKm = FormParse.Int(satisKm),
                SatisKanali = satisKanali, Devir = devir
            };
            try
            {
                await svc.CreateAsync(input);
                return Results.Redirect("/satislar");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/satislar?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        return app;
    }
}
