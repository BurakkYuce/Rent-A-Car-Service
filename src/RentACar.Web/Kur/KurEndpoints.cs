using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Kur;
using RentACar.Web.Identity;

namespace RentACar.Web.Kur;

/// <summary>Kur uçları: TCMB yenile + sabit kur kaydet/sil. Yazma → FinanceWrite + antiforgery.</summary>
public static class KurEndpoints
{
    public static IEndpointRouteBuilder MapKurEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/kurlar").RequirePermission(Permission.FinanceWrite).AntiforgeryByEnv();

        grp.MapPost("/yenile", async (TcmbKurService svc) =>
        {
            var n = await svc.RefreshAsync();
            return Results.Redirect(n > 0 ? "/kurlar?ok=1"
                : $"/kurlar?hata={Uri.EscapeDataString("TCMB kuru çekilemedi (bağlantı?).")}");
        });

        grp.MapPost("/sabit/kaydet", async (SabitKurService svc, HttpRequest req) =>
        {
            try
            {
                var f = req.Form;
                await svc.UpsertAsync(new SabitKurInput
                {
                    Kod = f["kod"].ToString(),
                    Kur = FormParse.Dec(f["kur"].ToString()) ?? 0m,
                    // date-picker → TAKVİM günü (UTC gün başı; FormParse.Date yerel-kaymasını KULLANMA — gün kayabilir)
                    BasTar = Gun(f["basTar"].ToString()),
                    BitTar = Gun(f["bitTar"].ToString()),
                    Aktif = f["aktif"].ToString() == "true"
                });
                return Results.Redirect("/kurlar?ok=1");
            }
            catch (ValidationException ex) { return Results.Redirect($"/kurlar?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/sabit/sil", async (SabitKurService svc, [FromForm] Guid id) =>
        {
            await svc.DeleteAsync(id);
            return Results.Redirect("/kurlar?ok=1");
        });

        return app;
    }

    /// <summary>date-picker "yyyy-MM-dd" → o takvim gününün UTC başı (yerel-offset kaymasız).</summary>
    private static DateTimeOffset? Gun(string s)
        => DateOnly.TryParse(s?.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero)
            : null;
}
