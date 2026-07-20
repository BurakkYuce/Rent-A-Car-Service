using RentACar.Domain.Enums;

namespace RentACar.Application.BelgeSablon;

/// <summary>
/// Çözülmüş belge şablonu bölümleri (PDF renderer'a taşınır). Her alan ya seçili/varsayılan şablonun
/// metnidir ya da null → renderer koddaki <see cref="BelgeSablonVarsayilan"/> sabitini basar (kısmi
/// override serbest). Token'lar ({BelgeNo}/{Tarih}/{Firma*}) render ANINDA <see cref="SablonToken"/>
/// ile konur (belge no/tarih o an bilinir; çözümleme anında değil).
/// </summary>
public sealed record SablonMetin(
    string? Baslik, string? HukukiMetinSol, string? HukukiMetinSag,
    string? EkKosullarVarsayilan, string? AltBilgi)
{
    public static readonly SablonMetin Bos = new(null, null, null, null, null);
}

/// <summary>Belge şablonu çözümleyici — YAZDIRMA anında okur. Admin-gate'li BelgeSablonService'i ATLAR
/// (yazdırma OperationsWrite/FinanceWrite yetkisiyle çalışır; ManageUsers'a takılmaz — KdvVarsayilan deseni).
/// Doğrudan repository'den okur; RLS tenant izolasyonunu zaten uygular.</summary>
public sealed class BelgeSablonCozumleyici(IBelgeSablonRepository repository)
{
    /// <summary>Kira sözleşmesi için: seçili şablon (varsa/doğru türse — pasif olsa da açık seçim onurlanır),
    /// yoksa tür varsayılanı, o da yoksa boş (renderer sabiti basar).</summary>
    public async Task<SablonMetin> KiraAsync(Guid? sablonId, CancellationToken ct = default)
    {
        var s = sablonId is Guid id ? await repository.FindAsync(id, ct) : null;
        if (s is null || s.BelgeTuru != BelgeTuru.KiraSozlesmesi)
            s = await repository.FindDefaultAsync(BelgeTuru.KiraSozlesmesi, ct);
        return Map(s);
    }

    /// <summary>Fatura/makbuz için: tür varsayılan şablonu (per-belge seçim yok), yoksa boş.</summary>
    public async Task<SablonMetin> VarsayilanAsync(BelgeTuru turu, CancellationToken ct = default)
        => Map(await repository.FindDefaultAsync(turu, ct));

    private static SablonMetin Map(Domain.Entities.BelgeSablon? s) => s is null ? SablonMetin.Bos
        : new SablonMetin(s.BelgeBasligi, s.HukukiMetinSol, s.HukukiMetinSag, s.EkKosullarVarsayilan, s.AltBilgi);
}

/// <summary>Şablon metnindeki {Anahtar} yer-tutucularını değerle değiştirir (render anında). Boş değer →
/// yer-tutucu silinir. Büyük/küçük harf duyarsız.</summary>
public static class SablonToken
{
    public static string? Uygula(string? metin, IReadOnlyDictionary<string, string?> sozluk)
    {
        if (string.IsNullOrEmpty(metin)) return metin;
        foreach (var (k, v) in sozluk)
            metin = metin.Replace("{" + k + "}", v ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        return metin;
    }
}
