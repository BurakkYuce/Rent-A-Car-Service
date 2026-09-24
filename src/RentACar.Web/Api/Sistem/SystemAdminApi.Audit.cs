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

    /// <summary>
    /// Sır ya da KVKK kapsamındaki kişisel veri anahtarı mı (büyük/küçük harf duyarsız). Güvenlik incelemesi L1: PII
    /// anahtarları (TC, ehliyet, pasaport, maaş, IBAN) da maskelenir — interceptor düz-metin legacy alanları maskeliyor
    /// ama eski kayıtlar ve yeni adlandırmalar kaçabilir.
    /// </summary>
    public static bool IsSecretKey(string key)
        => SecretKeySuffixes.Any(s => key.EndsWith(s, StringComparison.OrdinalIgnoreCase))
           || SecretKeyParts.Any(p => key.Contains(p, StringComparison.OrdinalIgnoreCase))
           || PiiKeyParts.Any(p => key.Contains(p, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] PiiKeyParts = ["TcKimlik", "TcNo", "KimlikNo", "VergiNo", "EhliyetNo", "PasaportNo", "Maas", "Iban"];

    /// <summary>Denetim değer JSON'u → sır/PII anahtarları (iç içe nesne ve dizilerde de) maskeli JSON. Nesne değilse ya da
    /// ayrıştırılamıyorsa <c>null</c>.</summary>
    public static string? MaskSecrets(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject obj) return null;
            MaskNode(obj);
            return obj.ToJsonString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void MaskNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var key in o.Select(kv => kv.Key).ToList())
                {
                    if (IsSecretKey(key) && o[key] is not null) o[key] = "***";
                    else if (MaskEmbeddedJson(o[key]) is { } masked) o[key] = masked;
                    else MaskNode(o[key]);
                }
                break;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++)
                {
                    if (MaskEmbeddedJson(a[i]) is { } masked) a[i] = masked;
                    else MaskNode(a[i]);
                }
                break;
        }
    }

    /// <summary>Metin değeri içine gömülü JSON nesnesi/dizisi (ör. JSON kolonları dize olarak serileştirilmiş) — ayrıştırılıp
    /// maskelenir ve yine metin olarak döner; JSON değilse <c>null</c>.</summary>
    private static string? MaskEmbeddedJson(JsonNode? node)
    {
        if (node is not JsonValue v || !v.TryGetValue<string>(out var s)) return null;
        var t = s.TrimStart();
        if (t.Length == 0 || (t[0] != '{' && t[0] != '[')) return null;
        try
        {
            var inner = JsonNode.Parse(s);
            if (inner is null) return null;
            MaskNode(inner);
            return inner.ToJsonString();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record AuditDto(Guid Id, DateTimeOffset TarihUtc, string? Kullanici, string Tablo, string KayitId, string Islem,
    string? EskiDegerler, string? YeniDegerler);
