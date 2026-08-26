using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.FinancialAccounts;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.FinancialAccounts;

/// <summary>Kasa/Banka hesap master form post uçları. OperationsWrite.</summary>
public static class FinancialAccountEndpoints
{
    public static IEndpointRouteBuilder MapFinancialAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/hesaplar").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (FinancialAccountService svc,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? tur, [FromForm] string? doviz,
            [FromForm] string? iban, [FromForm] string? hesapNo, [FromForm] string? banka, [FromForm] string? sube,
            [FromForm] string? hediyeCek, [FromForm] string? ozelKod, [FromForm] string? uyariMailListesi) =>
            await Run(() => svc.CreateAsync(new FinancialAccountInput
            { Kod = kod, Ad = ad, Tur = tur, Doviz = doviz, Iban = iban, HesapNo = hesapNo, Banka = banka, Sube = sube,
              HediyeCek = hediyeCek is "true" or "on" or "True", OzelKod = ozelKod, UyariMailListesi = uyariMailListesi,
              Aktif = true }), "Kayıt eklendi."));

        grp.MapPost("/update", async (FinancialAccountService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? tur, [FromForm] string? doviz,
            [FromForm] string? iban, [FromForm] string? hesapNo, [FromForm] string? banka, [FromForm] string? sube,
            [FromForm] string? hediyeCek, [FromForm] string? ozelKod, [FromForm] string? uyariMailListesi, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new FinancialAccountInput
            { Kod = kod, Ad = ad, Tur = tur, Doviz = doviz, Iban = iban, HesapNo = hesapNo, Banka = banka, Sube = sube,
              HediyeCek = hediyeCek is "true" or "on" or "True", OzelKod = ozelKod, UyariMailListesi = uyariMailListesi,
              Aktif = aktif }), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (FinancialAccountService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action, string mesaj)
    {
        try { await action(); return Sonuc.Tamam("/hesaplar", mesaj); }
        catch (ValidationException ex) { return Results.Redirect($"/hesaplar?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
