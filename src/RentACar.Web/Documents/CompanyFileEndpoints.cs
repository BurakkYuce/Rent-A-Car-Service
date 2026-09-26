using Microsoft.Net.Http.Headers;
using RentACar.Application.FirmaDokumanlar;

namespace RentACar.Web.Documents;

/// <summary>
/// Firmanın kendi PDF dokümanlarının İNDİRME ucu (<c>/dokumanlar/{id}/indir</c>). Yeni arayüzün doküman listesi
/// (<c>/api/ui/v1</c>, <c>DocumentApi</c>) indirme adresi olarak bunu döner. Yükleme/silme <c>/api/ui/v1</c>'de
/// (Blazor form uçları F13'te kalktı).
///
/// <para><b>Kapı:</b> yalnız <c>RequireAuthorization()</c> — sahada çıktı alması gereken her personel (muhasebeci
/// dahil) indirebilmeli. Başka tenant'ın belgesi servis/RLS yüzünden null döner → 404.</para>
/// </summary>
public static class CompanyFileEndpoints
{
    public static IEndpointRouteBuilder MapCompanyFileEndpoints(this IEndpointRouteBuilder app)
    {
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
}
