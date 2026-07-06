using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Personnel;
using RentACar.Web.Identity;

namespace RentACar.Web.Personnel;

/// <summary>Personel master form post uçları (roadmap C1). PII/HR → ManageUsers (Admin). Opsiyonel
/// tarih/sayısal alanlar boş "" bind 400 vermesin diye IFormCollection + FormParse.</summary>
public static class PersonelEndpoints
{
    public static IEndpointRouteBuilder MapPersonelEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/personel").RequirePermission(Permission.ManageUsers).AntiforgeryByEnv();

        grp.MapPost("/create", async (PersonelService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(req.Form))));

        grp.MapPost("/update", async (PersonelService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form))));

        grp.MapPost("/delete", async (PersonelService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static PersonelInput Build(IFormCollection f) => new()
    {
        Kod = f["kod"].ToString(),
        Ad = f["ad"].ToString(),
        Soyad = f["soyad"].ToString(),
        TcKimlik = FormParse.Str(f, "tcKimlik"),
        IseGiris = FormParse.Date(FormParse.Str(f, "iseGiris")),
        IseCikis = FormParse.Date(FormParse.Str(f, "iseCikis")),
        SurucuBelgeNo = FormParse.Str(f, "surucuBelgeNo"),
        Maas = FormParse.Dec(FormParse.Str(f, "maas")),
        Sube = FormParse.Str(f, "sube"),
        Aktif = (FormParse.Str(f, "aktif") ?? "true") is "true" or "True"
    };


    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/personel"); }
        catch (ValidationException ex) { return Results.Redirect($"/personel?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
