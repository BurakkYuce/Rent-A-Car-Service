using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.TenantSettings;
using RentACar.Domain.Common;
using RentACar.Web.Api.Arac;
using RentACar.Web.Api.Finans;
using RentACar.Web.Api.FinansBelge;
using RentACar.Web.Api.FinansHub;
using RentACar.Web.Api.IstemciHata;
using RentACar.Web.Api.Kira;
using RentACar.Web.Api.Menu;
using RentACar.Web.Api.Oturum;
using RentACar.Web.Api.Panel;
using RentACar.Web.Api.Platform;
using RentACar.Web.Api.Rapor;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Api.Secim;
using RentACar.Web.Api.Sistem;
using RentACar.Web.Api.TabloDuzenleri;
using RentACar.Web.Api.Tanim;

namespace RentACar.Web.Api;

/// <summary>
/// <c>/api/ui/v1</c> — yeni arayüzün (Angular, <c>/app</c>) JSON katmanı (F1.2). Tek grup, dört kural:
/// <list type="number">
/// <item><b>Her hata ProblemDetails:</b> uç istisnaları <see cref="UiHata"/>'dan geçer; boru hattının
/// yönlendiren/HTML dönen altı adımı (cookie challenge, TenantActive, PlatformIsolation, hız sınırı,
/// DogrulamaHatasi, UseExceptionHandler, StatusCodePages) <c>/api/ui</c> için JSON döner — SPA 302'yi izleyip
/// HTML alırsa sessizce bozulur, 172 alanlı kira formu kaybolur.</item>
/// <item><b>CSRF her ortamda:</b> güvensiz her istek <c>X-XSRF-TOKEN</c> başlığı taşımak zorunda
/// (Blazor formlarının "yalnız prod" anahtarından <see cref="Identity.FormSecurity"/> BAĞIMSIZ).</item>
/// <item><b>Pilot kapısı:</b> <c>oturum/*</c> ve <c>istemci-hata</c> dışında pilot olmayan firmaya 403
/// <c>pilot_degil</c> (<c>TenantSettings.YeniArayuzPilot</c>).</item>
/// <item><b><c>Cache-Control: no-store</c></b> tüm <c>/api/ui</c> yanıtlarında (kişisel veri önbelleğe düşmez).</item>
/// </list>
/// Grubun dışında <c>/api/ui</c> ucu açmak YASAK — CSRF ve pilot filtreleri yalnız grupta; yapısal test
/// (<c>UiApiYapisalTests</c>) her <c>/api/ui</c> ucunda <see cref="UiApiGrubuMetadata"/> arar.
/// </summary>
public static class UiApiExtensions
{
    public const string Onek = "/api/ui";
    public const string V1 = "/api/ui/v1";
    public const string XsrfCerezi = "XSRF-TOKEN";
    public const string XsrfBasligi = "X-XSRF-TOKEN";
    public const string OpenApiBelgesi = "ui-v1";

    /// <summary>İstek yeni arayüz API'sine mi (boru hattı adımları buna göre JSON döner).</summary>
    public static bool UiYolu(PathString yol) => yol.StartsWithSegments(Onek, StringComparison.OrdinalIgnoreCase);

    /// <summary>Oturum uçları — PlatformIsolation muafiyeti ve pilot kapısı muafiyeti.</summary>
    public static bool OturumYolu(PathString yol)
        => yol.StartsWithSegments(V1 + "/oturum", StringComparison.OrdinalIgnoreCase);

    /// <summary>Giriş/çıkış: TenantActive bunları atlar (Blazor'daki <c>/auth</c> muafiyetinin karşılığı) —
    /// kapalı firmanın bayat çerezini taşıyan kullanıcı başka firmaya giriş yapabilmeli, çıkış hep çalışmalı.
    /// F12.1: platform konsolunun giriş/çıkışı da (Blazor <c>/platform</c> muafiyetinin karşılığı).</summary>
    public static bool GirisCikisYolu(PathString yol)
        => yol.StartsWithSegments(V1 + "/oturum/giris", StringComparison.OrdinalIgnoreCase)
           || yol.StartsWithSegments(V1 + "/oturum/cikis", StringComparison.OrdinalIgnoreCase)
           || yol.StartsWithSegments(PlatformPrefix + "/oturum/giris", StringComparison.OrdinalIgnoreCase)
           || yol.StartsWithSegments(PlatformPrefix + "/oturum/cikis", StringComparison.OrdinalIgnoreCase);

