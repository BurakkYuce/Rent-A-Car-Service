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
        grp.MapPost("/ilan/olustur", async (WebListingService svc, HttpRequest req) =>
        {
            var ayri = req.Form["mod"].ToString() == "ayri"; // varsayılan: beraber
            try
            {
                // Beraber modda form İMZA gönderir (bir satır = bir model kümesi), ayrı modda ARAÇ ID'si.
                var ilanId = ayri
                    ? await svc.StepOneAsync(
                        [.. req.Form["aracId"].Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty)],
                        together: false)
                    : await svc.StepOneSignatureAsync([.. req.Form["imza"].Select(s => s ?? "").Where(s => s.Length > 0)]);
                return Sonuc.Tamam($"/web-sitesi/ilan/{ilanId}/fiyat", "Kayıt oluşturuldu.");
            }
            catch (ValidationException ex)
            {
                return Geri("/web-sitesi/arac-ekle" + (ayri ? "?mod=ayri" : ""), ex);
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
                return Sonuc.Tamam($"/web-sitesi/ilan/{id}/ozellikler", "Kaydedildi.");
            }
            catch (ValidationException ex) { return Geri($"/web-sitesi/ilan/{id}/fiyat", ex); }
        });

        // ---- Adım 3: teknik özellikler → YAYINDA ----
        grp.MapPost("/ilan/{id:guid}/ozellikler/kaydet", async (WebListingService svc, Guid id, HttpRequest req) =>
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
                // false = özellikler kaydedildi ama FOTOĞRAF olmadığı için taslakta kaldı. Sessizce
                // "yayınlandı" demek yalan olurdu: vitrin fotosuz ilanı hiç göstermiyor.
                if (await svc.StepThreeAsync(id, satirlar))
                    return Results.Redirect("/web-sitesi?ok=1");
                return Results.Redirect($"/web-sitesi/ilan/{id}/ozellikler?hata="
                    + Uri.EscapeDataString("Özellikler kaydedildi. Yayınlamak için en az bir fotoğraf ekleyin — "
                        + "fotoğrafsız ilan sitede görünmez."));
            }
            catch (ValidationException ex) { return Geri($"/web-sitesi/ilan/{id}/ozellikler", ex); }
        });

        // ---- Fotoğraflar (adım-3 içinde) ----
        // Vitrinin yayın şartlarından biri "üye araçlardan en az birinin fotoğrafı var". Sihirbazda
        // yükleme yolu OLMADIĞI için personel, hub'daki "Foto yok" teşhisini görüp de çaresine
        // ulaşamıyordu (tek yol araç düzenleme ekranıydı). Yol artık sihirbazın içinde.
        grp.MapPost("/ilan/{id:guid}/foto", async (WebListingService svc, Guid id, IFormFile? foto) =>
        {
            var geri = $"/web-sitesi/ilan/{id}/ozellikler";
            if (foto is null || foto.Length == 0) return Results.Redirect(geri);
            using var ms = new MemoryStream();
            await foto.CopyToAsync(ms);
            try { await svc.AddPhotoAsync(id, ms.ToArray()); return Results.Redirect(geri); }
            catch (ValidationException ex) { return Geri(geri, ex); }
        }).WithMetadata(new RequestSizeLimitAttribute(3_000_000)); // 2 MB foto + multipart payı (araç ucuyla aynı)

        grp.MapPost("/ilan/{id:guid}/foto/{vehicleId:guid}/{photoId:guid}/sil",
            async (WebListingService svc, Guid id, Guid vehicleId, Guid photoId) =>
        {
            var geri = $"/web-sitesi/ilan/{id}/ozellikler";
            try { await svc.DeletePhotoAsync(id, vehicleId, photoId); return Results.Redirect(geri); }
            catch (ValidationException ex) { return Geri(geri, ex); }
        });

        // ---- Yönetim ----
        grp.MapPost("/ilan/{id:guid}/durum", async (WebListingService svc, Guid id, [FromForm] string durum) =>
        {
            try
            {
                await svc.SetStatusAsync(id, durum == "pasif" ? WebIlanDurum.Pasif : WebIlanDurum.Yayinda);
                return Results.Redirect("/web-sitesi?ok=1");
            }
            catch (ValidationException ex) { return Geri("/web-sitesi", ex); }
        });

        grp.MapPost("/ilan/{id:guid}/sil", async (WebListingService svc, Guid id) =>
        {
            try { await svc.DeleteAsync(id); return Results.Redirect("/web-sitesi?ok=1"); }
            catch (ValidationException ex) { return Geri("/web-sitesi", ex); }
        });

        return app;
    }

    private static IResult Geri(string yol, ValidationException ex)
        => Results.Redirect(yol + (yol.Contains('?') ? "&" : "?") + "hata=" + Uri.EscapeDataString(ex.Message));
}
