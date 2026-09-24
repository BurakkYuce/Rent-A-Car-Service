using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.TenantSettings;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Kira;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Sistem;

/// <summary>
/// F11.1b — <c>/ayarlar</c> (Blazor <c>Ayarlar.razor</c> + <c>TenantSettingsEndpoints</c> paritesi). Tümü
/// <see cref="Permission.ManageUsers"/> (grup kapısı + servis guard + "ayarlar" ekran kapısı).
/// <list type="bullet">
/// <item><b>SIR alanları</b> (e-Fatura şifresi, SMS/POS API anahtarı, SMTP şifresi) HİÇBİR yanıtta dönmez; yalnız
/// <c>*Tanimli</c> bayrağı (ham cipher'ın varlığından — çözülemeyen cipher da "tanımlı" sayılır). Yazmada boş gelirse
/// mevcut korunur (servis kuralı).</item>
/// <item><b>PUT tam değiştirme:</b> satır varsa <c>surum</c> zorunlu; karşılaştırma satır kilidi altında
/// (<see cref="ITenantSettingsVersionStore"/>), uyuşmazlık 409 <c>cakisma</c>.</item>
/// <item><b>Test gönderimleri</b> üretimdeki kod yolunu kullanır; yapılandırma yoksa ya da teslim doğrulanamadıysa
/// başarı DÖNMEZ (dürüst stub kuralı).</item>
/// </list>
/// </summary>
public static partial class SystemAdminApi
{
    /// <summary>Logo yükleme isteği üst sınırı: 1 MB logo + multipart payı.</summary>
    private const long LogoRequestLimit = 1_200_000;

    private const int SecretMaxLength = 256;

    private static void MapSettings(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/ayarlar").WithTags(SystemApiCommon.Tag).RequirePermission(Permission.ManageUsers);

        g.MapGet("", async (HttpContext http, CancellationToken ct) => TypedResults.Ok(await BuildSettingsAsync(http.RequestServices, ct)));

        g.MapPut("", async Task<Ok<SettingsDto>> (SettingsRequest i, HttpContext http, TenantSettingsService svc,
            IDbContextFactory<AppDbContext> f, CancellationToken ct) =>
        {
            if (await svc.VersionAsync(ct) is not null) SystemApiCommon.RequireVersion(i.Surum);
            ValidateSettings(i);
            if (i.VarsayilanGrupId is { } grup)
            {
                await using var db = await f.CreateDbContextAsync(ct);
                if (!await db.VehicleGroups.AsNoTracking().AnyAsync(x => x.Id == grup, ct))
                    throw new ValidationException("Varsayılan araç grubu bulunamadı.", "varsayilanGrupId");
            }
            await svc.SaveAsync(ToModel(i), SystemApiCommon.Clean(i.Surum), ct);
            return TypedResults.Ok(await BuildSettingsAsync(http.RequestServices, ct));
        }).AlanlariEsle(SettingsRules);

        // ---- PDF logosu
        g.MapGet("/logo", async Task<Results<FileContentHttpResult, ProblemHttpResult>> (HttpContext http, TenantSettingsService svc, CancellationToken ct) =>
        {
            var m = await svc.GetAsync(ct);
            if (m.LogoBytes is not { Length: > 0 } logo) return SystemApiCommon.NotFound("Logo yüklü değil.");
            http.Response.Headers.XContentTypeOptions = "nosniff";
            var type = ImageValidation.Detect(logo) == ImageKind.Jpeg ? "image/jpeg" : "image/png";
            return TypedResults.File(logo, type);
        }).Produces(StatusCodes.Status200OK, typeof(byte[]), "image/png", "image/jpeg").ProducesProblem(StatusCodes.Status404NotFound);

        g.MapPost("/logo", async Task<Ok<LogoDto>> (IFormFile? dosya, TenantSettingsService svc, CancellationToken ct) =>
        {
            if (dosya is null || dosya.Length == 0) throw new ValidationException("Logo dosyası seçilmedi.", "dosya");
            if (dosya.Length > LogoKurallari.MaxBayt) throw new ValidationException("Logo en fazla 1 MB olabilir.", "dosya");
            using var ms = new MemoryStream();
            await dosya.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();
            // Tür İÇERİKTEN (magic bytes) — istemcinin Content-Type'ına ve uzantısına güvenilmez. Servis aynı kuralı
            // ikinci kez uygular (derinlik).
            if (LogoKurallari.Reddet(bytes) is { } err) throw new ValidationException(err, "dosya");
            await svc.SetLogoAsync(bytes, ct);
            var size = PngBoyut.Oku(bytes);
            return TypedResults.Ok(new LogoDto(true, size?.Genislik, size?.Yukseklik, bytes.Length));
        }).DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(LogoRequestLimit)).AlanlariEsle(LogoRules);

