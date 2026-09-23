using System.Text.Json;
using System.Text.Json.Nodes;
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
/// <para><b>Sır maskesi (yeni yüzey sertleştirmesi):</b> denetim interceptor'ı PII alanlarını maskeler ama sır cipher'larını
/// (<c>SmtpSifreEnc</c>, <c>SmsApiKeyEnc</c>…), <c>PasswordHash</c> ve <c>CalendarToken</c>'ı maskelemez — Blazor ekranı bunları
/// 120 karakterle kırpıp gösteriyordu. API eski/yeni değer JSON'unda sır niteliğindeki her anahtarın değerini <c>***</c>
/// yapar; ayrıştırılamayan değer hiç dönmez.</para>
/// </summary>
public static partial class SystemAdminApi
{
    private static readonly string[] SecretKeySuffixes = ["Enc", "Hash", "Token"];
    private static readonly string[] SecretKeyParts = ["Sifre", "Password", "ApiKey", "Secret", "Parola"];

    private static void MapAudit(RouteGroupBuilder v1)
    {
        v1.MapGet("/denetim", async Task<Ok<Sayfa<AuditDto>>> (string? tablo, string? kullanici, string? islem, int? sayfa, int? boyut,
            AuditService s, CancellationToken ct) =>
        {
            Sinirlar.Metin(tablo, 128, "tablo", "Tablo");
            Sinirlar.Metin(kullanici, 128, "kullanici", "Kullanıcı");
            var page = Math.Max(1, sayfa ?? 1);
            var size = boyut is null or < 1 or > 200 ? 30 : boyut.Value;
            var r = await s.SearchAsync(new AuditFilter
            {
                EntityName = SystemApiCommon.Clean(tablo), UserName = SystemApiCommon.Clean(kullanici),
                Action = F5Ortak.EnumAdi<AuditAction>(islem, "islem"), Page = page, PageSize = size,
            }, ct);
            var items = r.Items.Select(a => new AuditDto(a.Id, a.TimestampUtc.ToUniversalTime(), a.UserName, a.EntityName, a.EntityId,
                a.Action.ToString(), MaskSecrets(a.OldValues), MaskSecrets(a.NewValues))).ToList();
            return TypedResults.Ok(new Sayfa<AuditDto>(items, r.Total, r.Page, r.PageSize));
        }).WithTags(SystemApiCommon.Tag).RequirePermission(Permission.ManageUsers);
    }

    internal static bool IsSecretKey(string key)
        => SecretKeySuffixes.Any(s => key.EndsWith(s, StringComparison.Ordinal))
           || SecretKeyParts.Any(p => key.Contains(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Denetim değer JSON'u → sır anahtarları maskeli JSON. Nesne değilse ya da ayrıştırılamıyorsa <c>null</c>.</summary>
    internal static string? MaskSecrets(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject obj) return null;
            foreach (var key in obj.Select(kv => kv.Key).ToList())
                if (IsSecretKey(key) && obj[key] is not null)
                    obj[key] = "***";
            return obj.ToJsonString();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record AuditDto(Guid Id, DateTimeOffset TarihUtc, string? Kullanici, string Tablo, string KayitId, string Islem,
    string? EskiDegerler, string? YeniDegerler);
