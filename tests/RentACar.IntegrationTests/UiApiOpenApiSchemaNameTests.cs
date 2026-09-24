using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// YAPISAL — OpenAPI şema adları benzersiz olmalı. Üretici şema kimliğini CLR tipinin KISA adından türetir; aynı kısa adı
/// taşıyan iki DTO tek şemaya düşer ve bir ucun sözleşmesi ötekinin şekliyle belgelenir (<c>GET /vade</c> satırı
/// bildirim merkezinin <c>DueItemDto</c>'suyla belgeleniyordu; SPA tipi elle yazılmak zorunda kalmıştı). Bu test
/// <c>/api/ui/v1</c> uçlarının istek/yanıt tiplerinden erişilen TÜM tipleri (özellikler ve genel tip argümanları
/// dahil) toplar ve aynı kısa adı taşıyan iki farklı tip bulursa kırmızı olur.
/// </summary>
[Collection("web")]
public sealed class UiApiOpenApiSchemaNameTests(WebFixture fx)
{
    [Fact]
    public void Schema_types_reachable_from_ui_endpoints_have_unique_simple_names()
    {
        var isService = fx.Web.Services.GetRequiredService<IServiceProviderIsService>();
        var endpoints = fx.Web.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => ("/" + e.RoutePattern.RawText?.TrimStart('/')).StartsWith("/api/ui/v1/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.NotEmpty(endpoints);

        var seen = new HashSet<Type>();
        var byName = new Dictionary<string, HashSet<Type>>(StringComparer.Ordinal);

        void Visit(Type t)
        {
            if (t.IsByRef) t = t.GetElementType()!;
            if (t.IsArray) { Visit(t.GetElementType()!); return; }
            if (t.IsGenericType)
                foreach (var arg in t.GetGenericArguments()) Visit(arg);
            if (t.IsGenericParameter || t.Assembly.GetName().Name?.StartsWith("RentACar.", StringComparison.Ordinal) != true) return;
            if (!seen.Add(t)) return;
            if (!t.IsGenericType)
            {
                if (!byName.TryGetValue(t.Name, out var set)) byName[t.Name] = set = [];
                set.Add(t);
            }
            if (t.IsEnum) return;
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.GetIndexParameters().Length == 0) Visit(p.PropertyType);
        }

        foreach (var e in endpoints)
        {
            if (e.Metadata.GetMetadata<MethodInfo>() is { } method)
            {
                Visit(method.ReturnType);
                foreach (var p in method.GetParameters())
                {
                    var type = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;
                    if (type == typeof(HttpContext) || type == typeof(CancellationToken)) continue;
                    if (!type.IsValueType && type != typeof(string) && isService.IsService(type)) continue; // DI hizmeti
                    Visit(type);
                }
            }
            foreach (var produces in e.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>())
                if (produces.Type is { } rt) Visit(rt);
            foreach (var accepts in e.Metadata.GetOrderedMetadata<IAcceptsMetadata>())
                if (accepts.RequestType is { } qt) Visit(qt);
        }

        Assert.Contains(byName.Keys, n => n == "DueItemDto"); // tarama gerçekten DTO'lara ulaşıyor
        var duplicates = byName.Where(kv => kv.Value.Count > 1)
            .Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value.Select(t => t.FullName))}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        Assert.True(duplicates.Count == 0, "Aynı kısa adlı şema tipleri (birini yeniden adlandırın):\n" + string.Join("\n", duplicates));
    }
}
