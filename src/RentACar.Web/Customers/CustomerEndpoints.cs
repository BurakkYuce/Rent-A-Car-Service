using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Domain.Enums;

namespace RentACar.Web.Customers;

/// <summary>
/// Cari create/update/delete form post uçları. Tenant HttpContext claim'inden (RLS).
/// NOT: PR #1/#2 smoke kolaylığı için antiforgery devre dışı — üretimde açılmalı.
/// </summary>
public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/cariler").RequireAuthorization().DisableAntiforgery();

        group.MapPost("/create", async (CustomerService svc,
            [FromForm] CariType tip, [FromForm] string? ad, [FromForm] string? soyad, [FromForm] string? tcKimlik,
            [FromForm] string? unvan, [FromForm] string? vergiDairesi, [FromForm] string? vergiNo,
            [FromForm] string? cepTel, [FromForm] string? email, [FromForm] string? il, [FromForm] string? ilce,
            [FromForm] string? adres, [FromForm] string? tarife, [FromForm] int vadeGun, [FromForm] decimal riskLimiti,
            [FromForm] bool karaListe, [FromForm] bool pasif) =>
        {
            var input = Build(tip, ad, soyad, tcKimlik, unvan, vergiDairesi, vergiNo, cepTel, email, il, ilce, adres, tarife, vadeGun, riskLimiti, karaListe, pasif);
            try
            {
                await svc.CreateAsync(input);
                return Results.Redirect("/cariler");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/cariler?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        group.MapPost("/update", async (CustomerService svc, [FromForm] Guid id,
            [FromForm] CariType tip, [FromForm] string? ad, [FromForm] string? soyad, [FromForm] string? tcKimlik,
            [FromForm] string? unvan, [FromForm] string? vergiDairesi, [FromForm] string? vergiNo,
            [FromForm] string? cepTel, [FromForm] string? email, [FromForm] string? il, [FromForm] string? ilce,
            [FromForm] string? adres, [FromForm] string? tarife, [FromForm] int vadeGun, [FromForm] decimal riskLimiti,
            [FromForm] bool karaListe, [FromForm] bool pasif) =>
        {
            var input = Build(tip, ad, soyad, tcKimlik, unvan, vergiDairesi, vergiNo, cepTel, email, il, ilce, adres, tarife, vadeGun, riskLimiti, karaListe, pasif);
            try
            {
                var ok = await svc.UpdateAsync(id, input);
                return ok ? Results.Redirect("/cariler") : Results.NotFound();
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/cariler/{id}?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        group.MapPost("/delete", async (CustomerService svc, [FromForm] Guid id) =>
        {
            await svc.DeleteAsync(id);
            return Results.Redirect("/cariler");
        });

        return app;
    }

    private static CustomerInput Build(
        CariType tip, string? ad, string? soyad, string? tcKimlik, string? unvan, string? vergiDairesi,
        string? vergiNo, string? cepTel, string? email, string? il, string? ilce, string? adres,
        string? tarife, int vadeGun, decimal riskLimiti, bool karaListe, bool pasif)
        => new()
        {
            Tip = tip, Ad = ad, Soyad = soyad, TcKimlik = tcKimlik,
            Unvan = unvan, VergiDairesi = vergiDairesi, VergiNo = vergiNo,
            CepTel = cepTel, Email = email, Il = il, Ilce = ilce, Adres = adres,
            Tarife = tarife, VadeGun = vadeGun, RiskLimiti = riskLimiti,
            KaraListe = karaListe, Pasif = pasif
        };
}
