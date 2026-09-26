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
///   <item><see cref="Boyut"/> 1..<see cref="MaxSize"/> aralığına kırpılır (0/negatif → 1, 200 üstü → 200).
///         Reddetmek yerine kırpmak bilinçli: istemci "hepsini ver" diye büyük sayı yollarsa
///         400 yerine en fazla 200 satır alır — veritabanı yine korunur.</item>
///   <item><see cref="Sirala"/> boşluktan arındırılır; boş/yalnız-boşluk → null (varsayılan sıralama).
///         Biçim <c>"alan"</c> (artan) ya da <c>"-alan"</c> (azalan); alan adının GEÇERLİLİĞİ burada
///         değil, <see cref="SortFieldMap{T}"/>'nin beyaz listesinde denetlenir.</item>
/// </list>
/// </summary>
public sealed record ListeIstegi(int Sayfa = 1, int Boyut = 50, string? Sirala = null)
{
    /// <summary>Tek istekte dönebilecek en fazla kayıt (plan: <c>Boyut ≤ 200</c>).</summary>
    public const int MaxSize = 200;

    private readonly int _page = NormalizePage(Sayfa);
    private readonly int _size = NormalizeSize(Boyut);
    private readonly string? _sort = NormalizeSort(Sirala);

    /// <summary>1 tabanlı sayfa numarası; daima ≥ 1.</summary>
    public int Sayfa { get => _page; init => _page = NormalizePage(value); }

    /// <summary>Sayfa boyutu; daima 1..<see cref="MaxSize"/>.</summary>
    public int Boyut { get => _size; init => _size = NormalizeSize(value); }

    /// <summary>Kırpılmış sıralama anahtarı ya da null (varsayılan sıralama).</summary>
    public string? Sirala { get => _sort; init => _sort = NormalizeSort(value); }

    /// <summary>Atlanacak kayıt sayısı. <c>long</c>: çok büyük sayfa numarasında
    /// <c>(Sayfa-1)*Boyut</c> <c>int</c>'i taşırıp negatif Skip üretmesin.</summary>
    public long Atla => (long)(Sayfa - 1) * Boyut;

    /// <summary>
    /// <see cref="Sirala"/>'yı (alan, azalan) olarak ayrıştırır; null → sıralama istenmemiş.
    /// <c>"-"</c> tek başına alan adı boş bir ifadedir: <c>("", true)</c> döner ve beyaz liste
    /// onu geçersiz alan olarak reddeder (sessizce varsayılana düşmez).
    /// </summary>
    public (string Alan, bool Azalan)? ParseSort()
    {
        if (Sirala is null) return null;
        return Sirala.StartsWith('-')
            ? (Sirala[1..].Trim(), true)
            : (Sirala, false);
    }

    private static int NormalizePage(int page) => page < 1 ? 1 : page;

    private static int NormalizeSize(int size) => Math.Clamp(size, 1, MaxSize);

    private static string? NormalizeSort(string? sort)
        => string.IsNullOrWhiteSpace(sort) ? null : sort.Trim();
}
