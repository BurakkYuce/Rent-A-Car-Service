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

    /// <summary>FAZ-82 — üç durumlu (yapılandırılmadı / Evet / Hayır) select → <c>bool?</c>.</summary>
    private static bool? UcDurumlu(IFormCollection f, string alan) => f[alan].ToString() switch
    {
        "true" => true,
        "false" => false,
        _ => null
    };

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
                // FAZ-82 fiyat/muhasebe varsayılanları. Hepsi opsiyonel sayısal/metin → boş string
                // gelince FormParse null'a çevirir (CLAUDE.md §5 tuzağı: [FromForm] int?/decimal? 400 verir).
                VarsayilanFiyatTuru = FormParse.Str(f, "varsayilanFiyatTuru"),
                VarsayilanYakitSeviyesi = FormParse.Int(f["varsayilanYakitSeviyesi"].ToString()),
                // Üç durumlu (yapılandırılmadı / Evet / Hayır) → bool?. Checkbox KULLANILMADI: işaretsiz
                // kutu "false" ile "hiç dokunulmadı"yı ayırt edemez; beklemede bir alanda bu ayrım önemli
                // (ileride motora bağlanırsa "kullanıcı bilinçli kapattı" bilgisi kaybolmasın).
                DropMesafeYokIseSifir = UcDurumlu(f, "dropMesafeYokIseSifir"),
                SaatFarkiToleransDk = FormParse.Int(f["saatFarkiToleransDk"].ToString()),
                IadeIslemSaatSiniri = FormParse.Int(f["iadeIslemSaatSiniri"].ToString()),
                KurElleGirisKilitli = f["kurElleGirisKilitli"].ToString() is "true" or "on",
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
            // FAZ-82: doğrulama hataları (KDV aralığı, fiyat türü, yakıt 0-12, negatif tolerans) artık
            // PRG ile forma döner. Önce yakalanmıyordu → tek yazım hatası genel hata sayfası veriyordu
            // ve kullanıcı hangi alanın reddedildiğini göremiyordu (/domain-ekle deseni).
            try
            {
                await svc.SaveAsync(m);
                return Results.Redirect("/ayarlar?ok=1");
            }
            catch (RentACar.Application.Common.ValidationException ex)
            {
                return Results.Redirect("/ayarlar?hata=" + Uri.EscapeDataString(ex.Message));
            }
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

        // WhatsApp TEST gönderimi — "yapılandırma doğru mu?" sorusunun tek dürüst cevabı gerçek
        // bir gönderimdir. Günlük özet job'ı yalnız sabah 08:00 sonrası ve günde BİR kez çalıştığı
        // için (idempotency) yapılandırmayı onunla denemek pratikte imkânsızdı.
        //
        // Gerçek gönderici yalnız Twilio config VARSA DI'ya giriyor; yoksa stub no-op döner ve
        // ekranda "yapılandırma yok" olarak görünür — sessiz başarı YOK.
        grp.MapPost("/whatsapp-test", async (HttpRequest req,
            RentACar.Application.Integrations.IWhatsAppService wa, IConfiguration cfg) =>
        {
            var no = req.Form["testNo"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(no))
                return Results.Redirect("/ayarlar?hata=" + Uri.EscapeDataString("Test için bir WhatsApp numarası girin (E.164, ör. +905321112233)."));

            // YAPILANDIRMA KAPISI — testin en kritik satırı. Twilio config yoksa DI'da StubWhatsAppService
            // duruyor ve o HER ZAMAN true döner (günlük özet job'ı "test modu"nda kayıt yazabilsin diye).
            // Bu kontrol olmadan buton hiçbir şey göndermediği hâlde "başarılı" diyordu — canlı denemede
            // yakalandı. Stub'ın dönüşünü değiştirmek yanlış olurdu (job'ın kayıt semantiğini bozar);
            // dürüst olması gereken yer BU uç.
            if (string.IsNullOrWhiteSpace(cfg["Twilio:AccountSid"]))
                return Results.Redirect("/ayarlar?hata=" + Uri.EscapeDataString(
                    "Twilio yapılandırılmamış — test modundasınız, hiçbir mesaj gönderilmedi. "
                    + "Twilio:AccountSid / AuthToken / WhatsAppFrom ayarlarını verin."));

            // Şablon adı ÜRETİMDEKİYLE aynı (operasyon_ozet) — test, gerçek kod yolunu denemeli.
            // Şablon SID'i tanımlıysa şablon gider; tanımlı değil ve AllowFreeform açıksa serbest
            // metin gider (sandbox yolu). İkisi de yoksa gönderici false döner ve bunu görürüz.
            var mesaj = $"RentPro test mesajı — {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC. Bu mesajı aldıysanız WhatsApp yapılandırmanız çalışıyor.";
            // Teşhis yolu SID'e ihtiyaç duyar (durum SID ile tekil sorulur) → GonderAsync.
            var twilioSvc = wa as RentACar.Web.Integrations.TwilioWhatsAppService;
            var pars = new Dictionary<string, string> { ["1"] = mesaj };
            var (ok, mesajSid) = twilioSvc is not null
                ? await twilioSvc.GonderAsync(no, "operasyon_ozet", pars)
                : (await wa.SendTemplateAsync(no, "operasyon_ozet", pars), null);
            if (!ok)
                return Results.Redirect("/ayarlar?hata=" + Uri.EscapeDataString(
                    "WhatsApp gönderilemedi. Twilio yapılandırmasını ve sunucu loglarını kontrol edin."));

            // TESLİM DOĞRULAMASI — 201 Created "kabul edildi" demek, "ulaştı" demek DEĞİL. WhatsApp
            // teslim hatası saniyeler içinde mesajın durumuna düşer. Canlı denemede birebir yaşandı:
            // uç "gönderildi" dedi, mesaj `failed / 63015` idi. Test butonunun tek işi "çalışıyor mu"
            // sorusuna dürüst cevap vermek olduğu için kısa bir yoklama yapılır.
            if (twilioSvc is { } twilio && mesajSid is { Length: > 0 })
            {
                // 9 x 1sn: canlı denemede teslim hatası (63015) ~5-8 sn içinde düştü; 3,5 sn'lik
                // ilk pencere ona yetişemeyip "belli değil" diyordu. Teşhis butonu için 9 sn kabul
                // edilebilir bir bekleme, yanlış "başarılı" demekten iyidir.
                for (var i = 0; i < 9; i++)
                {
                    await Task.Delay(1000);
                    var (durum, kod) = await twilio.SonDurumAsync(mesajSid);
                    if (durum is "failed" or "undelivered")
                        return Results.Redirect("/ayarlar?hata=" + Uri.EscapeDataString(
                            $"Mesaj Twilio'ya iletildi ama TESLİM EDİLEMEDİ ({durum}). "
                            + RentACar.Web.Integrations.TwilioWhatsAppService.HataAciklama(kod)));
                    if (durum is "delivered" or "read")
                        return Results.Redirect("/ayarlar?ok=1");
                }
                // Hâlâ kuyrukta: başarısız DEĞİL ama teslim de doğrulanmadı — ikisini karıştırma.
                return Results.Redirect("/ayarlar?hata=" + Uri.EscapeDataString(
                    "Mesaj Twilio'ya iletildi, teslim durumu henüz belli değil. "
                    + "Birkaç saniye sonra telefonu ve Twilio konsolunu kontrol edin."));
            }
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
