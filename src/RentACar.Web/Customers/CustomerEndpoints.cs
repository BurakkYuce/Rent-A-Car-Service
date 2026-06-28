using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.Customers;

/// <summary>
/// Cari create/update/delete form post uçları. Tenant HttpContext claim'inden (RLS).
/// Çok sayıda opsiyonel alan (CRM parite zenginleştirme dahil) boş "" ile [FromForm] tipli bind 400
/// vermesin diye tüm form <see cref="IFormCollection"/>'dan okunup FormParse/Enum.TryParse ile çevrilir.
/// NOT: PR #1/#2 smoke kolaylığı için antiforgery devre dışı — üretimde açılmalı.
/// </summary>
public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/cariler").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        group.MapPost("/create", async (CustomerService svc, HttpRequest req) =>
        {
            try { await svc.CreateAsync(Build(req.Form)); return Results.Redirect("/cariler"); }
            catch (ValidationException ex) { return Results.Redirect($"/cariler?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        group.MapPost("/update", async (CustomerService svc, HttpRequest req, [FromForm] Guid id) =>
        {
            try
            {
                var ok = await svc.UpdateAsync(id, Build(req.Form));
                return ok ? Results.Redirect("/cariler") : Results.NotFound();
            }
            catch (ValidationException ex) { return Results.Redirect($"/cariler/{id}?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        group.MapPost("/delete", async (CustomerService svc, [FromForm] Guid id) =>
        {
            await svc.DeleteAsync(id);
            return Results.Redirect("/cariler");
        });

        return app;
    }

    private static CustomerInput Build(IFormCollection f) => new()
    {
        Tip = ParseEnum<CariType>(Str(f, "tip")) ?? CariType.Bireysel,
        Ad = Str(f, "ad"),
        Soyad = Str(f, "soyad"),
        TcKimlik = Str(f, "tcKimlik"),
        Unvan = Str(f, "unvan"),
        VergiDairesi = Str(f, "vergiDairesi"),
        VergiNo = Str(f, "vergiNo"),
        CepTel = Str(f, "cepTel"),
        Gsm2 = Str(f, "gsm2"),
        Email = Str(f, "email"),
        Il = Str(f, "il"),
        Ilce = Str(f, "ilce"),
        Adres = Str(f, "adres"),
        Kaynak = Str(f, "kaynak"),
        MusteriTemsilcisi = Str(f, "musteriTemsilcisi"),
        IysIzinli = BoolReq(f, "iysIzinli"),
        Uyari = BoolReq(f, "uyari"),
        UyariNedeni = Str(f, "uyariNedeni"),
        EhliyetNo = Str(f, "ehliyetNo"),
        EhliyetSinifi = Str(f, "ehliyetSinifi"),
        EhliyetTarihi = FormParse.Date(Str(f, "ehliyetTarihi")),
        EhliyetYeri = Str(f, "ehliyetYeri"),
        Tarife = Str(f, "tarife"),
        VadeGun = FormParse.Int(Str(f, "vadeGun")) ?? 0,
        RiskLimiti = FormParse.Dec(Str(f, "riskLimiti")) ?? 0m,
        RiskMesaji = Str(f, "riskMesaji"),
        RiskTarihi = FormParse.Date(Str(f, "riskTarihi")),
        HgsYansitmaTuru = Str(f, "hgsYansitmaTuru"),
        KaraListe = BoolReq(f, "karaListe"),
        Pasif = BoolReq(f, "pasif"),
        // CRM parite zenginleştirme
        Sinif = Str(f, "sinif"),
        MailIzin = BoolN(f, "mailIzin"),
        SmsIzin = BoolN(f, "smsIzin"),
        TelefonIzin = BoolN(f, "telefonIzin"),
        DogumTarihi = FormParse.Date(Str(f, "dogumTarihi")),
        BabaAdi = Str(f, "babaAdi"),
        AnaAdi = Str(f, "anaAdi"),
        PasaportNo = Str(f, "pasaportNo"),
        FaturaDonemi = Str(f, "faturaDonemi"),
        TevkifatOrani = FormParse.Dec(Str(f, "tevkifatOrani")),
        Yetkili1Ad = Str(f, "yetkili1Ad"),
        Yetkili1Tel = Str(f, "yetkili1Tel"),
        Yetkili1Mail = Str(f, "yetkili1Mail"),
        Yetkili2Ad = Str(f, "yetkili2Ad"),
        Yetkili2Tel = Str(f, "yetkili2Tel"),
        Yetkili2Mail = Str(f, "yetkili2Mail"),
        Yetkili3Ad = Str(f, "yetkili3Ad"),
        Yetkili3Tel = Str(f, "yetkili3Tel"),
        Yetkili3Mail = Str(f, "yetkili3Mail")
    };

    private static string? Str(IFormCollection f, string key)
    {
        var v = f[key].ToString();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    /// <summary>Checkbox: "true"/"on" işaretli → true; yoksa/boş → false.</summary>
    private static bool BoolReq(IFormCollection f, string key)
    {
        var v = Str(f, key);
        return v is "true" or "True" or "on";
    }

    /// <summary>3 durumlu nullable bool select: boş → null, "true" → true, diğer dolu → false.</summary>
    private static bool? BoolN(IFormCollection f, string key)
    {
        var v = Str(f, key);
        if (v is null) return null;
        return v is "true" or "True" or "on" or "evet" or "Evet";
    }

    /// <summary>Boş/whitespace/geçersiz → null; aksi halde enum değeri.</summary>
    private static T? ParseEnum<T>(string? s) where T : struct, Enum
        => Enum.TryParse<T>((s ?? string.Empty).Trim(), out var v) ? v : null;
}
