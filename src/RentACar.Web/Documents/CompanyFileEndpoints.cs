using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.FirmaDokumanlar;
using RentACar.Web.Identity;

namespace RentACar.Web.Documents;

/// <summary>
/// Firmanın kendi PDF dokümanları (/dokumanlar).
///
/// <para><b>İki grup, iki kapı:</b> yükleme/silme <see cref="Permission.OperationsWrite"/>;
/// listeleme/indirme yalnız <c>RequireAuthorization()</c> — sahada çıktı alması gereken her
/// personel (muhasebeci dahil) indirebilmeli. Servis guard'ları aynı ayrımı ikinci kez yapar
/// (çift savunma); web tarafındaki kapı kaldırılsa bile servis reddeder.</para>
///
/// <para><b>Uçlar alt yolda</b> (/yukle, /sil, /{id}/indir): <c>@page "/dokumanlar"</c> ile aynı
/// yola MapPost eklemek AmbiguousMatchException (500) üretir — repoda kayıtlı tuzak.</para>
///
/// <para><b>POST'tan çıplak 404/400 DÖNÜLMEZ</b>: <c>UseStatusCodePagesWithReExecute</c> onu POST
/// olarak yeniden çalıştırır, kullanıcı alakasız bir antiforgery hatası görür. Form POST'unun
/// doğru cevabı PRG (<c>Redirect(...?hata=)</c>).</para>
/// </summary>
public static class CompanyFileEndpoints
{
    /// <summary>Servis sınırı (<see cref="CompanyFileService.MaxBytes"/>) + multipart zarfı ve metin
    /// alanları için 1 MB pay. Uçtaki bu kapı sunucuyu korur (sınır üstünü belleğe almadan reddeder),
    /// asıl karar SERVİSTE. Sabitten türetilir ki servis sınırı değişince burası sessizce dar kalmasın.</summary>
    private const long RequestSizeLimit = CompanyFileService.MaxBytes + 1024 * 1024;

    public static IEndpointRouteBuilder MapCompanyFileEndpoints(this IEndpointRouteBuilder app)
    {
        var write = app.MapGroup("/dokumanlar").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        write.MapPost("/yukle", async (CompanyFileService svc, HttpRequest req, IFormFile? dosya) =>
        {
            // Kestrel/multipart sınırı aşıldığında framework istisna atar; kullanıcıya PRG ile
            // NET mesaj dön — "413" ham sayfası değil.
            if (dosya is null || dosya.Length == 0) return Error("Dosya seçilmedi.");
            if (dosya.Length > CompanyFileService.MaxBytes)
                return Error($"Dosya en fazla {CompanyFileService.MaxBytes / (1024 * 1024)} MB olabilir.");

            using var ms = new MemoryStream();
            await dosya.CopyToAsync(ms);

            return await Run(() => svc.UploadAsync(new FirmaDokumanInput(
                Baslik: req.Form["baslik"].ToString(),
                Aciklama: FormParse.Str(req.Form, "aciklama"),
                DosyaAdi: dosya.FileName,
                Bytes: ms.ToArray())));
        }).WithMetadata(new RequestSizeLimitAttribute(RequestSizeLimit));

        write.MapPost("/sil", async (CompanyFileService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        // İndirme — yalnız oturum kapısı. Başka tenant'ın belgesi servis/RLS yüzünden null döner → 404
        // ("yok" ile "yetkisiz" ayırt edilmez; belgenin varlığı da bilgidir).
        var read = app.MapGroup("/dokumanlar").RequireAuthorization();

        read.MapGet("/{id:guid}/indir", async (Guid id, CompanyFileService svc, HttpRequest req,
            HttpResponse res, CancellationToken ct) =>
        {
            var content = await svc.DownloadAsync(id, ct);
            if (content is null) return Results.NotFound();

            // ETag = belge + son güncelleme tick'i (PlatformBelge'nin "belge + sürüm" deseninin
            // karşılığı; burada sürüm kolonu yok, dosya değişimi UpdatedAtUtc'yi taşır).
            // Cache-Control `private`: belge firmaya özeldir, paylaşımlı ara-cache'e DÜŞMEMELİ.
            var etag = $"\"{id}-{content.GuncellemeUtc.Ticks}\"";
            if (EntityTagHeaderValue.TryParseList(req.Headers.IfNoneMatch, out var tags)
                && tags.Any(t => t.Tag == etag))
                return Results.StatusCode(StatusCodes.Status304NotModified);

            res.Headers.CacheControl = "private, max-age=3600";

            // Talep gereği DAİMA attachment (sahada indirilip yazdırılacak dosyalar).
            // `Results.File` fileDownloadName verildiğinde zaten `attachment` yazar.
            return Results.File(content.Bytes, content.ContentType,
                fileDownloadName: content.DosyaAdi,
                entityTag: new EntityTagHeaderValue(etag));
        });

        return app;
    }

    private static IResult Error(string message) => Results.Redirect($"/dokumanlar?hata={Uri.EscapeDataString(message)}");

    private static async Task<IResult> Run(Func<Task> operation)
    {
        try { await operation(); return Results.Redirect("/dokumanlar?ok=1"); }
        catch (ValidationException ex) { return Error(ex.Message); }
    }
}
