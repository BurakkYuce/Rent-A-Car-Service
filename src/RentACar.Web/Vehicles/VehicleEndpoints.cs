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
        }).RequirePermission(Permission.OperationsDelete);

        // FAZ 2.5: manuel odometre girişi — km log + Vehicle.Km aynı transaction (geriye gitme reddi).
        group.MapPost("/km-log", async (VehicleService svc, [FromForm] Guid id, [FromForm] string? km) =>
        {
            try
            {
                await svc.ManuelKmGirAsync(id,
                    FormParse.Int(km) ?? throw new ValidationException("KM zorunludur."));
                return Results.Redirect($"/araclar/{id}");
            }
            catch (ValidationException ex)
            { return Results.Redirect($"/araclar/{id}?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        return app;
    }

    private static VehicleInput Build(IFormCollection f) => new()
    {
        Plaka = f["plaka"].ToString(),
        Marka = FormParse.Str(f, "marka"),
        Tip = FormParse.Str(f, "tip"),
        Grup = FormParse.Str(f, "grup"),
        // PR-10: form <select name="grup"> DAİMA post eder → anahtar var + değer boş demek,
        // kullanıcının "(Grupsuz)"u BİLİNÇLİ seçtiği demektir (varsayılan grup uygulanmaz).
        // Anahtarın hiç olmaması ise "belirtilmedi"dir (yalnız form-dışı çağrılar).
        GrupBilincliBos = f.ContainsKey("grup") && string.IsNullOrWhiteSpace(f["grup"].ToString()),
        VitrinAdet = FormParse.Int(FormParse.Str(f, "vitrinAdet")), // PR-11 (boş → null = 1)
        Segment = FormParse.Str(f, "segment"),
        Sipp = FormParse.Str(f, "sipp"),
        Renk = FormParse.Str(f, "renk"),
        ModelYili = FormParse.Int(FormParse.Str(f, "modelYili")),
        Vites = ParseEnum<Vites>(FormParse.Str(f, "vites")),
        SasiNo = FormParse.Str(f, "sasiNo"),
        MotorNo = FormParse.Str(f, "motorNo"),
        Sube = FormParse.Str(f, "sube"),
        Durum = ParseEnum<VehicleStatus>(FormParse.Str(f, "durum")) ?? VehicleStatus.Musait,
        FiloDurum = ParseEnum<FiloStatus>(FormParse.Str(f, "filoDurum")),
        Km = FormParse.Int(FormParse.Str(f, "km")) ?? 0,
        // PR-21: boş seçim artık NULL ("belirtilmedi"); eskiden sessizce Benzin'e düşüyordu.
        Yakit = ParseEnum<FuelType>(FormParse.Str(f, "yakit")),
        // Parite zenginleştirme
        MotorGucu = FormParse.Int(FormParse.Str(f, "motorGucu")),
        SilindirHacmi = FormParse.Int(FormParse.Str(f, "silindirHacmi")),
        RuhsatNo = FormParse.Str(f, "ruhsatNo"),
        TescilTarihi = FormParse.Date(FormParse.Str(f, "tescilTarihi")),
        AracSahibi = FormParse.Str(f, "aracSahibi"),
        AlimBedeli = FormParse.Dec(FormParse.Str(f, "alimBedeli")),
        AlimTarihi = FormParse.Date(FormParse.Str(f, "alimTarihi")),
        AlisVergisiz = FormParse.Dec(FormParse.Str(f, "alisVergisiz")),
        AlisOtv = FormParse.Dec(FormParse.Str(f, "alisOtv")),
        AlisKdv = FormParse.Dec(FormParse.Str(f, "alisKdv")),
        AylikMaliyet = FormParse.Dec(FormParse.Str(f, "aylikMaliyet")),
        FiloYonetimMaliyeti = FormParse.Dec(FormParse.Str(f, "filoYonetimMaliyeti")),
        IkinciElDeger = FormParse.Dec(FormParse.Str(f, "ikinciElDeger")),
        FiloGirisTarih = FormParse.Date(FormParse.Str(f, "filoGirisTarih")),
        FiloCikisTarih = FormParse.Date(FormParse.Str(f, "filoCikisTarih")),
        OzelKod1 = FormParse.Str(f, "ozelKod1"),
        OzelKod2 = FormParse.Str(f, "ozelKod2"),
        OzelKod3 = FormParse.Str(f, "ozelKod3"),
        OzelKod4 = FormParse.Str(f, "ozelKod4"),
        OzelKod5 = FormParse.Str(f, "ozelKod5"),
        // FAZ-28 detay alanları
        BelgeNo = FormParse.Str(f, "belgeNo"),
        RuhsatSahibi = FormParse.Str(f, "ruhsatSahibi"),
        SozNo = FormParse.Str(f, "sozNo"),
        AraciAlan = FormParse.Str(f, "araciAlan"),
        Kiralayan = FormParse.Str(f, "kiralayan"),
        AssistanFirma = FormParse.Str(f, "assistanFirma"),
        TsbKodu = FormParse.Str(f, "tsbKodu"),
        OdemeSekli = FormParse.Str(f, "odemeSekli"),
        PasifSebep = FormParse.Str(f, "pasifSebep"),
        SonDurum = FormParse.Str(f, "sonDurum"),
        HgsFirma = FormParse.Str(f, "hgsFirma"),
        SonTeslimKm = FormParse.Int(FormParse.Str(f, "sonTeslimKm")),
        KiraGun = FormParse.Int(FormParse.Str(f, "kiraGun")),
        DisKmLimit = FormParse.Int(FormParse.Str(f, "disKmLimit")),
        KiraFiyat = FormParse.Dec(FormParse.Str(f, "kiraFiyat")),
        TsbKaskoDegeri = FormParse.Dec(FormParse.Str(f, "tsbKaskoDegeri")),
        AlisEuroFiyat = FormParse.Dec(FormParse.Str(f, "alisEuroFiyat")),
        SatisEuroFiyat = FormParse.Dec(FormParse.Str(f, "satisEuroFiyat")),
        SonTeslimTarihi = FormParse.Date(FormParse.Str(f, "sonTeslimTarihi")),
        KiraBitTar = FormParse.Date(FormParse.Str(f, "kiraBitTar")),
        KiraBekTar = FormParse.Date(FormParse.Str(f, "kiraBekTar")),
        KiraMusteriId = FormParse.Id(FormParse.Str(f, "kiraMusteriId")),
        AlisEuro = FormParse.Str(f, "alisEuro") is "true" or "on" or "True" ? true : null,
        HgsNo = FormParse.Str(f, "hgsNo"),
        OgsNo = FormParse.Str(f, "ogsNo"),
        KasaTipi = FormParse.Str(f, "kasaTipi"),
        DetayTipi = FormParse.Str(f, "detayTipi"),
        AlimFaturaNo = FormParse.Str(f, "alimFaturaNo"),
        AlimYapilanFirma = FormParse.Str(f, "alimYapilanFirma"),
        KiraKmLimiti = FormParse.Int(FormParse.Str(f, "kiraKmLimiti")),
        // roadmap K2 — operasyon bayrakları + bakım/lastik
        WebRezKapat = Flag(f, "webRezKapat"),
        OfisRezKapat = Flag(f, "ofisRezKapat"),
        ZIzni = Flag(f, "zIzni"),
        Utts = Flag(f, "utts"),
        KarLastigi = Flag(f, "karLastigi"),
        YedekAnahtar = Flag(f, "yedekAnahtar"),
        Temizlik = Flag(f, "temizlik"),
        Rehin = Flag(f, "rehin"),
        SonBakimTarih = FormParse.Date(FormParse.Str(f, "sonBakimTarih")),
        SonBakimKm = FormParse.Int(FormParse.Str(f, "sonBakimKm")),
        LastikDurumu = FormParse.Str(f, "lastikDurumu"),
        // FAZ-10 araç kartı derinliği — opsiyonel sayı/tarih alanları string alınıp FormParse ile
        // çevrilir (boş "" değer-tipli bind'de 400 verirdi — CLAUDE.md §5 tuzağı).
        TsrbMarkaKodu = FormParse.Str(f, "tsrbMarkaKodu"),
        TsrbTipKodu = FormParse.Str(f, "tsrbTipKodu"),
        AltGrupAdi = FormParse.Str(f, "altGrupAdi"),
        EntegrasyonKodu = FormParse.Str(f, "entegrasyonKodu"),
        TeypKodu = FormParse.Str(f, "teypKodu"),
        TakipMarka = FormParse.Str(f, "takipMarka"),
        TakipNo = FormParse.Str(f, "takipNo"),
        SahipGrup = FormParse.Str(f, "sahipGrup"),
        AracSahibiNo = FormParse.Str(f, "aracSahibiNo"),
        AracSahibi2 = FormParse.Str(f, "aracSahibi2"),
        KrediFirma = FormParse.Str(f, "krediFirma"),
        KapatmaTarih = FormParse.Date(FormParse.Str(f, "kapatmaTarih")),
        CikmasiPlananTarih = FormParse.Date(FormParse.Str(f, "cikmasiPlananTarih")),
        AracSatisKm = FormParse.Int(FormParse.Str(f, "aracSatisKm")),
        Aciklama = FormParse.Str(f, "aciklama"),
        Konum = FormParse.Str(f, "konum"),
        AlimBedeliKur = FormParse.Dec(FormParse.Str(f, "alimBedeliKur")),
        Arac2FiyatKur = FormParse.Dec(FormParse.Str(f, "arac2FiyatKur")),
        SimdiKur = FormParse.Dec(FormParse.Str(f, "simdiKur")),
        AylikMaliyetDoviz = FormParse.Dec(FormParse.Str(f, "aylikMaliyetDoviz"))
    };

    /// <summary>Checkbox: değer "true"/"on" ise true; yoksa false.</summary>
    private static bool Flag(IFormCollection f, string key)
    {
        var v = f[key].ToString();
        return v is "true" or "on" or "True";
    }


    /// <summary>Boş/whitespace/geçersiz → null; aksi halde enum değeri.</summary>
    private static T? ParseEnum<T>(string? s) where T : struct, Enum
        => Enum.TryParse<T>((s ?? string.Empty).Trim(), out var v) ? v : null;
}
