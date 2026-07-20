using RentACar.Application.Authorization;
using RentACar.Application.TenantSettings;
using RentACar.Web.Identity;

namespace RentACar.Web.TenantSettings;

/// <summary>Ayarlar yazma ucu (roadmap D1). Hassas → ManageUsers (admin). Sır alanları boş gelirse
/// mevcut korunur (servis). IFormCollection (opsiyonel alanlar boş string → servis null'a çevirir).</summary>
public static class TenantSettingsEndpoints
{
    public static IEndpointRouteBuilder MapTenantSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/ayarlar").RequirePermission(Permission.ManageUsers).AntiforgeryByEnv();

        grp.MapPost("/kaydet", async (HttpRequest req, TenantSettingsService svc) =>
        {
            var f = req.Form;
            var m = new TenantSettingsModel
            {
                FirmaUnvan = f["firmaUnvan"].ToString(),
                FirmaVergiDairesi = f["firmaVergiDairesi"].ToString(),
                FirmaVergiNo = f["firmaVergiNo"].ToString(),
                FirmaAdres = f["firmaAdres"].ToString(),
                FirmaTel = f["firmaTel"].ToString(),
                FirmaEmail = f["firmaEmail"].ToString(),
                FirmaMobilTel = f["firmaMobilTel"].ToString(),
                FirmaMarka = f["firmaMarka"].ToString(),
                EFaturaKullanici = f["eFaturaKullanici"].ToString(),
                EFaturaSifre = f["eFaturaSifre"].ToString(),
                SmsBaslik = f["smsBaslik"].ToString(),
                SmsApiKey = f["smsApiKey"].ToString(),
                PosMerchantId = f["posMerchantId"].ToString(),
                PosApiKey = f["posApiKey"].ToString(),
                // roadmap M1
                LogoUrl = f["logoUrl"].ToString(),
                VarsayilanDoviz = f["varsayilanDoviz"].ToString(),
                VarsayilanKdvOrani = FormParse.Dec(f["varsayilanKdvOrani"].ToString()),
                MinKiraGun = FormParse.Int(f["minKiraGun"].ToString()),
                MaxKiraGun = FormParse.Int(f["maxKiraGun"].ToString()),
                RezOnayZorunlu = f["rezOnayZorunlu"].ToString() is "true" or "on",
                DonemselFaturalamaJob = f["donemselFaturalamaJob"].ToString() is "true" or "on", // FAZ 4.2-B4
                DonemselOtomatikTahsilat = f["donemselOtomatikTahsilat"].ToString() is "true" or "on",
                SmtpHost = f["smtpHost"].ToString(),
                SmtpPort = FormParse.Int(f["smtpPort"].ToString()),
                SmtpKullanici = f["smtpKullanici"].ToString(),
                SmtpSifre = f["smtpSifre"].ToString(),
                SmtpSsl = f["smtpSsl"].ToString() is "true" or "on",
                WhatsAppNumarasi = f["whatsAppNumarasi"].ToString(),
                WhatsAppGunlukOzet = f["whatsAppGunlukOzet"].ToString() is "true" or "on"
            };
            await svc.SaveAsync(m);
            return Results.Redirect("/ayarlar?ok=1");
        });

        // PR-C: PDF logo yükle (multipart). Yalnız PNG/JPG (magic-bytes doğrulaması) + ≤1 MB (serviste de kontrol).
        grp.MapPost("/logo", async (IFormFile? logo, TenantSettingsService svc) =>
        {
            if (logo is null || logo.Length == 0) return Results.Redirect("/ayarlar?ok=1");
            using var ms = new MemoryStream();
            await logo.CopyToAsync(ms);
            var bytes = ms.ToArray();
            if (!GorselMi(bytes)) return Results.Redirect("/ayarlar?hata=" + Uri.EscapeDataString("Yalnız PNG/JPG yüklenebilir."));
            await svc.SetLogoAsync(bytes);
            return Results.Redirect("/ayarlar?ok=1");
        });

        grp.MapPost("/logo-sil", async (TenantSettingsService svc) =>
        {
            await svc.SetLogoAsync(null);
            return Results.Redirect("/ayarlar?ok=1");
        });

        return app;
    }

    /// <summary>PNG (89 50 4E 47) veya JPEG (FF D8 FF) magic-bytes — bozuk/uygunsuz dosya PDF'i çökertmesin.</summary>
    private static bool GorselMi(byte[] b) =>
        (b.Length >= 4 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) ||
        (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF);
}
