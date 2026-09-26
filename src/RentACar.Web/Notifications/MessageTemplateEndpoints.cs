using RentACar.Application.Authorization;
using RentACar.Application.Notifications;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.Notifications;

/// <summary>
/// Mesaj şablonu yazma ucu. POST alt-yolda (<c>/kaydet</c>) — sayfayla aynı yolda MapPost açmak
/// <c>@page</c> ile çakışır ve AmbiguousMatchException/500 üretir (repo dersi).
/// </summary>
public static class MessageTemplateEndpoints
{
    public static IEndpointRouteBuilder MapMessageTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/mesaj-sablonlari")
            .RequirePermission(Permission.ManageUsers)
            .AntiforgeryByEnv();

        grp.MapPost("/kaydet", async (HttpRequest req, CustomerNotificationService svc) =>
        {
            var f = req.Form;
            if (!Enum.TryParse<MessageType>(f["tur"].ToString(), out var type)
                || !Enum.TryParse<MessageChannel>(f["kanal"].ToString(), out var channel))
                return Results.Redirect("/mesaj-sablonlari?hata=" + Uri.EscapeDataString("Geçersiz şablon türü ya da kanalı."));

            try
            {
                await svc.SaveTemplateAsync(new MesajSablonInput
                {
                    Tur = type,
                    Kanal = channel,
                    Konu = f["konu"].ToString(),
                    Govde = f["govde"].ToString(),
                    Aktif = f["aktif"].ToString() is "true" or "on",
                });
                return Results.Redirect("/mesaj-sablonlari?ok=1");
            }
            catch (RentACar.Application.Common.ValidationException ex)
            {
                return Results.Redirect("/mesaj-sablonlari?hata=" + Uri.EscapeDataString(ex.Message));
            }
        });

        return app;
    }
}
