using RentACar.Application.Authorization;
using RentACar.Application.TenantSettings;
using RentACar.Web.Identity;

namespace RentACar.Web.TenantSettings;

/// <summary>Ayarlar yazma ucu (roadmap D1). Hassas → ManageUsers (admin). Sır alanları boş gelirse
/// mevcut korunur (servis). IFormCollection (opsiyonel alanlar boş string → servis null'a çevirir).</summary>
public static class TenantSettingsEndpoints
{
    public static IEndpointRouteBuilder MapTenantSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/ayarlar").RequirePermission(Permission.ManageUsers).AntiforgeryByEnv();

        grp.MapPost("/kaydet", async (HttpRequest req, TenantSettingsService svc) =>
        {
            var f = req.Form;
            var m = new TenantSettingsModel
            {
                FirmaUnvan = f["firmaUnvan"].ToString(),
                FirmaVergiDairesi = f["firmaVergiDairesi"].ToString(),
                FirmaVergiNo = f["firmaVergiNo"].ToString(),
                FirmaAdres = f["firmaAdres"].ToString(),
                FirmaTel = f["firmaTel"].ToString(),
                FirmaEmail = f["firmaEmail"].ToString(),
                EFaturaKullanici = f["eFaturaKullanici"].ToString(),
                EFaturaSifre = f["eFaturaSifre"].ToString(),
                SmsBaslik = f["smsBaslik"].ToString(),
                SmsApiKey = f["smsApiKey"].ToString(),
                PosMerchantId = f["posMerchantId"].ToString(),
                PosApiKey = f["posApiKey"].ToString()
            };
            await svc.SaveAsync(m);
            return Results.Redirect("/ayarlar?ok=1");
        });

        return app;
    }
}
