using Microsoft.Net.Http.Headers;
using RentACar.Application.PlatformBelgeler;

namespace RentACar.Web.Documents;

/// <summary>
/// PR-B — tenant tarafı belge indirme. <b>SALT-OKUR</b>; yazma uçları platform tarafında.
///
/// <para><b>Dört koşulun tamamı sağlanmadan içerik dönmez</b> (bkz. <see cref="IPlatformDocumentRepository"/>):
/// (1) oturum açık — bu grubun <c>RequireAuthorization()</c>'ı, (2) belge yayında, (3) global ya da bu
/// tenant'a hedefli, (4) herkese açık ya da kullanıcı yönetici. (2)-(4) servis/repo yüklemi.
/// <see cref="PlatformBelge"/> bir PLATFORM tablosu olduğu için RLS burada KORUMAZ — kontrol tamamen
/// uygulama katmanındadır ve bu PR'ın en kritik hata sınıfı budur.</para>
///
/// <para>Yetkisiz istek <b>404</b> alır, 403 değil: belgenin var olduğu bilgisi de sızmamalı.</para>
///
/// <para>Yeni bir <c>Permission</c> değeri EKLENMEDİ — belgeleri (KVKK metni, kılavuz) oturum açmış
/// tüm personel görmeli; daraltma belge başına <c>YalnizYoneticiler</c> bayrağıyla.</para>
/// </summary>
public static class CompanyDocumentEndpoints
{
    public static IEndpointRouteBuilder MapCompanyDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/firma-belgeleri").RequireAuthorization();

        grp.MapGet("/{id:guid}/indir", async (Guid id, PlatformDocumentService svc, HttpRequest req,
            HttpResponse res, CancellationToken ct) =>
        {
            var content = await svc.DownloadAsync(id, ct);
            if (content is null) return Results.NotFound(); // yok VEYA yetkisiz — ayırt edilmez

            // ETag = belge + SÜRÜM: platform yeni sürüm yükleyince değişir → tarayıcı tazeler.
            // Foto ucundaki desen (VehiclePhotoEndpoints:62-76), ama Cache-Control `private`:
            // belge tenant'a özel olabilir, paylaşımlı ara-cache'e düşmemeli.
            var etag = $"\"{id}v{content.Surum}\"";
            if (EntityTagHeaderValue.TryParseList(req.Headers.IfNoneMatch, out var reqTags)
                && reqTags.Any(t => t.Tag == etag))
                return Results.StatusCode(StatusCodes.Status304NotModified);

            res.Headers.CacheControl = "private, max-age=3600";

            // `?indir=1` → attachment (diske kaydet); varsayılan → **inline** (tarayıcının PDF
            // görüntüleyicisi açılır, oradan yazdırılır). Duman testinde yakalandı: `Results.File`
            // dosya adı verildiğinde DAİMA `attachment` yazıyor, bu da sayfadaki "Görüntüle"
            // bağlantısını sessizce bir indirmeye çeviriyordu.
            // `DosyaAdi` servis tarafında ASCII'ye slug'lanmış → başlıkta tırnak içi güvenli.
            var isDownload = req.Query.ContainsKey("indir");
            if (!isDownload) res.Headers.ContentDisposition = $"inline; filename=\"{content.DosyaAdi}\"";

            return Results.File(content.Bytes, "application/pdf",
                fileDownloadName: isDownload ? content.DosyaAdi : null,
                entityTag: new EntityTagHeaderValue(etag));
        });

        return app;
    }
}
