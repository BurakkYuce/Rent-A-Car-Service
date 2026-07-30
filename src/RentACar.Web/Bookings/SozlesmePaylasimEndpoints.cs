using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Web.Identity;
using RentACar.Web.Reports;

namespace RentACar.Web.Bookings;

/// <summary>
/// PR-C — paylaşım linki yönetim uçları (<b>girişli</b>: paylaş / yeni sürüm / iptal).
///
/// <para><b>Anlık görüntü BURADA üretilir.</b> <c>SozlesmeService.GetAsync</c> → <c>PdfExportService.Contract</c>
/// → <c>SozlesmePaylasimService.PaylasAsync(bytes)</c>. QuestPDF Application katmanına girmez; kira
/// sözleşmesi çıktısı <c>/kiralar/{id}/pdf</c> ile AYNI renderer'dan gelir (tek kaynak — müşteriye
/// giden nüsha ile personelin bastığı nüsha ayrışamaz).</para>
///
/// <para>Yetki: <see cref="Permission.OperationsWrite"/> — servis içinde de aynı guard var (çift savunma).</para>
/// </summary>
public static class SozlesmePaylasimEndpoints
{
    public static IEndpointRouteBuilder MapSozlesmePaylasimEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/kiralar")
            .RequirePermission(Permission.OperationsWrite)
            .AntiforgeryByEnv();

        grp.MapPost("/{id:guid}/paylas", (Guid id, SozlesmeService sozlesme, PdfExportService pdf,
            SozlesmePaylasimService paylasim, CancellationToken ct)
            => Isle(id, sozlesme, pdf, ct, (no, bytes) => paylasim.PaylasAsync(id, no, bytes, ct)));

        // Bayat anlık görüntüyü tazeler: ESKİ token ölür, YENİ token doğar. Otomatik yenileme
        // bilinçli yok — müşterinin elindeki belge sessizce değişmemeli.
        grp.MapPost("/{id:guid}/paylas-yeni", (Guid id, SozlesmeService sozlesme, PdfExportService pdf,
            SozlesmePaylasimService paylasim, CancellationToken ct)
            => Isle(id, sozlesme, pdf, ct, (no, bytes) => paylasim.YeniSurumAsync(id, no, bytes, ct)));

        grp.MapPost("/{id:guid}/paylas-iptal", async (Guid id, SozlesmePaylasimService paylasim,
            CancellationToken ct) =>
        {
            try
            {
                var vardi = await paylasim.IptalEtAsync(id, ct);
                return Sonra(id, vardi ? "Paylaşım linki iptal edildi." : "Aktif paylaşım linki yok.");
            }
            catch (ValidationException ex) { return Hata(id, ex); }
        });

        return app;
    }

    /// <summary>Paylaş ve yeni-sürüm aynı zinciri kullanır; tek fark çağrılan servis metodu.</summary>
    private static async Task<IResult> Isle(Guid id, SozlesmeService sozlesme, PdfExportService pdf,
        CancellationToken ct, Func<string, byte[], Task<PaylasimDurum>> islem)
    {
        try
        {
            var s = await sozlesme.GetAsync(id, ct);
            // Yok VEYA bu kullanıcının kapsamında değil (şube/tenant) → listeye geri.
            // Çıplak `Results.NotFound()` DÖNDÜRÜLMEZ: `UseStatusCodePagesWithReExecute("/not-found")`
            // boş-gövdeli 404'ü POST olarak yeniden çalıştırıyor, o da antiforgery'e takılıp istemciye
            // 400 "antiforgery token" hatası döndürüyor — yani izolasyon doğru çalışsa bile kullanıcı
            // alakasız bir hata görüyor. (Aynı tuzak `/internal/alert` içinde de belgeli.) Form POST'unun
            // doğru cevabı PRG: anlaşılır mesajla geri dön.
            if (s is null) return Results.Redirect(
                "/kiralar?hata=" + Uri.EscapeDataString("Kira sözleşmesi bulunamadı."));
            await islem(s.SozlesmeNo, pdf.Contract(s));
            return Sonra(id, "Paylaşım linki hazır.");
        }
        catch (ValidationException ex) { return Hata(id, ex); }
    }

    // Statik SSR: POST sonrası kira ekranına dön (PRG deseni), sekme paylaşım barının olduğu yer.
    private static IResult Sonra(Guid id, string mesaj)
        => Results.Redirect($"/kiralar/{id}?paylasim={Uri.EscapeDataString(mesaj)}");

    private static IResult Hata(Guid id, ValidationException ex)
        => Results.Redirect($"/kiralar/{id}?hata={Uri.EscapeDataString(ex.Message)}");
}
