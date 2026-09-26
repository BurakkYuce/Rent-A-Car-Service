using System.Linq.Expressions;

namespace RentACar.Application.Common;

/// <summary>
/// BEYAZ LİSTE sıralama (F1.3). İstemcinin <c>sirala</c> değeri asla ifade/kolon adına
/// çevrilmez (dinamik LINQ yok): yalnız burada kayıtlı alan adları kabul edilir, her biri
/// derleme zamanında yazılmış bir anahtar seçiciye eşlenir.
///
/// <para><b>Deterministik eşitlik bozucu:</b> her sıralamanın sonuna <c>ThenBy(eşitlik bozucu)</c>
/// eklenir (tipik olarak <c>Id</c>). Aynı anahtarlı satırlar olduğunda PostgreSQL sırayı garanti
/// etmez; bozucu olmadan Skip/Take sayfalar arasında satır tekrarlar ya da atlar. Bozucu birincil
/// alanın YÖNÜNÜ izler → <c>"-alan"</c> sonucu <c>"alan"</c> sonucunun tam tersidir.</para>
///
/// <para><b>Bilinmeyen alan → <see cref="ValidationException"/></b> (varsayılana sessizce düşmek
/// yerine RED): istemci yazım hatasını ya da desteklenmeyen sütunu fark etsin; aksi hâlde
/// "sıraladım" sanıp yanlış sıralı veri gösterir. <c>null</c>/boş istek → varsayılan sıralama.</para>
///
/// <para>Alan adları büyük/küçük harf duyarsız (Ordinal) eşleşir. Örnek:</para>
/// <code>
/// static readonly SiralamaHaritasi&lt;Vehicle&gt; Harita = SiralamaHaritasi&lt;Vehicle&gt;
///     .Olustur(v =&gt; v.Id)
///     .Alan("plaka", v =&gt; v.Plaka)
///     .Alan("olusturma", v =&gt; v.CreatedAtUtc)
///     .Varsayilan("-olusturma");
/// </code>
/// Örnekler değişmez değil (fluent çağrılar aynı örneği döndürür); bir kez kurulup
/// <c>static readonly</c> alanda paylaşılması amaçlanır — kurulumdan sonra salt okunur kullanılır.
/// </summary>
public sealed class SortFieldMap<T>
{
    /// <summary>Hata mesajında yankılanan kullanıcı girdisinin üst sınırı (log/yanıt şişmesin).</summary>
    private const int MaxPerMessage = 64;

    private readonly Dictionary<string, Func<IQueryable<T>, bool, IOrderedQueryable<T>>> _fields
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _sorted = [];
    private readonly Func<IOrderedQueryable<T>, bool, IOrderedQueryable<T>> _tieBreaker;
    private readonly Func<IQueryable<T>, IOrderedQueryable<T>> _tieBreakerOnly;
    private (string Alan, bool Azalan)? _default;

    private SortFieldMap(
        Func<IOrderedQueryable<T>, bool, IOrderedQueryable<T>> tieBreaker,
        Func<IQueryable<T>, IOrderedQueryable<T>> breakingOnly)
    {
        _tieBreaker = tieBreaker;
        _tieBreakerOnly = breakingOnly;
    }

    /// <summary>Haritayı zorunlu eşitlik bozucuyla başlatır (benzersiz bir anahtar olmalı, ör. <c>Id</c>).</summary>
    public static SortFieldMap<T> Create<TAnahtar>(Expression<Func<T, TAnahtar>> tieBreaker)
    {
        ArgumentNullException.ThrowIfNull(tieBreaker);
        return new(
            (q, descending) => descending ? q.ThenByDescending(tieBreaker) : q.ThenBy(tieBreaker),
            q => q.OrderBy(tieBreaker));
    }

    /// <summary>İzin verilen alanlar (kayıt sırasıyla) — hata mesajı ve dokümantasyon için.</summary>
    public IReadOnlyList<string> Fields => _sorted;

    /// <summary>Beyaz listeye bir alan ekler. Aynı ad iki kez eklenemez (programcı hatası).</summary>
    public SortFieldMap<T> Alan<TAnahtar>(string name, Expression<Func<T, TAnahtar>> key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(key);
        if (name.StartsWith('-') || name.Trim() != name)
            throw new ArgumentException($"Sıralama alan adı '-' ile başlayamaz ve boşluk içeremez: '{name}'.", nameof(name));
        if (!_fields.TryAdd(name, (q, descending) => descending ? q.OrderByDescending(key) : q.OrderBy(key)))
            throw new ArgumentException($"Sıralama alanı iki kez tanımlandı: '{name}'.", nameof(name));
        _sorted.Add(name);
        return this;
    }

    /// <summary>
    /// <c>sirala</c> boşken kullanılacak sıralama (<c>"alan"</c> / <c>"-alan"</c>). Alan önceden
    /// <see cref="Alan{TAnahtar}"/> ile tanımlanmış olmalı. Hiç verilmezse yalnız eşitlik bozucu
    /// (artan) uygulanır — sıra yine deterministiktir.
    /// </summary>
    public SortFieldMap<T> Default(string sort)
    {
        var resolve = new ListeIstegi(Sirala: sort).ParseSort()
            ?? throw new ArgumentException("Varsayılan sıralama boş olamaz.", nameof(sort));
        if (!_fields.ContainsKey(resolve.Alan))
            throw new ArgumentException($"Varsayılan sıralama tanımsız alana işaret ediyor: '{resolve.Alan}'.", nameof(sort));
        _default = resolve;
        return this;
    }

    /// <summary>
    /// Sıralamayı uygular. <paramref name="sort"/> null/boş → varsayılan; beyaz listede
    /// olmayan alan → <see cref="ValidationException"/>. Sonuç daima eşitlik bozucuyla biter.
    /// </summary>
    public IOrderedQueryable<T> Apply(IQueryable<T> source, string? sort)
    {
        ArgumentNullException.ThrowIfNull(source);
        var request = new ListeIstegi(Sirala: sort).ParseSort() ?? _default;

        if (request is not { } selection)
            return _tieBreakerOnly(source);

        if (!_fields.TryGetValue(selection.Alan, out var fieldSort))
            // TODO(F1.1 birleşince): ValidationException(mesaj, alan: "sirala") — ProblemDetails errors.sirala.
            throw new ValidationException(
                $"Geçersiz sıralama alanı: '{Clamp(selection.Alan)}'. İzin verilenler: {string.Join(", ", _sorted)}.");

        return _tieBreaker(fieldSort(source, selection.Azalan), selection.Azalan);
    }

    private static string Clamp(string s) => s.Length <= MaxPerMessage ? s : s[..MaxPerMessage] + "…";
}