        g.MapDelete("/logo", async Task<NoContent> (TenantSettingsService svc, CancellationToken ct) =>
        {
            await svc.SetLogoAsync(null, ct);
            return TypedResults.NoContent();
        });

        // ---- web sitesi alan adları
        g.MapPost("/web-sitesi-ac", async Task<Ok<SettingsDto>> (HttpContext http, TenantSettingsService svc, CancellationToken ct) =>
        {
            await svc.OpenPublicSiteAsync(ct);
            return TypedResults.Ok(await BuildSettingsAsync(http.RequestServices, ct));
        });

        g.MapPost("/domainler", async Task<Ok<SettingsDto>> (DomainAddRequest i, HttpContext http, TenantSettingsService svc, CancellationToken ct) =>
        {
            await svc.AddCustomDomainAsync(NormalizeCustomHost(i.Host), ct);
            return TypedResults.Ok(await BuildSettingsAsync(http.RequestServices, ct));
        }).AlanlariEsle(DomainRules);

        // F11.1b güvenlik M6 — DNS TXT sahiplik doğrulaması (ancak bundan sonra alan adı etkinleşir).
        g.MapPost("/domainler/dogrula", async Task<Ok<SettingsDto>> (DomainAddRequest i, HttpContext http, TenantSettingsService svc, CancellationToken ct) =>
        {
            await svc.VerifyCustomDomainAsync(NormalizeCustomHost(i.Host), ct);
            return TypedResults.Ok(await BuildSettingsAsync(http.RequestServices, ct));
        }).AlanlariEsle(DomainRules).RequireRateLimiting(SendTestRatePolicy);