    /// <summary>F12.1: platform konsolu uçlarının öneki (ayrı yetki alanı — <c>PlatformAdmin</c> policy).</summary>
    public const string PlatformPrefix = V1 + "/platform";

    /// <summary>F12.1: platform konsolu API'si mi (segment sınırıyla: <c>/platformlar</c> eşleşmez). PlatformIsolation
    /// bu yolu platform operatörüne açar; pilot kapısı uygulanmaz (platform oturumunun firması yok).</summary>
    public static bool PlatformYolu(PathString yol)
        => yol.StartsWithSegments(PlatformPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Pilot kapısından muaf rota: oturum (giriş yapılabilsin, "pilot değilsiniz" bandı için
    /// <c>ben</c> okunabilsin), istemci hata raporu ve platform konsolu (F12.1 — firma bağlamı yok; erişimi
    /// PlatformAdmin policy'si belirler). Rota DESENİ üzerinden karar verilir.</summary>
    public static bool PilotMuaf(string rota)
        => new PathString(rota.StartsWith('/') ? rota : "/" + rota) is var p
           && (OturumYolu(p) || p.StartsWithSegments(V1 + "/istemci-hata", StringComparison.OrdinalIgnoreCase)
               || PlatformYolu(p));

    /// <summary>RFC 9110 güvenli yöntemler — CSRF doğrulaması yalnız bunların DIŞINDA.</summary>
    public static bool GuvenliYontem(string yontem)
        => HttpMethods.IsGet(yontem) || HttpMethods.IsHead(yontem) || HttpMethods.IsOptions(yontem) || HttpMethods.IsTrace(yontem);

    // ------------------------------------------------------------------ servisler

    public static IServiceCollection AddUiApi(this IServiceCollection services, IWebHostEnvironment env)
    {
        // Angular HttpClient XSRF deseni: XSRF-TOKEN çerezini okur, X-XSRF-TOKEN başlığıyla geri yollar.
        // Blazor formları gizli alanla (__RequestVerificationToken) doğrulanmaya devam eder — başlık yoksa
        // antiforgery form alanına bakar; bu ayar onları etkilemez.
        services.AddAntiforgery(o => o.HeaderName = XsrfBasligi);
        // Uç tanımları (uç keşfi) YALNIZ Development'ta dışarı açılır; üretimde şema sızdırılmaz.
        if (env.IsDevelopment())
            services.AddOpenApi(OpenApiBelgesi, o => o.ShouldInclude = d =>
                ("/" + (d.RelativePath ?? "")).StartsWith(V1 + "/", StringComparison.OrdinalIgnoreCase));
        return services;
    }

    // ------------------------------------------------------------------ boru hattı

    /// <summary>
    /// <c>/api/ui</c> istekleri için boru hattı çiti. StatusCodePages ve istisna işleyicisinden SONRA
    /// (içeride) durur: (a) <c>no-store</c> başlığını yanıt başlarken yazar; (b) StatusCodePages re-execute'u
    /// kapatır (boş 404/405 Blazor <c>/not-found</c> HTML'ine dönmesin); (c) uç filtresine ulaşmadan atılan
    /// istisnayı (bağlama hatası, middleware) ProblemDetails'e çevirir — UseExceptionHandler'ın <c>/Error</c>
    /// HTML'ine ve Development hata sayfasına düşmez; (d) gövdesiz hata durumunu ProblemDetails yapar.
    /// </summary>
    public static IApplicationBuilder UseUiApiBoruHatti(this IApplicationBuilder app)
        => app.Use(async (ctx, next) =>
        {
            if (!UiYolu(ctx.Request.Path))
            {
                await next(ctx);
                return;
            }

            ctx.Response.OnStarting(static s =>
            {
                var r = ((HttpContext)s).Response;
                r.Headers.CacheControl = "no-store";
                r.Headers.Pragma = "no-cache";
                return Task.CompletedTask;
            }, ctx);

            if (ctx.Features.Get<IStatusCodePagesFeature>() is { } scp) scp.Enabled = false;

            try
            {
                await next(ctx);
            }
            catch (Exception ex) when (!ctx.Response.HasStarted
                                       && !(ex is OperationCanceledException && ctx.RequestAborted.IsCancellationRequested))
            {
                Logla(ctx, ex);
                ctx.Response.Clear();
                await ProblemFor(ex).ExecuteAsync(ctx);
                return;
            }

            if (!ctx.Response.HasStarted && ctx.Response.StatusCode >= 400
                && ctx.Response.ContentLength is null && string.IsNullOrEmpty(ctx.Response.ContentType))
                await GenelProblem(ctx.Response.StatusCode).ExecuteAsync(ctx);
        });

    /// <summary>Boru hattı adımlarının (challenge, tenant kapalı, hız sınırı…) ortak JSON yazıcısı.</summary>
    public static Task YazAsync(HttpContext ctx, string kod, string detay)
        => UiHata.Problem(kod, detay).ExecuteAsync(ctx);

    /// <summary>Bağlama hatasının (bozuk JSON, eksik gövde, çevrilemeyen parametre) tek mesajı — her ortamda aynı.</summary>
    public const string BaglamaHatasiMesaji = "İstek gövdesi okunamadı ya da eksik.";

    /// <summary>İstisna → ProblemDetails. Bağlama hatası (bozuk JSON, eksik gövde) istemci hatasıdır, 500 değil.</summary>
    internal static ProblemHttpResult ProblemFor(Exception ex) => ex switch
    {
        BadHttpRequestException b when b.StatusCode == StatusCodes.Status400BadRequest
            => UiHata.Problem(UiHata.Dogrulama, BaglamaHatasiMesaji),
        BadHttpRequestException b => GenelProblem(b.StatusCode),
        _ => UiHata.Problem(ex),
    };

    /// <summary>Kod tablosunda karşılığı olmayan durumlar (404, 405, 415, 413…) — <c>kod</c>'suz ProblemDetails.</summary>
    private static ProblemHttpResult GenelProblem(int status) => status switch
    {
        // Üretimde RouteHandlerOptions.ThrowOnBadRequest KAPALI (yalnız Development'ta açık): bağlama hatası istisna
        // değil GÖVDESİZ 400 olarak döner. Development'taki ProblemFor ile AYNI sözleşme — SPA davranışı `kod`'a
        // bağlı; kodsuz 400 alan hatası yerine genel hata bandına düşüyordu.
        StatusCodes.Status400BadRequest => UiHata.Problem(UiHata.Dogrulama, BaglamaHatasiMesaji),
        StatusCodes.Status401Unauthorized => UiHata.Problem(UiHata.OturumYok, "Oturum açık değil."),
        StatusCodes.Status403Forbidden => UiHata.Problem(UiHata.YetkiYok, "Bu işlem için yetkiniz yok."),
        StatusCodes.Status429TooManyRequests => UiHata.Problem(UiHata.CokIstek, "Çok fazla istek; biraz sonra tekrar deneyin."),
        _ => TypedResults.Problem(statusCode: status),
    };

    private static void Logla(HttpContext ctx, Exception ex)
    {
        var log = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RentACar.Web.Api.UiApi");
        switch (LogSeviyesi(ex))
        {
            case LogLevel.Warning:
                log.LogWarning(ex, "/api/ui veri taşması güvenlik ağı (uç sınırı eksik): {Yol}", ctx.Request.Path);
                break;
            case LogLevel.Information:
                log.LogInformation(ex, "/api/ui istemci hatası: {Yol}", ctx.Request.Path);
                break;
            default:
                log.LogError(ex, "/api/ui beklenmeyen hata: {Yol}", ctx.Request.Path);
                break;
        }
    }

    /// <summary>
    /// Hata günlük seviyesi (SAF, public: test edilebilsin). 22001/22003 güvenlik ağı <b>Warning</b>: 400 dönse de
    /// bir uçta sınır denetiminin EKSİK olduğunu gösterir (DEVIR §5 "uç sınırları") — Information'da kaybolmasın.
    /// Diğer istemci hataları Information, eşlenmeyen istisna Error.
    /// </summary>
    public static LogLevel LogSeviyesi(Exception ex)
        => UiHata.VeriTasmasi(ex) ? LogLevel.Warning
            : ex is BadHttpRequestException || UiHata.Esle(ex) is not null ? LogLevel.Information
            : LogLevel.Error;

    // ------------------------------------------------------------------ uçlar

    /// <summary>Grup + filtreler + oturum uçları + (test/ileri modüller için) DI'dan gelen uç kayıtları.</summary>
    public static RouteGroupBuilder MapUiApi(this WebApplication app)
    {
        var v1 = app.MapGroup(V1)
            .RequireAuthorization()                 // varsayılan: oturum şart; anonim uçlar AllowAnonymous der
            .WithMetadata(new UiApiGrubuMetadata())
            .AddEndpointFilter(HataFiltresi)        // en dış: aşağıdakilerin de istisnası ProblemDetails olur
            .AddEndpointFilter(XsrfFiltresi)
            .AddEndpointFilter(PilotFiltresi);

        v1.MapOturumApi();
        v1.MapMenuApi();    // F1.6
        v1.MapSecimApi();   // F1.6
        v1.MapFinansApi();  // F4.4 — sabit panel para işlemleri (F8 yeniden kullanır)
        v1.MapIstemciHataApi(); // F3.3
        v1.MapTabloDuzeniApi(); // F3.5
        v1.MapKiraApi();        // F4.1
        v1.MapPanelApi();       // F4.1
        v1.MapRezervasyonApi(); // F5.1
        v1.MapTeklifApi();      // F5.1
        v1.MapPlanlamaApi();    // F5.1 — takvim + müsaitlik
        v1.MapRezSartApi();     // F5.1
        v1.MapFiloKiralamaApi(); // F5.1
        v1.MapAracApi();         // F6.1a — araç liste/kart/detay/durum/foto + seçim
        v1.MapAracTanimApi();    // F6.1a — araç sahipleri, segmentler, araç tipleri
        v1.MapFinanceHubApi();   // F8.1a — finans ekranları 1. yarı (kasa, cari, depozito, dönem, kurlar)
        v1.MapInvoiceUiApi();         // F8.1b — faturalar (liste/detay/satırlar/manuel/iade/toplu)
        v1.MapPenaltyUiApi();         // F8.1b — cezalar
        v1.MapExpenseUiApi();         // F8.1b — giderler
        v1.MapIncomingInvoiceUiApi(); // F8.1b — gelen e-fatura
        v1.MapVehicleSaleUiApi();     // F8.1b — araç satışları
        AracFinans.AracFinansUclari.Esle(v1); // F6.1b — kredi, müşteri taksit, sipariş, BAF, hasar, filo plan
        v1.MapSystemApi();       // F11.1b — sistem ayarları, kullanıcı/yetki, denetim, web sitesi, tanımlar (2. yarı)
        ServiceInsurance.ServiceInsuranceEndpoints.Map(v1); // F9.1 — servis, sigorta/MTV/muayene, vade, fiyat & tarife
        v1.MapReportApi();       // F10.1 — raporlar (yalnız okur)
        Rapor.ShiftApi.MapShiftApi(v1); // F10.3 — personel çalışma (vardiya) yazma uçları
        v1.MapPlatformApi();     // F12.1 — platform konsolu (ayrı yetki alanı: PlatformAdmin policy)
        Cari.CrmApi.Map(v1);     // F7.1 — cariler + CRM (anket, şikayet, assistans, hukuk, analiz)
        v1.MapTanimApi();        // F11.1a — F11 tanımları (ilk yarı), genel tanım CRUD deseni
        foreach (var kayit in app.Services.GetServices<IUiApiUcKaydi>())
            kayit.Esle(v1);

        if (app.Environment.IsDevelopment())
            app.MapOpenApi("/openapi/{documentName}.json");
        return v1;
    }

    private static async ValueTask<object?> HataFiltresi(EndpointFilterInvocationContext c, EndpointFilterDelegate next)
    {
        try
        {
            return await next(c);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && c.HttpContext.RequestAborted.IsCancellationRequested))
        {
            Logla(c.HttpContext, ex);
            return ProblemFor(ex);
        }
    }

    private static async ValueTask<object?> XsrfFiltresi(EndpointFilterInvocationContext c, EndpointFilterDelegate next)
    {
        var http = c.HttpContext;
        if (!GuvenliYontem(http.Request.Method))
        {
            // Başlık ZORUNLU: form alanıyla gelen token kabul edilmez (antiforgery başlık yoksa forma bakardı).
            if (!http.Request.Headers.ContainsKey(XsrfBasligi))
                return UiHata.Problem(UiHata.XsrfGecersiz, "Güvenlik belirteci (X-XSRF-TOKEN) eksik.");
            var af = http.RequestServices.GetRequiredService<IAntiforgery>();
            if (!await af.IsRequestValidAsync(http))
                return UiHata.Problem(UiHata.XsrfGecersiz,
                    "Güvenlik belirteci geçersiz ya da oturumla eşleşmiyor; belirteci yenileyip tekrar deneyin.");
        }
        return await next(c);
    }

    private static async ValueTask<object?> PilotFiltresi(EndpointFilterInvocationContext c, EndpointFilterDelegate next)
    {
        var http = c.HttpContext;
        var rota = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? http.Request.Path.Value ?? "";
        if (!PilotMuaf(rota) && !await PilotMuAsync(http))
            return UiHata.Problem(UiHata.PilotDegil, "Yeni arayüz bu firmada henüz açık değil.");
        return await next(c);
    }

    /// <summary>Firmanın pilot bayrağı (ayar satırı yoksa false). Önbelleksiz: bayrak kapatılınca ANINDA kapanır.</summary>
    internal static async Task<bool> PilotMuAsync(HttpContext http)
    {
        if (http.RequestServices.GetRequiredService<ITenantContext>().TenantId is null) return false;
        var ayar = await http.RequestServices.GetRequiredService<ITenantSettingsRepository>().GetAsync(http.RequestAborted);
        return ayar?.YeniArayuzPilot == true;
    }

    /// <summary>
    /// CSRF belirtecini üretir ve <c>XSRF-TOKEN</c> çerezine yazar (JS okur → <c>X-XSRF-TOKEN</c>).
    /// Belirteç KİMLİĞE bağlıdır: giriş/çıkışta <c>ctx.User</c> yeni principal'a ayarlandıktan SONRA
    /// çağrılmalı — aksi halde eski kimliğe bağlı belirteç verilir ve ilk güvensiz istek reddedilir.
    /// </summary>
    public static void XsrfVer(HttpContext ctx)
    {
        var tokens = ctx.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(ctx);
        var env = ctx.RequestServices.GetRequiredService<IWebHostEnvironment>();
        ctx.Response.Cookies.Append(XsrfCerezi, tokens.RequestToken!, new CookieOptions
        {
            Path = "/",
            HttpOnly = false,              // SPA okuyabilmeli (çift-gönderim deseni)
            SameSite = SameSiteMode.Strict,
            Secure = !env.IsDevelopment(), // dev http://localhost kırılmasın
            IsEssential = true,
        });
    }
}

/// <summary>Uç <c>/api/ui/v1</c> grubunda (filtreleriyle) eşlendi — yapısal test işareti.</summary>
public sealed record UiApiGrubuMetadata;

/// <summary>
/// <c>/api/ui/v1</c> grubuna uç ekleyen kayıt (DI'dan toplanır). Grubun filtreleri (hata, CSRF, pilot)
/// eklenen uçlara da uygulanır. Test host'u hata sözleşmesini gerçek boru hattında sınamak için bunu kullanır.
/// </summary>
public interface IUiApiUcKaydi
{
    void Esle(RouteGroupBuilder v1);
}
