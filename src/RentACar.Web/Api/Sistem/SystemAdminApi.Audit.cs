using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Auditing;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Enums;
using RentACar.Web.Api.Kira;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Sistem;

/// <summary>
/// F11.1b — <c>/denetim</c> (Blazor <c>AuditList.razor</c>): SALT OKUR, <see cref="Permission.ManageUsers"/>.
/// <para><b>Sır maskesi:</b> kural <see cref="AuditSecretMask"/>'ta TEK kaynaktır; denetim interceptor'ı artık aynı kuralla
/// yazar (yeni kayıtlarda değer hiç yoktur). Okuma yolundaki maske, kural öncesinden kalan eski kayıtlar için sürer;
/// ayrıştırılamayan değer hiç dönmez.</para>
/// </summary>
public static partial class SystemAdminApi
{
    private static void MapAudit(RouteGroupBuilder v1)
    {
        v1.MapGet("/denetim", async Task<Ok<Sayfa<AuditDto>>> (string? tablo, string? kullanici, string? islem, int? sayfa, int? boyut,
            AuditService s, CancellationToken ct) =>
        {
            RentalLimits.Text(tablo, 128, "tablo", "Tablo");
            RentalLimits.Text(kullanici, 128, "kullanici", "Kullanıcı");
            var page = Math.Max(1, sayfa ?? 1);
            var size = boyut is null or < 1 or > 200 ? 30 : boyut.Value;
            var r = await s.SearchAsync(new AuditFilter
            {
                EntityName = SystemApiCommon.Clean(tablo), UserName = SystemApiCommon.Clean(kullanici),
                Action = F5Shared.EnumAdi<AuditAction>(islem, "islem"), Page = page, PageSize = size,
            }, ct);
            var items = r.Items.Select(a => new AuditDto(a.Id, a.TimestampUtc.ToUniversalTime(), a.UserName, a.EntityName, a.EntityId,
                a.Action.ToString(), MaskSecrets(a.OldValues, a.EntityName), MaskSecrets(a.NewValues, a.EntityName))).ToList();
            return TypedResults.Ok(new Sayfa<AuditDto>(items, r.Total, r.Page, r.PageSize));
        }).WithTags(SystemApiCommon.Tag).RequirePermission(Permission.ManageUsers);
    }

    /// <inheritdoc cref="AuditSecretMask.IsSecretKey"/>
    public static bool IsSecretKey(string key) => AuditSecretMask.IsSecretKey(key);

    /// <inheritdoc cref="AuditSecretMask.MaskJson"/>
    public static string? MaskSecrets(string? json, string? table = null) => AuditSecretMask.MaskJson(json, table);
}

public sealed record AuditDto(Guid Id, DateTimeOffset TarihUtc, string? Kullanici, string Tablo, string KayitId, string Islem,
    string? EskiDegerler, string? YeniDegerler);