        MapSendTests(g);
    }

    /// <summary>Sürüm alanlardan ÖNCE okunur: arada yazım olursa istemci eski sürümü alır ve PUT'u 409 olur (güvenli yön).</summary>
    private static async Task<SettingsDto> BuildSettingsAsync(IServiceProvider sp, CancellationToken ct)
    {
        var svc = sp.GetRequiredService<TenantSettingsService>();
        var version = await svc.VersionAsync(ct);
        var m = await svc.GetAsync(ct);
        var raw = await sp.GetRequiredService<ITenantSettingsRepository>().GetAsync(ct);
        var wa = await svc.ListWhatsAppGonderimAsync(7, ct);
        var cfg = sp.GetRequiredService<IConfiguration>();
        return new SettingsDto
        {
            FirmaUnvan = m.FirmaUnvan, FirmaVergiDairesi = m.FirmaVergiDairesi, FirmaVergiNo = m.FirmaVergiNo,
            FirmaAdres = m.FirmaAdres, FirmaTel = m.FirmaTel, FirmaEmail = m.FirmaEmail, FirmaMobilTel = m.FirmaMobilTel,
            FirmaMarka = m.FirmaMarka,
            EFaturaKullanici = m.EFaturaKullanici, EFaturaSifreTanimli = !string.IsNullOrEmpty(raw?.EFaturaSifreEnc),
            SmsBaslik = m.SmsBaslik, SmsApiKeyTanimli = !string.IsNullOrEmpty(raw?.SmsApiKeyEnc),
            PosMerchantId = m.PosMerchantId, PosApiKeyTanimli = !string.IsNullOrEmpty(raw?.PosApiKeyEnc),
            LogoUrl = m.LogoUrl, LogoVar = m.LogoBytes is { Length: > 0 },
            VarsayilanDoviz = m.VarsayilanDoviz, VarsayilanKdvOrani = m.VarsayilanKdvOrani, VarsayilanGrupId = m.VarsayilanGrupId,
            VarsayilanFiyatTuru = m.VarsayilanFiyatTuru, VarsayilanYakitSeviyesi = m.VarsayilanYakitSeviyesi,
            DropMesafeYokIseSifir = m.DropMesafeYokIseSifir, SaatFarkiToleransDk = m.SaatFarkiToleransDk,
            IadeIslemSaatSiniri = m.IadeIslemSaatSiniri, KurElleGirisKilitli = m.KurElleGirisKilitli,
            RenkGecikenler = m.RenkGecikenler, RenkBugunDonecekler = m.RenkBugunDonecekler, RenkBugunCikacaklar = m.RenkBugunCikacaklar,
            RenkOpsiyonlu = m.RenkOpsiyonlu, RenkLimitBakiye = m.RenkLimitBakiye, RenkAlacakli = m.RenkAlacakli,
            RenkRezAtananPlaka = m.RenkRezAtananPlaka, RenkKiralanmayan = m.RenkKiralanmayan,
            DonemselFaturalamaJob = m.DonemselFaturalamaJob, DonemselOtomatikTahsilat = m.DonemselOtomatikTahsilat,
            MinKiraGun = m.MinKiraGun, MaxKiraGun = m.MaxKiraGun, RezOnayZorunlu = m.RezOnayZorunlu,
            SmtpHost = m.SmtpHost, SmtpPort = m.SmtpPort, SmtpKullanici = m.SmtpKullanici,
            SmtpSifreTanimli = !string.IsNullOrEmpty(raw?.SmtpSifreEnc), SmtpSsl = m.SmtpSsl,
            SmtpGonderenAdres = m.SmtpGonderenAdres, SmtpGonderenAd = m.SmtpGonderenAd,
            FaturaSeriKodu = m.FaturaSeriKodu, WhatsAppNumarasi = m.WhatsAppNumarasi, WhatsAppGunlukOzet = m.WhatsAppGunlukOzet,
            WebSitesiAcik = m.PublicSiteEnabled, WebSitesiAdresi = m.PublicSiteHost,
            Domainler = m.CustomDomains.Select(d => new DomainDto(d.Host, d.Kind, d.Durum, d.DogrulamaKaydi, d.DogrulamaDegeri)).ToList(),
            YeniArayuzPilot = raw?.YeniArayuzPilot == true,
            TwilioTanimli = !string.IsNullOrWhiteSpace(cfg["Twilio:AccountSid"]),
            WhatsAppGonderimleri = wa.Select(x => new WhatsAppDeliveryDto(x.Gun, x.Tur, x.Alici, x.Basarili, x.HataMesaji,
                x.OlusturmaTarihi.ToUniversalTime())).ToList(),
            Surum = version,
        };
    }

    private static TenantSettingsModel ToModel(SettingsRequest i) => new()
    {
        FirmaUnvan = i.FirmaUnvan, FirmaVergiDairesi = i.FirmaVergiDairesi, FirmaVergiNo = i.FirmaVergiNo,
        FirmaAdres = i.FirmaAdres, FirmaTel = i.FirmaTel, FirmaEmail = i.FirmaEmail, FirmaMobilTel = i.FirmaMobilTel,
        FirmaMarka = i.FirmaMarka,
        EFaturaKullanici = i.EFaturaKullanici, EFaturaSifre = i.EFaturaSifre,
        SmsBaslik = i.SmsBaslik, SmsApiKey = i.SmsApiKey, PosMerchantId = i.PosMerchantId, PosApiKey = i.PosApiKey,
        LogoUrl = i.LogoUrl, VarsayilanDoviz = i.VarsayilanDoviz, VarsayilanKdvOrani = i.VarsayilanKdvOrani,
        VarsayilanGrupId = i.VarsayilanGrupId, VarsayilanFiyatTuru = i.VarsayilanFiyatTuru,
        VarsayilanYakitSeviyesi = i.VarsayilanYakitSeviyesi, DropMesafeYokIseSifir = i.DropMesafeYokIseSifir,
        SaatFarkiToleransDk = i.SaatFarkiToleransDk, IadeIslemSaatSiniri = i.IadeIslemSaatSiniri,
        KurElleGirisKilitli = i.KurElleGirisKilitli,
        RenkGecikenler = i.RenkGecikenler, RenkBugunDonecekler = i.RenkBugunDonecekler, RenkBugunCikacaklar = i.RenkBugunCikacaklar,
        RenkOpsiyonlu = i.RenkOpsiyonlu, RenkLimitBakiye = i.RenkLimitBakiye, RenkAlacakli = i.RenkAlacakli,
        RenkRezAtananPlaka = i.RenkRezAtananPlaka, RenkKiralanmayan = i.RenkKiralanmayan,
        DonemselFaturalamaJob = i.DonemselFaturalamaJob, DonemselOtomatikTahsilat = i.DonemselOtomatikTahsilat,
        MinKiraGun = i.MinKiraGun, MaxKiraGun = i.MaxKiraGun, RezOnayZorunlu = i.RezOnayZorunlu,
        SmtpHost = i.SmtpHost, SmtpPort = i.SmtpPort, SmtpKullanici = i.SmtpKullanici, SmtpSifre = i.SmtpSifre,
        SmtpSsl = i.SmtpSsl, SmtpGonderenAdres = i.SmtpGonderenAdres, SmtpGonderenAd = i.SmtpGonderenAd,
        FaturaSeriKodu = i.FaturaSeriKodu, WhatsAppNumarasi = i.WhatsAppNumarasi, WhatsAppGunlukOzet = i.WhatsAppGunlukOzet,
    };
}
