using System.Linq.Expressions;
using System.Reflection;
using RentACar.Application.Common;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// <see cref="TurkishText.Normalize"/>'ın SQL karşılığı (F1.6): <c>lower(replace(replace(alan,'İ','i'),…))</c> içinde
/// katlanmış terimi arar. Çevrim tablosu <see cref="TurkishText.Esleme"/>'den üretilir — C# ve SQL kuralı ayrışamaz.
/// <para>Terim SQL PARAMETRESİ olarak gider (sabit değil): her farklı arama metni EF sorgu önbelleğine
/// yeni bir giriş eklemesin ve metin SQL'e gömülmesin. <c>Contains</c> parametreyle <c>strpos</c>/kaçışlı
/// LIKE'a çevrilir; kullanıcının yazdığı <c>%</c>/<c>_</c> joker olarak yorumlanmaz.</para>
/// </summary>
internal static class TrSql
{
    private static readonly MethodInfo Replace = typeof(string).GetMethod(nameof(string.Replace), [typeof(string), typeof(string)])!;
    private static readonly MethodInfo ToLower = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
    private static readonly MethodInfo Contains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;

    /// <summary>Parametreleştirme kutusu: sabit nesnenin üye erişimini EF parametreye çevirir.</summary>
    private sealed class Kutu(string deger)
    {
        public string Deger { get; } = deger;
    }

    /// <summary><paramref name="alan"/> katlanmış biçimde <paramref name="katlanmisTerim"/>'i içeriyor mu.
    /// Terim ÖNCEDEN <see cref="TurkishText.Normalize"/>'dan geçmiş olmalı.</summary>
    public static Expression<Func<T, bool>> Icerir<T>(Expression<Func<T, string>> alan, string katlanmisTerim)
    {
        Expression govde = alan.Body;
        foreach (var (k, h) in TurkishText.Esleme)
            govde = Expression.Call(govde, Replace, Expression.Constant(k.ToString()), Expression.Constant(h.ToString()));
        govde = Expression.Call(govde, ToLower);
        var terim = Expression.Property(Expression.Constant(new Kutu(katlanmisTerim)), nameof(Kutu.Deger));
        return Expression.Lambda<Func<T, bool>>(Expression.Call(govde, Contains, terim), alan.Parameters);
    }
}
