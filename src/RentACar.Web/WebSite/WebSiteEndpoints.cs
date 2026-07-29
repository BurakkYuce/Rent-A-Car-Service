using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.WebSite;
using RentACar.Domain.Entities;
using RentACar.Web.Identity;

namespace RentACar.Web.WebSite;

/// <summary>
/// PR-12/13: "Web Sitesi" modülünün form-POST uçları — ilan sihirbazının üç adımı.
///
/// İKİ KATMANLI KAPI: <see cref="AuthExtensions.RequireWebSitesiModulu"/> (satın alma, platform
/// kararı) + <see cref="Permission.OperationsWrite"/> (rol). Yeni bir <c>Permission</c> enum değeri
/// EKLENMEDİ (matris testlerini dalgalandırır); daraltma <c>ScreenPermissionService</c>'in
/// "web-sitesi" ekran kodu ile yapılır.
///
/// SİHİRBAZ = TASLAK-KAYIT + PRG. Her adım kendi POST'unu yapar ve bir sonraki adıma REDIRECT eder;
/// yenileme/geri-tuşu/çift-submit doğal olarak güvenli. Gizli alanlarla durum taşımak 200 araçlık
/// filoda form limitini (ValueCountLimit=1024) aşardı.
/// </summary>
public static class WebSiteEndpoints
{
    public static IEndpointRouteBuilder MapWebSiteEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/web-sitesi")
            .RequirePermission(Permission.OperationsWrite)
            .RequireWebSitesiModulu()
            .AntiforgeryByEnv();

        // ---- Adım 1: araç seçimi → TASLAK ilan(lar) ----
        grp.MapPost("/ilan/olustur", async (WebIlanService svc, HttpRequest req) =>
        {
            var ayri = req.Form["mod"].ToString() == "ayri"; // varsayılan: beraber
            try
            {
                // Beraber modda form İMZA gönderir (bir satır = bir model kümesi), ayrı modda ARAÇ ID'si.
                var ilanId = ayri
                    ? await svc.AdimBirAsync(
                        [.. req.Form["aracId"].Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty)],
                        beraber: false)
                    : await svc.AdimBirImzaAsync([.. req.Form["imza"].Select(s => s ?? "").Where(s => s.Length > 0)]);
                return Results.Redirect($"/web-sitesi/ilan/{ilanId}/fiyat");
            }
            catch (ValidationException ex)
            {
                return Geri("/web-sitesi/arac-ekle" + (ayri ? "?mod=ayri" : ""), ex);
            }
        });

        // ---- Adım 2: fiyat ----
        grp.MapPost("/ilan/{id:guid}/fiyat/kaydet", async (WebIlanService svc, Guid id, HttpRequest req) =>
        {
            var f = req.Form;
            try
            {
                await svc.AdimIkiAsync(id,
                    FormParse.Dec(FormParse.Str(f, "gunlukFiyat")) ?? 0m,
                    FormParse.Dec(FormParse.Str(f, "haftalikToplam")),
                    FormParse.Dec(FormParse.Str(f, "aylikToplam")),
                    f["kdvDahil"].ToString() != "false");
                return Results.Redirect($"/web-sitesi/ilan/{id}/ozellikler");
            }
            catch (ValidationException ex) { return Geri($"/web-sitesi/ilan/{id}/fiyat", ex); }
        });

        // ---- Adım 3: teknik özellikler → YAYINDA ----
        grp.MapPost("/ilan/{id:guid}/ozellikler/kaydet", async (WebIlanService svc, Guid id, HttpRequest req) =>
        {
            var f = req.Form;
            // Satırlar paralel dizilerle gelir: etiket[i] / deger[i] / gorunur (checkbox → index listesi).
            var etiketler = f["etiket"];
            var degerler = f["deger"];
            var gorunurler = f["gorunur"].Select(s => s ?? "").ToHashSet(StringComparer.Ordinal);

            var satirlar = new List<OzellikSatiri>();
            for (var i = 0; i < etiketler.Count; i++)
            {
                var etiket = etiketler[i] ?? "";
                var deger = i < degerler.Count ? degerler[i] ?? "" : "";
                if (string.IsNullOrWhiteSpace(etiket) || string.IsNullOrWhiteSpace(deger)) continue;
                satirlar.Add(new OzellikSatiri(etiket, deger, gorunurler.Contains(i.ToString())));
            }

            try
            {
                await svc.AdimUcAsync(id, satirlar);
                return Results.Redirect("/web-sitesi?ok=1");
            }
            catch (ValidationException ex) { return Geri($"/web-sitesi/ilan/{id}/ozellikler", ex); }
        });

        // ---- Yönetim ----
        grp.MapPost("/ilan/{id:guid}/durum", async (WebIlanService svc, Guid id, [FromForm] string durum) =>
        {
            try
            {
                await svc.SetDurumAsync(id, durum == "pasif" ? WebIlanDurum.Pasif : WebIlanDurum.Yayinda);
                return Results.Redirect("/web-sitesi?ok=1");
            }
            catch (ValidationException ex) { return Geri("/web-sitesi", ex); }
        });

        grp.MapPost("/ilan/{id:guid}/sil", async (WebIlanService svc, Guid id) =>
        {
            try { await svc.SilAsync(id); return Results.Redirect("/web-sitesi?ok=1"); }
            catch (ValidationException ex) { return Geri("/web-sitesi", ex); }
        });

        return app;
    }

    private static IResult Geri(string yol, ValidationException ex)
        => Results.Redirect(yol + (yol.Contains('?') ? "&" : "?") + "hata=" + Uri.EscapeDataString(ex.Message));
}
