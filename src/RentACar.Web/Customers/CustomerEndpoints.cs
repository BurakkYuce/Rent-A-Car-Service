using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.Customers;

/// <summary>
/// Cari create/update/delete form post uçları. Tenant HttpContext claim'inden (RLS).
/// Opsiyonel tarih alanları (ehliyetTarihi, riskTarihi) boş "" ile 400 vermesin diye
/// <c>string?</c> alınıp FormParse.Date ile çevrilir.
/// NOT: PR #1/#2 smoke kolaylığı için antiforgery devre dışı — üretimde açılmalı.
/// </summary>
public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/cariler").RequirePermission(Permission.OperationsWrite).DisableAntiforgery();

        group.MapPost("/create", async (CustomerService svc,
            [FromForm] CariType tip, [FromForm] string? ad, [FromForm] string? soyad, [FromForm] string? tcKimlik,
            [FromForm] string? unvan, [FromForm] string? vergiDairesi, [FromForm] string? vergiNo,
            [FromForm] string? cepTel, [FromForm] string? gsm2, [FromForm] string? email, [FromForm] string? il,
            [FromForm] string? ilce, [FromForm] string? adres, [FromForm] string? kaynak,
            [FromForm] string? musteriTemsilcisi, [FromForm] bool iysIzinli, [FromForm] bool uyari,
            [FromForm] string? uyariNedeni, [FromForm] string? ehliyetNo, [FromForm] string? ehliyetSinifi,
            [FromForm] string? ehliyetTarihi, [FromForm] string? ehliyetYeri, [FromForm] string? tarife,
            [FromForm] int vadeGun, [FromForm] decimal riskLimiti, [FromForm] string? riskMesaji,
            [FromForm] string? riskTarihi, [FromForm] string? hgsYansitmaTuru,
            [FromForm] bool karaListe, [FromForm] bool pasif) =>
        {
            var input = Build(tip, ad, soyad, tcKimlik, unvan, vergiDairesi, vergiNo, cepTel, gsm2, email, il, ilce,
                adres, kaynak, musteriTemsilcisi, iysIzinli, uyari, uyariNedeni, ehliyetNo, ehliyetSinifi,
                ehliyetTarihi, ehliyetYeri, tarife, vadeGun, riskLimiti, riskMesaji, riskTarihi, hgsYansitmaTuru,
                karaListe, pasif);
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
            [FromForm] string? cepTel, [FromForm] string? gsm2, [FromForm] string? email, [FromForm] string? il,
            [FromForm] string? ilce, [FromForm] string? adres, [FromForm] string? kaynak,
            [FromForm] string? musteriTemsilcisi, [FromForm] bool iysIzinli, [FromForm] bool uyari,
            [FromForm] string? uyariNedeni, [FromForm] string? ehliyetNo, [FromForm] string? ehliyetSinifi,
            [FromForm] string? ehliyetTarihi, [FromForm] string? ehliyetYeri, [FromForm] string? tarife,
            [FromForm] int vadeGun, [FromForm] decimal riskLimiti, [FromForm] string? riskMesaji,
            [FromForm] string? riskTarihi, [FromForm] string? hgsYansitmaTuru,
            [FromForm] bool karaListe, [FromForm] bool pasif) =>
        {
            var input = Build(tip, ad, soyad, tcKimlik, unvan, vergiDairesi, vergiNo, cepTel, gsm2, email, il, ilce,
                adres, kaynak, musteriTemsilcisi, iysIzinli, uyari, uyariNedeni, ehliyetNo, ehliyetSinifi,
                ehliyetTarihi, ehliyetYeri, tarife, vadeGun, riskLimiti, riskMesaji, riskTarihi, hgsYansitmaTuru,
                karaListe, pasif);
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
        string? vergiNo, string? cepTel, string? gsm2, string? email, string? il, string? ilce, string? adres,
        string? kaynak, string? musteriTemsilcisi, bool iysIzinli, bool uyari, string? uyariNedeni,
        string? ehliyetNo, string? ehliyetSinifi, string? ehliyetTarihi, string? ehliyetYeri, string? tarife,
        int vadeGun, decimal riskLimiti, string? riskMesaji, string? riskTarihi, string? hgsYansitmaTuru,
        bool karaListe, bool pasif)
        => new()
        {
            Tip = tip, Ad = ad, Soyad = soyad, TcKimlik = tcKimlik,
            Unvan = unvan, VergiDairesi = vergiDairesi, VergiNo = vergiNo,
            CepTel = cepTel, Gsm2 = gsm2, Email = email, Il = il, Ilce = ilce, Adres = adres,
            Kaynak = kaynak, MusteriTemsilcisi = musteriTemsilcisi, IysIzinli = iysIzinli,
            Uyari = uyari, UyariNedeni = uyariNedeni,
            EhliyetNo = ehliyetNo, EhliyetSinifi = ehliyetSinifi,
            EhliyetTarihi = FormParse.Date(ehliyetTarihi), EhliyetYeri = ehliyetYeri,
            Tarife = tarife, VadeGun = vadeGun, RiskLimiti = riskLimiti,
            RiskMesaji = riskMesaji, RiskTarihi = FormParse.Date(riskTarihi),
            HgsYansitmaTuru = hgsYansitmaTuru,
            KaraListe = karaListe, Pasif = pasif
        };
}
