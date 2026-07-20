using System.Diagnostics;
using System.Security.Claims;
using RentACar.Web.Identity;
using Serilog;
using Serilog.Context;

namespace RentACar.Web.Observability;

/// <summary>
/// İstek-kapsamlı log zenginleştirme (çok-kiracılı gözlemlenebilirlik): her log satırına tenant/kullanıcı/
/// request-id ekler ki "X firmasının / Y kullanıcısının istekleri" filtrelenebilsin. İKİ mekanizma:
/// (a) <see cref="Middleware"/> LogContext'e property'leri iterek İSTEK BOYUNCA çıkan TÜM logları (servis
///     LogError'ları dahil) zenginleştirir — UseAuthentication'dan SONRA çalışmalı (claim'ler o zaman dolu);
/// (b) <see cref="Enrich"/> UseSerilogRequestLogging'in tamamlanma-olayını zenginleştirir (o olay pipeline
///     sonunda, LogContext scope'u kapandıktan sonra loglandığı için ayrı mekanizma gerekir).
/// KARDİNALİTE: tenant/kullanıcı yalnız LOG'da; ASLA metrik etiketi değil (Prometheus patlaması).
/// SINIR: statik-SSR + form-POST HTTP isteklerini kapsar; interaktif Blazor circuit'inde HttpContext null
/// olduğundan circuit-içi loglar buradan zenginleşmez (çoğunlukla-statik app'te düşük etki).
/// </summary>
public static class RequestEnrichment
{
    private static string TenantId(ClaimsPrincipal u)
        => u.FindFirst(IdentityClaims.TenantId)?.Value
           ?? (u.HasClaim("platform_admin", "true") ? "platform" : "-");

    private static string User(ClaimsPrincipal u) => u.Identity?.Name ?? "-";

    private static string RequestId(HttpContext ctx)
        => Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;

    /// <summary>UseSerilogRequestLogging tamamlanma-olayı için (User pipeline sonunda dolu).</summary>
    public static void Enrich(IDiagnosticContext diag, HttpContext ctx)
    {
        var u = ctx.User;
        if (u.Identity?.IsAuthenticated == true)
        {
            diag.Set("tenant_id", TenantId(u));
            diag.Set("user", User(u));
        }
        diag.Set("request_id", RequestId(ctx));
    }

    /// <summary>İstek boyunca çıkan tüm logları LogContext ile zenginleştirir (auth'tan SONRA yerleştirilir).</summary>
    public sealed class Middleware : IMiddleware
    {
        public async Task InvokeAsync(HttpContext ctx, RequestDelegate next)
        {
            var u = ctx.User;
            if (u.Identity?.IsAuthenticated != true)
            {
                using (LogContext.PushProperty("request_id", RequestId(ctx)))
                    await next(ctx);
                return;
            }
            using (LogContext.PushProperty("tenant_id", TenantId(u)))
            using (LogContext.PushProperty("user", User(u)))
            using (LogContext.PushProperty("request_id", RequestId(ctx)))
                await next(ctx);
        }
    }
}
