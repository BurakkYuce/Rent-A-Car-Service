using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.Finance;

/// <summary>Depozito (emanet) al/iade/mahsup uçları (roadmap I3). FinanceWrite.</summary>
public static class DepozitoEndpoints
{
    public static IEndpointRouteBuilder MapDepozitoEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/depozito").RequirePermission(Permission.FinanceWrite).AntiforgeryByEnv();

        // FAZ-50: hesapId (spesifik kasa/banka) Run() tarafından okunur ve nakit bacağına yazılır.
        // Mahsup NAKİT DEĞİL (depozito → cari borcu) → hesap seçimi yok sayılır, doğru.
        grp.MapPost("/al", async (DepositService svc, HttpRequest req) =>
            await Run(req, (cari, tutar, hesap, doviz, kur, anahtar, hesapId) =>
                svc.GetAsync(cari, tutar, hesap, doviz, kur, null, anahtar, hesapId)));

        grp.MapPost("/iade", async (DepositService svc, HttpRequest req) =>
            await Run(req, (cari, tutar, hesap, doviz, kur, anahtar, hesapId) =>
                svc.RefundAsync(cari, tutar, hesap, doviz, kur, null, anahtar, hesapId)));

        grp.MapPost("/mahsup", async (DepositService svc, HttpRequest req) =>
            await Run(req, (cari, tutar, _, doviz, kur, anahtar, _) => svc.OffsetAsync(cari, tutar, doviz, kur, null, anahtar)));

        // İRAT (FAZ 1.2): iade edilmeyen depozito GELİR olur; rentalId verilirse araca atfedilir.
        grp.MapPost("/irat", async (DepositService svc, HttpRequest req) =>
        {
            var f = req.Form;
            string? S(string k) { var v = f[k].ToString(); return string.IsNullOrWhiteSpace(v) ? null : v; }
            var cari = FormParse.Id(S("cariId")) ?? Guid.Empty;
            var tutar = FormParse.Dec(S("tutar")) ?? 0m;
            var doviz = S("doviz") ?? "TRY";
            var kur = FormParse.Dec(S("kur")); // boş → otomatik (1.1)
            var rentalId = FormParse.Id(S("rentalId"));
            var anahtar = FormParse.Id(S("islemAnahtari"));
            var aciklama = S("aciklama");
            var donus = S("donus");
            try
            {
                await svc.ForfeitAsync(cari, tutar, doviz, kur, rentalId, null, anahtar, aciklama);
                return Results.Redirect(FinanceEndpoints.SafeDonus(donus, "/depozito?ok=1"));
            }
            catch (ValidationException ex)
            { return Results.Redirect($"{FinanceEndpoints.SafeDonus(donus, "/depozito")}?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        return app;
    }

    private static async Task<IResult> Run(HttpRequest req,
        Func<Guid, decimal, LedgerAccountType, string?, decimal?, Guid?, Guid?, Task<Guid>> action)
    {
        var f = req.Form;
        string? S(string k) { var v = f[k].ToString(); return string.IsNullOrWhiteSpace(v) ? null : v; }
        var cari = FormParse.Id(S("cariId")) ?? Guid.Empty;
        var tutar = FormParse.Dec(S("tutar")) ?? 0m;
        var hesap = string.Equals(S("hesap"), "Banka", StringComparison.OrdinalIgnoreCase) ? LedgerAccountType.Banka : LedgerAccountType.Kasa;
        var doviz = S("doviz") ?? "TRY";
        var kur = FormParse.Dec(S("kur")); // boş → otomatik kur çözümü (1.1)
        var anahtar = FormParse.Id(S("islemAnahtari"));
        var donus = S("donus"); // kira formu gibi çağıran ekrana geri dönüş (SafeDonus: yalnız yerel yol)
        var hesapId = FormParse.Id(S("hesapId")); // FAZ-50
        try { await action(cari, tutar, hesap, doviz, kur, anahtar, hesapId); return Results.Redirect(FinanceEndpoints.SafeDonus(donus, "/depozito?ok=1")); }
        catch (ValidationException ex) { return Results.Redirect($"{FinanceEndpoints.SafeDonus(donus, "/depozito")}?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
