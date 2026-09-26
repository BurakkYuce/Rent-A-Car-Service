using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.WebSite;
using RentACar.Domain.Entities;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.WebSite;

/// <summary>
/// PR-12/13: "Web Sitesi" modülünün form-POST uçları — ilan sihirbazının üç adımı.
///
/// İKİ KATMANLI KAPI: <see cref="AuthExtensions.RequireWebsiteModule"/> (satın alma, platform
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
            .RequireWebsiteModule()
            .AntiforgeryByEnv();

        // ---- Adım 1: araç seçimi → TASLAK ilan(lar) ----
        grp.MapPost("/ilan/olustur", async (WebListingService svc, HttpRequest req) =>
        {
            var separate = req.Form["mod"].ToString() == "ayri"; // varsayılan: beraber
            try
            {
                // Beraber modda form İMZA gönderir (bir satır = bir model kümesi), ayrı modda ARAÇ ID'si.
                var listingId = separate
                    ? await svc.StepOneAsync(
                        [.. req.Form["aracId"].Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty)],
                        together: false)
                    : await svc.StepOneSignatureAsync([.. req.Form["imza"].Select(s => s ?? "").Where(s => s.Length > 0)]);
                return Result.Ok($"/web-sitesi/ilan/{listingId}/fiyat", "Kayıt oluşturuldu.");
            }
            catch (ValidationException ex)
            {
                return Back("/web-sitesi/arac-ekle" + (separate ? "?mod=ayri" : ""), ex);
            }
        });

        // ---- Adım 2: fiyat ----
        grp.MapPost("/ilan/{id:guid}/fiyat/kaydet", async (WebListingService svc, Guid id, HttpRequest req) =>
        {
            var f = req.Form;
            try
            {
                await svc.StepTwoAsync(id,
                    FormParse.Dec(FormParse.Str(f, "gunlukFiyat")) ?? 0m,
                    FormParse.Dec(FormParse.Str(f, "haftalikToplam")),
                    FormParse.Dec(FormParse.Str(f, "aylikToplam")),
                    f["kdvDahil"].ToString() != "false");
                return Result.Ok($"/web-sitesi/ilan/{id}/ozellikler", "Kaydedildi.");
            }
            catch (ValidationException ex) { return Back($"/web-sitesi/ilan/{id}/fiyat", ex); }
        });

        // ---- Adım 3: teknik özellikler → YAYINDA ----
        grp.MapPost("/ilan/{id:guid}/ozellikler/kaydet", async (WebListingService svc, Guid id, HttpRequest req) =>
        {
            var f = req.Form;
            // Satırlar paralel dizilerle gelir: etiket[i] / deger[i] / gorunur (checkbox → index listesi).
            var labels = f["etiket"];
            var values = f["deger"];
            var visibleItems = f["gorunur"].Select(s => s ?? "").ToHashSet(StringComparer.Ordinal);

            var rows = new List<OzellikSatiri>();
            for (var i = 0; i < labels.Count; i++)
            {
                var label = labels[i] ?? "";
                var value = i < values.Count ? values[i] ?? "" : "";
                if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value)) continue;
                rows.Add(new OzellikSatiri(label, value, visibleItems.Contains(i.ToString())));
            }

            try
            {
                // false = özellikler kaydedildi ama FOTOĞRAF olmadığı için taslakta kaldı. Sessizce
                // "yayınlandı" demek yalan olurdu: vitrin fotosuz ilanı hiç göstermiyor.
                if (await svc.StepThreeAsync(id, rows))
                    return Results.Redirect("/web-sitesi?ok=1");
                return Results.Redirect($"/web-sitesi/ilan/{id}/ozellikler?hata="
                    + Uri.EscapeDataString("Özellikler kaydedildi. Yayınlamak için en az bir fotoğraf ekleyin — "
                        + "fotoğrafsız ilan sitede görünmez."));
            }
            catch (ValidationException ex) { return Back($"/web-sitesi/ilan/{id}/ozellikler", ex); }
        });

        // ---- Fotoğraflar (adım-3 içinde) ----
        // Vitrinin yayın şartlarından biri "üye araçlardan en az birinin fotoğrafı var". Sihirbazda
        // yükleme yolu OLMADIĞI için personel, hub'daki "Foto yok" teşhisini görüp de çaresine
        // ulaşamıyordu (tek yol araç düzenleme ekranıydı). Yol artık sihirbazın içinde.
        grp.MapPost("/ilan/{id:guid}/foto", async (WebListingService svc, Guid id, IFormFile? foto) =>
        {
            var back = $"/web-sitesi/ilan/{id}/ozellikler";
            if (foto is null || foto.Length == 0) return Results.Redirect(back);
            using var ms = new MemoryStream();
            await foto.CopyToAsync(ms);
            try { await svc.AddPhotoAsync(id, ms.ToArray()); return Results.Redirect(back); }
            catch (ValidationException ex) { return Back(back, ex); }
        }).WithMetadata(new RequestSizeLimitAttribute(3_000_000)); // 2 MB foto + multipart payı (araç ucuyla aynı)

        grp.MapPost("/ilan/{id:guid}/foto/{vehicleId:guid}/{photoId:guid}/sil",
            async (WebListingService svc, Guid id, Guid vehicleId, Guid photoId) =>
        {
            var back = $"/web-sitesi/ilan/{id}/ozellikler";
            try { await svc.DeletePhotoAsync(id, vehicleId, photoId); return Results.Redirect(back); }
            catch (ValidationException ex) { return Back(back, ex); }
        });

        // ---- Yönetim ----
        grp.MapPost("/ilan/{id:guid}/durum", async (WebListingService svc, Guid id, [FromForm] string durum) =>
        {
            try
            {
                await svc.SetStatusAsync(id, durum == "pasif" ? WebIlanDurum.Pasif : WebIlanDurum.Yayinda);
                return Results.Redirect("/web-sitesi?ok=1");
            }
            catch (ValidationException ex) { return Back("/web-sitesi", ex); }
        });

        grp.MapPost("/ilan/{id:guid}/sil", async (WebListingService svc, Guid id) =>
        {
            try { await svc.DeleteAsync(id); return Results.Redirect("/web-sitesi?ok=1"); }
            catch (ValidationException ex) { return Back("/web-sitesi", ex); }
        });

        return app;
    }

    private static IResult Back(string path, ValidationException ex)
        => Results.Redirect(path + (path.Contains('?') ? "&" : "?") + "hata=" + Uri.EscapeDataString(ex.Message));
}
