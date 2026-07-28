namespace RentACar.PublicSite;

/// <summary>
/// PR-5: Caddy `on_demand_tls` ask-endpoint'i — sertifika çıkarmadan/yenilemeden ÖNCE bu host
/// `TenantDomains`'te kayıtlı mı sorar (Kind/Status FARK ETMEZ — `PendingVerification` de "bilinen"
/// sayılır, aksi halde yeni eklenen bir özel domain'in İLK ACME denemesi bile 404 alır ve asla kendi
/// kendini doğrulayamaz — bkz. PublicTenantResolver). Guard-free/anonim — Caddy içeriden (127.0.0.1)
/// çağırır, kimlik doğrulaması yok.
/// </summary>
public static class DomainVerificationEndpoints
{
    public static IEndpointRouteBuilder MapDomainVerificationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dogrulama/ask", async (string? domain, DomainAskCache cache, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(domain)) return Results.NotFound();
            return await cache.ExistsAsync(domain, ct) ? Results.Ok() : Results.NotFound();
        });
        return app;
    }
}
