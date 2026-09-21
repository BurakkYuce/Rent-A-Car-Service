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
public sealed class SiralamaHaritasi<T>
{
    /// <summary>Hata mesajında yankılanan kullanıcı girdisinin üst sınırı (log/yanıt şişmesin).</summary>
    private const int MesajdaEnFazla = 64;

    private readonly Dictionary<string, Func<IQueryable<T>, bool, IOrderedQueryable<T>>> _alanlar
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _sirali = [];
    private readonly Func<IOrderedQueryable<T>, bool, IOrderedQueryable<T>> _esitlikBozucu;
    private readonly Func<IQueryable<T>, IOrderedQueryable<T>> _yalnizBozucu;
    private (string Alan, bool Azalan)? _varsayilan;

    private SiralamaHaritasi(
        Func<IOrderedQueryable<T>, bool, IOrderedQueryable<T>> esitlikBozucu,
        Func<IQueryable<T>, IOrderedQueryable<T>> yalnizBozucu)
    {
        _esitlikBozucu = esitlikBozucu;
        _yalnizBozucu = yalnizBozucu;
    }

    /// <summary>Haritayı zorunlu eşitlik bozucuyla başlatır (benzersiz bir anahtar olmalı, ör. <c>Id</c>).</summary>
    public static SiralamaHaritasi<T> Olustur<TAnahtar>(Expression<Func<T, TAnahtar>> esitlikBozucu)
    {
        ArgumentNullException.ThrowIfNull(esitlikBozucu);
        return new(
            (q, azalan) => azalan ? q.ThenByDescending(esitlikBozucu) : q.ThenBy(esitlikBozucu),
            q => q.OrderBy(esitlikBozucu));
    }

    /// <summary>İzin verilen alanlar (kayıt sırasıyla) — hata mesajı ve dokümantasyon için.</summary>
    public IReadOnlyList<string> Alanlar => _sirali;

    /// <summary>Beyaz listeye bir alan ekler. Aynı ad iki kez eklenemez (programcı hatası).</summary>
    public SiralamaHaritasi<T> Alan<TAnahtar>(string ad, Expression<Func<T, TAnahtar>> anahtar)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ad);
        ArgumentNullException.ThrowIfNull(anahtar);
        if (ad.StartsWith('-') || ad.Trim() != ad)
            throw new ArgumentException($"Sıralama alan adı '-' ile başlayamaz ve boşluk içeremez: '{ad}'.", nameof(ad));
        if (!_alanlar.TryAdd(ad, (q, azalan) => azalan ? q.OrderByDescending(anahtar) : q.OrderBy(anahtar)))
            throw new ArgumentException($"Sıralama alanı iki kez tanımlandı: '{ad}'.", nameof(ad));
        _sirali.Add(ad);
        return this;
    }

    /// <summary>
    /// <c>sirala</c> boşken kullanılacak sıralama (<c>"alan"</c> / <c>"-alan"</c>). Alan önceden
    /// <see cref="Alan{TAnahtar}"/> ile tanımlanmış olmalı. Hiç verilmezse yalnız eşitlik bozucu
    /// (artan) uygulanır — sıra yine deterministiktir.
    /// </summary>
    public SiralamaHaritasi<T> Varsayilan(string sirala)
    {
        var coz = new ListeIstegi(Sirala: sirala).SiralamaCoz()
            ?? throw new ArgumentException("Varsayılan sıralama boş olamaz.", nameof(sirala));
        if (!_alanlar.ContainsKey(coz.Alan))
            throw new ArgumentException($"Varsayılan sıralama tanımsız alana işaret ediyor: '{coz.Alan}'.", nameof(sirala));
        _varsayilan = coz;
        return this;
    }

    /// <summary>
    /// Sıralamayı uygular. <paramref name="sirala"/> null/boş → varsayılan; beyaz listede
    /// olmayan alan → <see cref="ValidationException"/>. Sonuç daima eşitlik bozucuyla biter.
    /// </summary>
    public IOrderedQueryable<T> Uygula(IQueryable<T> kaynak, string? sirala)
    {
        ArgumentNullException.ThrowIfNull(kaynak);
        var istek = new ListeIstegi(Sirala: sirala).SiralamaCoz() ?? _varsayilan;

        if (istek is not { } secim)
            return _yalnizBozucu(kaynak);

        if (!_alanlar.TryGetValue(secim.Alan, out var alanSirala))
            // TODO(F1.1 birleşince): ValidationException(mesaj, alan: "sirala") — ProblemDetails errors.sirala.
            throw new ValidationException(
                $"Geçersiz sıralama alanı: '{Kirp(secim.Alan)}'. İzin verilenler: {string.Join(", ", _sirali)}.");

        return _esitlikBozucu(alanSirala(kaynak, secim.Azalan), secim.Azalan);
    }

    private static string Kirp(string s) => s.Length <= MesajdaEnFazla ? s : s[..MesajdaEnFazla] + "…";
}
