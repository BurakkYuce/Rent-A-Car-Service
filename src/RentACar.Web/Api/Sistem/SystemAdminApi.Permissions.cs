using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Enums;
using RentACar.Web.Api.Kira;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Sistem;

/// <summary>
/// F11.1b — <c>/yetki</c> (Blazor <c>Yetki.razor</c> + <c>YetkiEndpoints</c>): ekran yetki override'ları, rol kopyalama,
/// yetki grubu şablonları. Tümü <see cref="Permission.ManageUsers"/>. Override rol-matrisi floor'unu AŞAMAZ
/// (<see cref="PermissionResolver"/>); yalnız sıkılaştırır. Blazor tanımsız rol adını sessizce düşürüyordu — burada
/// 400 <c>errors[roller]</c> (sessiz daralma kullanıcıyı ekrandan kilitlerdi).
/// <para>Override yazımı bir "set" eylemidir (ekran kodu doğal anahtar, upsert) — tam kayıt PUT'u değil; sürüm
/// taşımaz. Son yazan kazanır (Blazor paritesi).</para>
/// </summary>
public static partial class SystemAdminApi
{
    private static void MapPermissions(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/yetki").WithTags(SystemApiCommon.Tag).RequirePermission(Permission.ManageUsers);

        g.MapGet("/ekranlar", async Task<Ok<IReadOnlyList<ScreenPermissionDto>>> (ScreenPermissionService s, CancellationToken ct)
            => TypedResults.Ok(await ScreensAsync(s, ct)));

        g.MapPost("/ekranlar", async Task<Ok<IReadOnlyList<ScreenPermissionDto>>> (ScreenPermissionRequest i, ScreenPermissionService s, CancellationToken ct) =>
        {
            Sinirlar.Metin(i.EkranKodu, 64, "ekranKodu", "Ekran kodu");
            var roles = ParseRoles(i.Roller);
            await s.SetAsync(i.EkranKodu ?? "", roles, i.Aktif, ct);
            return TypedResults.Ok(await ScreensAsync(s, ct));
        }).AlanlariEsle([("Ekran kodu", "ekranKodu")]);

        g.MapDelete("/ekranlar/{kod}", async Task<Results<NoContent, ProblemHttpResult>> (string kod, ScreenPermissionService s, CancellationToken ct)
            => await s.RemoveAsync(kod, ct) ? TypedResults.NoContent() : SystemApiCommon.NotFound("Ekran override'ı bulunamadı."));

        g.MapPost("/kopyala", async Task<Ok<CountResult>> (RoleCopyRequest i, ScreenPermissionService s, CancellationToken ct) =>
        {
            var source = F5Ortak.EnumAdi<UserRole>(i.Kaynak, "kaynak") ?? throw new ValidationException("Kaynak rol seçilmelidir.", "kaynak");
            var target = F5Ortak.EnumAdi<UserRole>(i.Hedef, "hedef") ?? throw new ValidationException("Hedef rol seçilmelidir.", "hedef");
            return TypedResults.Ok(new CountResult(await s.KopyalaRolAsync(source, target, ct)));
        }).AlanlariEsle([("Kaynak ve hedef", "hedef")]);

        g.MapGet("/gruplar", async Task<Ok<IReadOnlyList<PermissionGroupDto>>> (ScreenPermissionService s, CancellationToken ct)
            => TypedResults.Ok(await GroupsAsync(s, ct)));

        g.MapPost("/gruplar", async Task<Ok<IReadOnlyList<PermissionGroupDto>>> (PermissionGroupRequest i, ScreenPermissionService s, CancellationToken ct) =>
        {
            Sinirlar.Metin(i.Ad, 128, "ad", "Şablon adı");
            await s.SnapshotGrupAsync(i.Ad ?? "", ct);
            return TypedResults.Ok(await GroupsAsync(s, ct));
        }).AlanlariEsle([("Şablon adı", "ad")]);

        g.MapPost("/gruplar/uygula", async Task<Ok<CountResult>> (PermissionGroupRequest i, ScreenPermissionService s, CancellationToken ct)
            => TypedResults.Ok(new CountResult(await s.UygulaGrupAsync(i.Ad ?? "", ct)))).AlanlariEsle([("Şablon bulunamadı", "ad")]);

        g.MapDelete("/gruplar", async Task<Results<NoContent, ProblemHttpResult>> (string? ad, ScreenPermissionService s, CancellationToken ct)
            => !string.IsNullOrWhiteSpace(ad) && await s.SilGrupAsync(ad, ct) ? TypedResults.NoContent() : SystemApiCommon.NotFound("Şablon bulunamadı."));
    }

    private static IReadOnlyList<UserRole> ParseRoles(IReadOnlyList<string>? roles)
    {
        var result = new List<UserRole>();
        foreach (var r in roles ?? [])
            result.Add(F5Ortak.EnumAdi<UserRole>(r, "roller") ?? throw new ValidationException("Rol adı boş olamaz.", "roller"));
        return result.Distinct().ToList();
    }

    private static async Task<IReadOnlyList<ScreenPermissionDto>> ScreensAsync(ScreenPermissionService s, CancellationToken ct)
        => (await s.ListAsync(ct)).OrderBy(x => x.EkranKodu, StringComparer.Ordinal)
            .Select(x => new ScreenPermissionDto(x.EkranKodu,
                x.AllowedRolesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                x.Aktif, (x.UpdatedAtUtc ?? x.CreatedAtUtc).ToUniversalTime()))
            .ToList();

    private static async Task<IReadOnlyList<PermissionGroupDto>> GroupsAsync(ScreenPermissionService s, CancellationToken ct)
        => (await s.ListGruplarAsync(ct)).OrderBy(x => x.Ad, StringComparer.Ordinal)
            .Select(x => new PermissionGroupDto(x.Ad, CountItems(x.KalemlerJson), (x.UpdatedAtUtc ?? x.CreatedAtUtc).ToUniversalTime()))
            .ToList();

    private static int CountItems(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0;
        }
        catch (System.Text.Json.JsonException)
        {
            return 0;
        }
    }
}

public sealed record ScreenPermissionDto(string EkranKodu, IReadOnlyList<string> Roller, bool Aktif, DateTimeOffset GuncellemeUtc);

public sealed record ScreenPermissionRequest(string? EkranKodu, IReadOnlyList<string>? Roller, bool Aktif = true);

public sealed record RoleCopyRequest(string? Kaynak, string? Hedef);

public sealed record CountResult(int Adet);

public sealed record PermissionGroupDto(string Ad, int EkranSayisi, DateTimeOffset GuncellemeUtc);

public sealed record PermissionGroupRequest(string? Ad);
