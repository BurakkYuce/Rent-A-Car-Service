using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.Finance;

/// <summary>Depozito (emanet) al/iade/mahsup uçları (roadmap I3). FinanceWrite.</summary>
public static class DepositEndpoints
{
    public static IEndpointRouteBuilder MapDepositEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/depozito").RequirePermission(Permission.FinanceWrite).AntiforgeryByEnv();

        // FAZ-50: hesapId (spesifik kasa/banka) Run() tarafından okunur ve nakit bacağına yazılır.
        // Mahsup NAKİT DEĞİL (depozito → cari borcu) → hesap seçimi yok sayılır, doğru.
        grp.MapPost("/al", async (DepositService svc, HttpRequest req) =>
            await Run(req, (account, amount, depositAccount, currency, exchangeRate, key, accountId) =>
                svc.GetAsync(account, amount, depositAccount, currency, exchangeRate, null, key, accountId)));

        grp.MapPost("/iade", async (DepositService svc, HttpRequest req) =>
            await Run(req, (account, amount, depositAccount, currency, exchangeRate, key, accountId) =>
                svc.RefundAsync(account, amount, depositAccount, currency, exchangeRate, null, key, accountId)));

        grp.MapPost("/mahsup", async (DepositService svc, HttpRequest req) =>
            await Run(req, (account, amount, _, currency, exchangeRate, key, _) => svc.OffsetAsync(account, amount, currency, exchangeRate, null, key)));

        // İRAT (FAZ 1.2): iade edilmeyen depozito GELİR olur; rentalId verilirse araca atfedilir.
        grp.MapPost("/irat", async (DepositService svc, HttpRequest req) =>
        {
            var f = req.Form;
            string? S(string k) { var v = f[k].ToString(); return string.IsNullOrWhiteSpace(v) ? null : v; }
            var account = FormParse.Id(S("cariId")) ?? Guid.Empty;
            var amount = FormParse.Dec(S("tutar")) ?? 0m;
            var currency = S("doviz") ?? "TRY";
            var exchangeRate = FormParse.Dec(S("kur")); // boş → otomatik (1.1)
            var rentalId = FormParse.Id(S("rentalId"));
            var key = FormParse.Id(S("islemAnahtari"));
            var description = S("aciklama");
            var returnInfo = S("donus");
            try
            {
                await svc.ForfeitAsync(account, amount, currency, exchangeRate, rentalId, null, key, description);
                return Results.Redirect(FinanceEndpoints.SafeReturn(returnInfo, "/depozito?ok=1"));
            }
            catch (ValidationException ex)
            { return Results.Redirect($"{FinanceEndpoints.SafeReturn(returnInfo, "/depozito")}?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        return app;
    }

    private static async Task<IResult> Run(HttpRequest req,
        Func<Guid, decimal, LedgerAccountType, string?, decimal?, Guid?, Guid?, Task<Guid>> action)
    {
        var f = req.Form;
        string? S(string k) { var v = f[k].ToString(); return string.IsNullOrWhiteSpace(v) ? null : v; }
        var account = FormParse.Id(S("cariId")) ?? Guid.Empty;
        var amount = FormParse.Dec(S("tutar")) ?? 0m;
        var depositAccount = string.Equals(S("hesap"), "Banka", StringComparison.OrdinalIgnoreCase) ? LedgerAccountType.Banka : LedgerAccountType.Kasa;
        var currency = S("doviz") ?? "TRY";
        var exchangeRate = FormParse.Dec(S("kur")); // boş → otomatik kur çözümü (1.1)
        var key = FormParse.Id(S("islemAnahtari"));
        var returnInfo = S("donus"); // kira formu gibi çağıran ekrana geri dönüş (SafeDonus: yalnız yerel yol)
        var accountId = FormParse.Id(S("hesapId")); // FAZ-50
        try { await action(account, amount, depositAccount, currency, exchangeRate, key, accountId); return Results.Redirect(FinanceEndpoints.SafeReturn(returnInfo, "/depozito?ok=1")); }
        catch (ValidationException ex) { return Results.Redirect($"{FinanceEndpoints.SafeReturn(returnInfo, "/depozito")}?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
