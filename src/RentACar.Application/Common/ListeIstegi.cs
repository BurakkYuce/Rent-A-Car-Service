namespace RentACar.Application.Common;

/// <summary>
/// <c>/api/ui</c> liste sözleşmesinin İSTEK yarısı (F1.3): sayfa numarası, sayfa boyutu ve
/// sıralama anahtarı. Yanıt yarısı <see cref="Sayfa{T}"/>.
///
/// <para><b>Normalizasyon yapıcıda</b> yapılır ve <c>with</c> ifadesinde de korunur (init
/// erişimcisi aynı kuralı uygular) — geçersiz bir istek nesnesi var olamaz, tüketici
/// değerleri yeniden kontrol etmez:</para>
/// <list type="bullet">
///   <item><see cref="Sayfa"/> &lt; 1 → 1 (üst sınır yok; son sayfanın ötesi boş sayfa döner).</item>
///   <item><see cref="Boyut"/> 1..<see cref="EnFazlaBoyut"/> aralığına kırpılır (0/negatif → 1, 200 üstü → 200).
///         Reddetmek yerine kırpmak bilinçli: istemci "hepsini ver" diye büyük sayı yollarsa
///         400 yerine en fazla 200 satır alır — veritabanı yine korunur.</item>
///   <item><see cref="Sirala"/> boşluktan arındırılır; boş/yalnız-boşluk → null (varsayılan sıralama).
///         Biçim <c>"alan"</c> (artan) ya da <c>"-alan"</c> (azalan); alan adının GEÇERLİLİĞİ burada
///         değil, <see cref="SiralamaHaritasi{T}"/>'nin beyaz listesinde denetlenir.</item>
/// </list>
/// </summary>
public sealed record ListeIstegi(int Sayfa = 1, int Boyut = 50, string? Sirala = null)
{
    /// <summary>Tek istekte dönebilecek en fazla kayıt (plan: <c>Boyut ≤ 200</c>).</summary>
    public const int EnFazlaBoyut = 200;

    private readonly int _sayfa = SayfaNormalize(Sayfa);
    private readonly int _boyut = BoyutNormalize(Boyut);
    private readonly string? _sirala = SiralaNormalize(Sirala);

    /// <summary>1 tabanlı sayfa numarası; daima ≥ 1.</summary>
    public int Sayfa { get => _sayfa; init => _sayfa = SayfaNormalize(value); }

    /// <summary>Sayfa boyutu; daima 1..<see cref="EnFazlaBoyut"/>.</summary>
    public int Boyut { get => _boyut; init => _boyut = BoyutNormalize(value); }

    /// <summary>Kırpılmış sıralama anahtarı ya da null (varsayılan sıralama).</summary>
    public string? Sirala { get => _sirala; init => _sirala = SiralaNormalize(value); }

    /// <summary>Atlanacak kayıt sayısı. <c>long</c>: çok büyük sayfa numarasında
    /// <c>(Sayfa-1)*Boyut</c> <c>int</c>'i taşırıp negatif Skip üretmesin.</summary>
    public long Atla => (long)(Sayfa - 1) * Boyut;

    /// <summary>
    /// <see cref="Sirala"/>'yı (alan, azalan) olarak ayrıştırır; null → sıralama istenmemiş.
    /// <c>"-"</c> tek başına alan adı boş bir ifadedir: <c>("", true)</c> döner ve beyaz liste
    /// onu geçersiz alan olarak reddeder (sessizce varsayılana düşmez).
    /// </summary>
    public (string Alan, bool Azalan)? SiralamaCoz()
    {
        if (Sirala is null) return null;
        return Sirala.StartsWith('-')
            ? (Sirala[1..].Trim(), true)
            : (Sirala, false);
    }

    private static int SayfaNormalize(int sayfa) => sayfa < 1 ? 1 : sayfa;

    private static int BoyutNormalize(int boyut) => Math.Clamp(boyut, 1, EnFazlaBoyut);

    private static string? SiralaNormalize(string? sirala)
        => string.IsNullOrWhiteSpace(sirala) ? null : sirala.Trim();
}
