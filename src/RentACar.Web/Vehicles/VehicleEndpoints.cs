using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.Vehicles;

/// <summary>
/// Araç create/update/delete form post uçları. Tenant, HttpContext.User claim'inden
/// (ITenantContext → RLS) gelir; servis tenant'tan habersizdir.
/// Çok sayıda opsiyonel alan (parite zenginleştirme dahil) boş "" ile [FromForm] tipli bind 400
/// vermesin diye tüm form <see cref="IFormCollection"/>'dan okunup FormParse/Enum.TryParse ile çevrilir.
/// NOT: PR #1 smoke kolaylığı için antiforgery devre dışı — ÜRETİMDE açılmalı (follow-up).
/// </summary>
public static class VehicleEndpoints
{
    public static IEndpointRouteBuilder MapVehicleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/vehicles").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        group.MapPost("/create", async (VehicleService svc, HttpRequest req) =>
        {
            try { await svc.CreateAsync(Build(req.Form)); return Results.Redirect("/vehicles"); }
            catch (ValidationException ex) { return Results.Redirect($"/vehicles?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        group.MapPost("/update", async (VehicleService svc, HttpRequest req, [FromForm] Guid id) =>
        {
            try
            {
                var ok = await svc.UpdateAsync(id, Build(req.Form));
                return ok ? Results.Redirect("/vehicles") : Results.NotFound();
            }
            catch (ValidationException ex) { return Results.Redirect($"/vehicles/{id}?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        group.MapPost("/delete", async (VehicleService svc, [FromForm] Guid id) =>
        {
            await svc.DeleteAsync(id);
            return Results.Redirect("/vehicles");
        });

        return app;
    }

    private static VehicleInput Build(IFormCollection f) => new()
    {
        Plaka = f["plaka"].ToString(),
        Marka = Str(f, "marka"),
        Tip = Str(f, "tip"),
        Grup = Str(f, "grup"),
        Segment = Str(f, "segment"),
        Sipp = Str(f, "sipp"),
        Renk = Str(f, "renk"),
        ModelYili = FormParse.Int(Str(f, "modelYili")),
        Vites = ParseEnum<Vites>(Str(f, "vites")),
        SasiNo = Str(f, "sasiNo"),
        MotorNo = Str(f, "motorNo"),
        Sube = Str(f, "sube"),
        Durum = ParseEnum<VehicleStatus>(Str(f, "durum")) ?? VehicleStatus.Stokta,
        FiloDurum = ParseEnum<FiloStatus>(Str(f, "filoDurum")),
        Km = FormParse.Int(Str(f, "km")) ?? 0,
        Yakit = ParseEnum<FuelType>(Str(f, "yakit")) ?? FuelType.Benzin,
        // Parite zenginleştirme
        MotorGucu = FormParse.Int(Str(f, "motorGucu")),
        SilindirHacmi = FormParse.Int(Str(f, "silindirHacmi")),
        RuhsatNo = Str(f, "ruhsatNo"),
        TescilTarihi = FormParse.Date(Str(f, "tescilTarihi")),
        AracSahibi = Str(f, "aracSahibi"),
        AlimBedeli = FormParse.Dec(Str(f, "alimBedeli")),
        AlimTarihi = FormParse.Date(Str(f, "alimTarihi")),
        AlisVergisiz = FormParse.Dec(Str(f, "alisVergisiz")),
        AlisOtv = FormParse.Dec(Str(f, "alisOtv")),
        AlisKdv = FormParse.Dec(Str(f, "alisKdv")),
        AylikMaliyet = FormParse.Dec(Str(f, "aylikMaliyet")),
        FiloYonetimMaliyeti = FormParse.Dec(Str(f, "filoYonetimMaliyeti")),
        IkinciElDeger = FormParse.Dec(Str(f, "ikinciElDeger")),
        FiloGirisTarih = FormParse.Date(Str(f, "filoGirisTarih")),
        FiloCikisTarih = FormParse.Date(Str(f, "filoCikisTarih")),
        OzelKod1 = Str(f, "ozelKod1"),
        OzelKod2 = Str(f, "ozelKod2"),
        OzelKod3 = Str(f, "ozelKod3"),
        OzelKod4 = Str(f, "ozelKod4"),
        OzelKod5 = Str(f, "ozelKod5"),
        HgsNo = Str(f, "hgsNo"),
        OgsNo = Str(f, "ogsNo"),
        KasaTipi = Str(f, "kasaTipi"),
        DetayTipi = Str(f, "detayTipi"),
        AlimFaturaNo = Str(f, "alimFaturaNo"),
        AlimYapilanFirma = Str(f, "alimYapilanFirma"),
        KiraKmLimiti = FormParse.Int(Str(f, "kiraKmLimiti"))
    };

    private static string? Str(IFormCollection f, string key)
    {
        var v = f[key].ToString();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    /// <summary>Boş/whitespace/geçersiz → null; aksi halde enum değeri.</summary>
    private static T? ParseEnum<T>(string? s) where T : struct, Enum
        => Enum.TryParse<T>((s ?? string.Empty).Trim(), out var v) ? v : null;
}
