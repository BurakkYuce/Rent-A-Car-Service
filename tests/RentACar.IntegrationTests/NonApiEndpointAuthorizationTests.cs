using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// F13 Exit çiti (gerçek host, uç bazında): <c>/api/ui</c> DIŞINDA kalan her minimal-API ucu açık bir yetki kararı
/// taşır — <see cref="IAuthorizeData"/> (RequireAuthorization / RequirePermission / grup kapısı) ya da
/// <see cref="IAllowAnonymous"/>. Karar uç metadatasından okunur (kaynak taraması değil): grup kapısı yalnız o grubun
/// uçlarına geçer, aynı dosyadaki kapısız bir uç yakalanır. <c>/api/ui</c> uçları kendi yapısal testinde (UiApiYapisal).
/// </summary>
[Collection("web")]
public sealed class NonApiEndpointAuthorizationTests(WebFixture fx)
{
    [Fact]
    public void Non_api_endpoints_declare_an_authorization_decision()
    {
        var endpoints = fx.Web.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<System.Reflection.MethodInfo>() is not null)
            .Where(e => !("/" + (e.RoutePattern.RawText ?? "").TrimStart('/')).StartsWith("/api/ui", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(endpoints.Count >= 8, $"Uç tablosu şüpheli: {endpoints.Count}");

        // Gerekçeli istisna: OpenAPI belgesi yalnız Development'ta eşlenir (UiApiExtensions.MapUiApi); şema, veri değil.
        string[] exempt = ["openapi/{documentName}.json"];
        var undecided = endpoints
            .Where(e => !exempt.Contains(e.RoutePattern.RawText?.TrimStart('/')))
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null && !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any())
            .Select(e => e.DisplayName ?? e.RoutePattern.RawText)
            .ToList();
        Assert.True(undecided.Count == 0, "Yetki kararı açık olmayan uç:\n  " + string.Join("\n  ", undecided));
    }
}
