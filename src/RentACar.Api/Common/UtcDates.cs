using System.Text.Json;
using System.Text.Json.Serialization;

namespace RentACar.Api.Common;

/// <summary>
/// Harici API sınırında gelen her <see cref="DateTimeOffset"/>'i UTC'ye çevirir (DEVIR §5: DB'ye giden tarih UTC
/// olmalı; <c>/api/ui/v1</c>'deki <c>.ToUniversalTime()</c> deseninin sınırdaki tek noktası). Npgsql
/// <c>timestamptz</c>'a ofseti sıfır olmayan değeri yazmayı reddeder; <c>+03:00</c> girdi 500 üretiyordu.
/// Çeviri ANI korur: <c>Z</c> girdi aynen kalır, ofsetsiz girdi bugünkü gibi ayrıştırılır (yorumu değişmez) ve
/// yalnız ofseti 0'a indirilir. Servisler gün sınırını <c>.UtcDateTime</c>/<c>TenantDay</c> ile hesapladığı için
/// gün anlamı değişmez.
/// </summary>
public static class UtcDates
{
    /// <summary>Gövde: JSON okuyucu (yazım dokunulmaz; <c>DateTimeOffset?</c> da bu dönüştürücüyü kullanır).</summary>
    public sealed class JsonConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            // Yerleşik okuyucuyla AYNI ayrıştırma; hatalı biçim JsonException → 400 (FormatException 500 olurdu).
            => reader.TokenType == JsonTokenType.String && reader.TryGetDateTimeOffset(out var d)
                ? d.ToUniversalTime()
                : throw new JsonException("Geçersiz tarih biçimi (ISO 8601 bekleniyor).");

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
            => writer.WriteStringValue(value);
    }

    /// <summary>Sorgu/rota: uç filtresi — bağlanmış <c>DateTimeOffset</c>/<c>DateTimeOffset?</c> argümanlarını UTC'ye çevirir.</summary>
    public static async ValueTask<object?> Filter(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        for (var i = 0; i < ctx.Arguments.Count; i++)
            if (ctx.Arguments[i] is DateTimeOffset d) ctx.Arguments[i] = d.ToUniversalTime();
        return await next(ctx);
    }
}
