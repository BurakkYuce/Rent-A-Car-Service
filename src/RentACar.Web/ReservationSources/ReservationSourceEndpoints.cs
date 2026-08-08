using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.ReservationSources;
using RentACar.Web.Identity;

namespace RentACar.Web.ReservationSources;

/// <summary>Rezervasyon kaynağı master form post uçları. OperationsWrite.</summary>
public static class ReservationSourceEndpoints
{
    public static IEndpointRouteBuilder MapReservationSourceEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/rezervasyon-kaynaklari").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (ReservationSourceService svc, HttpRequest req,
            [FromForm] string kod, [FromForm] string ad) =>
            await Run(() => svc.CreateAsync(Build(req.Form, kod, ad, aktif: true))));

        grp.MapPost("/update", async (ReservationSourceService svc, HttpRequest req, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form, kod, ad, aktif))));

        grp.MapPost("/delete", async (ReservationSourceService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        // FAZ-24 "Aşağıya Yansıt" — seçili kaynağın oranlarını diğer AKTİF kaynaklara kopyalar.
        // Yalnız bu tabloya yazar; kayıtlı rezervasyon/fatura/defter DEĞİŞMEZ.
        grp.MapPost("/yansit", async (ReservationSourceService svc, [FromForm] Guid id) =>
            await Run(() => svc.OranlariYansitAsync(id)));

        return app;
    }

    /// <summary>Oran alanları OPSİYONEL decimal — boş string ("") gelirse [FromForm] 400 verirdi,
    /// bu yüzden string olarak alınıp FormParse.Dec ile çevriliyor (CLAUDE.md §5 tuzağı).</summary>
    private static ReservationSourceInput Build(IFormCollection f, string kod, string ad, bool aktif) => new()
    {
        Kod = kod,
        Ad = ad,
        Aktif = aktif,
        Tedarikci = FormParse.Str(f, "tedarikci"),
        KiraOrani = FormParse.Dec(FormParse.Str(f, "kiraOrani")),
        HizmetOrani = FormParse.Dec(FormParse.Str(f, "hizmetOrani")),
        DropOrani = FormParse.Dec(FormParse.Str(f, "dropOrani"))
    };

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/rezervasyon-kaynaklari"); }
        catch (ValidationException ex) { return Results.Redirect($"/rezervasyon-kaynaklari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
