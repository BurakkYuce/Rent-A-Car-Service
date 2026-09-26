using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.SiteIcerik;
using RentACar.Web.Identity;

namespace RentACar.Web.SiteIcerik;

/// <summary>
/// PR-16 — halka açık site içeriğinin form-POST uçları (sayfalar + SSS).
///
/// <para>İKİ KATMANLI KAPI, <c>WebSiteEndpoints</c> ile birebir aynı: <see cref="Permission.OperationsWrite"/>
/// (rol) + <c>RequireWebSitesiModulu()</c> (satın alma). Servis içinde ayrıca <c>"web-sitesi"</c> ekran
/// kodu kontrol ediliyor → üç katman.</para>
///
/// <para>POST'lar Razor sayfasının yolunda DEĞİL alt yolda (<c>/site-icerik/kaydet</c>): aynı yola
/// hem <c>@page</c> hem <c>MapPost</c> koymak <c>AmbiguousMatchException</c> (500) veriyor ve bunu
/// yalnız canlı test yakalıyor.</para>
/// </summary>
public static class SiteIcerikEndpoints
{
    public static IEndpointRouteBuilder MapSiteIcerikEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/site-icerik")
            .RequirePermission(Permission.OperationsWrite)
            .RequireWebSitesiModulu()
            .AntiforgeryByEnv();

        // ---- Sayfalar ----
        grp.MapPost("/kaydet", async (SiteContentService svc, HttpRequest req) =>
        {
            var f = req.Form;
            try
            {
                var id = await svc.SaveAsync(new SayfaIcerikInput(
                    FormParse.Id(FormParse.Str(f, "id")),
                    f["baslik"].ToString(),
                    f["govde"].ToString(),
                    FormParse.Str(f, "slug"),
                    FormParse.Str(f, "metaAciklama"),
                    FormParse.Int(FormParse.Str(f, "sira")) ?? 0,
                    f["yayinda"].ToString() == "true"));
                return Results.Redirect($"/site-icerik?ok=1&sayfa={id}");
            }
            catch (ValidationException ex) { return Geri(ex); }
        });

        grp.MapPost("/{id:guid}/durum", async (SiteContentService svc, Guid id, [FromForm] string durum) =>
        {
            try
            {
                await svc.PublishStatusAsync(id, durum == "yayinda");
                return Results.Redirect("/site-icerik?ok=1");
            }
            catch (ValidationException ex) { return Geri(ex); }
        });

        grp.MapPost("/{id:guid}/sil", async (SiteContentService svc, Guid id) =>
        {
            try { await svc.DeleteAsync(id); return Results.Redirect("/site-icerik?ok=1"); }
            catch (ValidationException ex) { return Geri(ex); }
        });

        // ---- SSS ----
        grp.MapPost("/sss/kaydet", async (SiteContentService svc, HttpRequest req) =>
        {
            var f = req.Form;
            try
            {
                await svc.SaveFaqAsync(new SssInput(
                    FormParse.Id(FormParse.Str(f, "id")),
                    f["soru"].ToString(),
                    f["cevap"].ToString(),
                    FormParse.Int(FormParse.Str(f, "sira")) ?? 0,
                    f["yayinda"].ToString() == "true"));
                return Results.Redirect("/site-icerik?ok=1&sekme=sss");
            }
            catch (ValidationException ex) { return Geri(ex, "&sekme=sss"); }
        });

        grp.MapPost("/sss/{id:guid}/sil", async (SiteContentService svc, Guid id) =>
        {
            try { await svc.DeleteFaqAsync(id); return Results.Redirect("/site-icerik?ok=1&sekme=sss"); }
            catch (ValidationException ex) { return Geri(ex, "&sekme=sss"); }
        });

        return app;
    }

    private static IResult Geri(ValidationException ex, string ek = "")
        => Results.Redirect("/site-icerik?hata=" + Uri.EscapeDataString(ex.Message) + ek);
}
