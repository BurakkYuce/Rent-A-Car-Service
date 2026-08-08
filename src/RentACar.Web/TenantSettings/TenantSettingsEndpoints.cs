using RentACar.Application.Authorization;
using RentACar.Application.TenantSettings;
using RentACar.Web.Identity;

namespace RentACar.Web.TenantSettings;

/// <summary>Ayarlar yazma ucu (roadmap D1). Hassas → ManageUsers (admin). Sır alanları boş gelirse
/// mevcut korunur (servis). IFormCollection (opsiyonel alanlar boş string → servis null'a çevirir).</summary>
public static class TenantSettingsEndpoints
{
    /// <summary>FAZ-81 — "Varsayılan" kutusu işaretliyse null, değilse seçilen renk.</summary>
    private static string? Renk(IFormCollection f, string alan)
    {
        var vars_ = f[alan + "Vars"];
        if (vars_.Count > 0 && vars_[^1] is "true" or "on" or "True") return null;
        var v = f[alan].ToString();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

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
                // FAZ-81 renk kodları. "Varsayılan" kutusu işaretliyse NULL gider —
                // type="color" boş gönderemediği için "varsayılana dön" ancak böyle ifade edilir.
                RenkGecikenler = Renk(f, "renkGecikenler"),
                RenkBugunDonecekler = Renk(f, "renkBugunDonecekler"),
                RenkBugunCikacaklar = Renk(f, "renkBugunCikacaklar"),
                RenkOpsiyonlu = Renk(f, "renkOpsiyonlu"),
                RenkLimitBakiye = Renk(f, "renkLimitBakiye"),
                RenkAlacakli = Renk(f, "renkAlacakli"),
                RenkRezAtananPlaka = Renk(f, "renkRezAtananPlaka"),
                RenkKiralanmayan = Renk(f, "renkKiralanmayan"),
                VarsayilanDoviz = f["varsayilanDoviz"].ToString(),
                VarsayilanKdvOrani = FormParse.Dec(f["varsayilanKdvOrani"].ToString()),
                VarsayilanGrupId = FormParse.Id(f["varsayilanGrupId"].ToString()), // PR-10
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
            // PR-A: kural TEK kaynakta (LogoKurallari) — tür + bayt + ölçü. Servis de AYNI kuralı
            // uyguluyor (derinlik); buradaki kontrol kullanıcıya hızlı/anlaşılır hata vermek için.
            // NOT: JPEG artık kabul edilmiyor — PNG-only (şeffaf zemin), bilinçli daraltma.
            if (RentACar.Application.Common.LogoKurallari.Reddet(bytes) is { } logoHata)
                return Results.Redirect("/ayarlar?hata=" + Uri.EscapeDataString(logoHata));
            await svc.SetLogoAsync(bytes);
            return Results.Redirect("/ayarlar?ok=1");
        });

        grp.MapPost("/logo-sil", async (TenantSettingsService svc) =>
        {
            await svc.SetLogoAsync(null);
            return Results.Redirect("/ayarlar?ok=1");
        });

        // PR-2: "Sitemi Aç" — subdomain host'u (idempotent) oluşturur + public-site'ı aktifleştirir.
        grp.MapPost("/site-ac", async (TenantSettingsService svc) =>
        {
            await svc.OpenPublicSiteAsync();
            return Results.Redirect("/ayarlar?ok=1");
        });

        // PR-5: özel domain ekle — Pending sınırı/host-çakışması ValidationException'a çevrilir, PRG'ye uygun.
        grp.MapPost("/domain-ekle", async (HttpRequest req, TenantSettingsService svc) =>
        {
            var host = req.Form["host"].ToString();
            try
            {
                await svc.AddCustomDomainAsync(host);
                return Results.Redirect("/ayarlar?ok=1");
            }
            catch (RentACar.Application.Common.ValidationException ex)
            {
                return Results.Redirect("/ayarlar?hata=" + Uri.EscapeDataString(ex.Message));
            }
        });

        return app;
    }
}
