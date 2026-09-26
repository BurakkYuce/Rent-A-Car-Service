using RentACar.Application.Common;

namespace RentACar.Web.Api;

/// <summary>
/// F4.1 — alan bazlı doğrulama, UÇ KATMANINDA. Servisler tarihsel olarak alansız
/// <see cref="ValidationException"/> fırlatıyor (mesaj alanı zaten söylüyor: "Dönüş KM, çıkış KM'den küçük
/// olamaz."); servisleri toptan yeniden yazmak yerine uç, bilinen mesaj öneklerini gövdedeki alan adına
/// (JSON camelCase) eşler → ProblemDetails <c>errors[alan]</c> ve SPA hatayı alanın altında gösterir.
/// <list type="bullet">
/// <item>YALNIZ tam tip <see cref="ValidationException"/> ve alanı BOŞ olan eşlenir: alt tipler
/// (<see cref="NoPermissionException"/>, <see cref="DuplicateOperationException"/>, çakışma istisnaları) kodlarını
/// (403/409) korur; servisin kendisi alan verdiyse ona dokunulmaz.</item>
/// <item>Eşleşme <b>önek</b> ve Ordinal: mesajın başı sabit metin, sonu değişken (tutar/limit) olabilir.</item>
/// <item>Eşleşmeyen mesaj alansız 400 kalır (form üstü genel hata) — yanlış alana düşürmekten iyidir.</item>
/// </list>
/// </summary>
public static class FieldMapping
{
    /// <summary>Uç filtresi: kurallar sırayla denenir, İLK eşleşen alan kazanır.</summary>
    public static TBuilder MapFields<TBuilder>(this TBuilder builder, IReadOnlyList<(string Onek, string Alan)> rules)
        where TBuilder : IEndpointConventionBuilder
        => builder.AddEndpointFilter(async (c, next) =>
        {
            try
            {
                return await next(c);
            }
            catch (ValidationException ex) when (ex.GetType() == typeof(ValidationException) && ex.Alan is null
                                                 && Find(ex.Message, rules) is { } alan)
            {
                throw new ValidationException(ex.Message, alan);
            }
        });

    /// <summary>Saf eşleme (test edilebilir).</summary>
    public static string? Find(string message, IReadOnlyList<(string Onek, string Alan)> rules)
    {
        foreach (var (prefix, alan) in rules)
            if (message.StartsWith(prefix, StringComparison.Ordinal))
                return alan;
        return null;
    }
}
