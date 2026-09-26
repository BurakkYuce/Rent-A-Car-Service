using System.Text.Json;
using System.Text.RegularExpressions;
using RentACar.Application.Common;
using RentACar.Domain.Common;

namespace RentACar.Application.TabloDuzenleri;

/// <summary>Bir sütunun kullanıcı düzeni. Sıra = listedeki konum; <c>Genislik</c> null = varsayılan genişlik.</summary>
public sealed record TabloSutunDuzeni(string Kod, bool Gorunur, int? Genislik);

/// <summary>Sıralama anahtarı (çoklu sıralamada öncelik = listedeki konum).</summary>
public sealed record TabloSiralamaDuzeni(string Kod, bool Azalan);

/// <summary>Kaydedilen düzen — hem PUT gövdesi hem GET yanıtındaki <c>duzen</c>.</summary>
public sealed record TabloDuzeniVerisi(IReadOnlyList<TabloSutunDuzeni> Sutunlar, IReadOnlyList<TabloSiralamaDuzeni> Siralama);

/// <summary><c>Duzen</c> null = kullanıcının bu tablo için kayıtlı düzeni yok (istemci varsayılanı kullanır).</summary>
public sealed record TabloDuzeniYaniti(string TabloKodu, TabloDuzeniVerisi? Duzen, DateTimeOffset? GuncellemeUtc);

/// <summary>Depodaki ham satır (serileştirilmiş düzen + son yazım anı).</summary>
public sealed record TabloDuzeniKaydi(string Duzen, DateTimeOffset GuncellemeUtc);

/// <summary>
/// Kalıcılık. Tenant boyutu EF filtresi + FORCE RLS'ten gelir; KULLANICI boyutu her çağrıda açık
/// <paramref name="userId"/> parametresiyle — repo kullanıcıyı kendisi seçmez, servis oturumdan verir.
/// </summary>
public interface ITableLayoutRepository
{
    Task<TabloDuzeniKaydi?> FetchAsync(Guid userId, string tableCode, CancellationToken ct = default);

    /// <summary>Upsert (kullanıcı × tablo tek satır; son yazan kazanır). Yazılan anı döner.</summary>
    Task<DateTimeOffset> WriteAsync(Guid userId, string tableCode, string layoutJson, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Satırı siler; yoksa sessizce hiçbir şey yapmaz.</summary>
    Task DeleteAsync(Guid userId, string tableCode, CancellationToken ct = default);
}

/// <summary>
/// Yeni arayüzün kişisel tablo düzenleri (F3.5): sütun sırası/görünürlüğü/genişliği + sıralama.
///
/// <para><b>Yetki modeli:</b> izin matrisine bağlı DEĞİL — her oturum kendi düzenini yönetir. Kapı
/// KİMLİKTİR: kullanıcı yalnız <see cref="ICurrentUser.UserId"/>'den gelir; hiçbir metot kullanıcı
/// parametresi almaz, dolayısıyla başkasının düzenini okumak/yazmak yapısal olarak ifade edilemez.
/// Oturumsuz ya da tenant'sız çağrı <see cref="NoPermissionException"/>.</para>
///
/// <para><b>Doğrulama sunucuda:</b> istemci JSON'u olduğu gibi saklanmaz — şemalı DTO doğrulanıp
/// yeniden serileştirilir (boyut sınırlı: ≤ 200 sütun, ≤ 5 sıralama, kodlar kısıtlı karakter kümesinde).</para>
/// </summary>
public sealed partial class TableLayoutService(
    ITableLayoutRepository repository, ICurrentUser currentUser, ITenantContext tenant)
{
    public const int MaxColumns = 200;
    public const int MaxSorts = 5;
    public const int MinWidth = 24;
    public const int MaxWidthPx = 2000;
    public const int MaxCodeLength = 64;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // \A…\z: .NET'te "$" sondaki "\n"yi de kabul eder ("tablo\n" geçerdi).
    [GeneratedRegex(@"\A[a-z0-9]+([.-][a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex TableCodePattern();

    [GeneratedRegex(@"\A[A-Za-z0-9_.-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ColumnCodePattern();

    public async Task<TabloDuzeniYaniti> FetchAsync(string tableCode, CancellationToken ct = default)
    {
        var user = User();
        ValidateTableCode(tableCode);
        var record = await repository.FetchAsync(user, tableCode, ct);
        if (record is null) return new TabloDuzeniYaniti(tableCode, null, null);

        TabloDuzeniVerisi? layout;
        try
        {
            layout = JsonSerializer.Deserialize<TabloDuzeniVerisi>(record.Duzen, Json);
        }
        catch (JsonException)
        {
            // Bozuk satır (elle müdahale) sayfayı düşürmez: varsayılan düzen gösterilir, sonraki kayıt üzerine yazar.
            layout = null;
        }
        return new TabloDuzeniYaniti(tableCode, layout, layout is null ? null : record.GuncellemeUtc);
    }

    public async Task<TabloDuzeniYaniti> SaveAsync(string tableCode, TabloDuzeniVerisi? layout, CancellationToken ct = default)
    {
        var user = User();
        ValidateTableCode(tableCode);
        var clean = Validate(layout);
        // PG timestamptz mikrosaniye tutar; Linux'ta DateTimeOffset 100 ns — yanıt ile sonraki GET aynı anı göstersin.
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % 10));
        var an = await repository.WriteAsync(user, tableCode, JsonSerializer.Serialize(clean, Json), now, ct);
        return new TabloDuzeniYaniti(tableCode, clean, an);
    }

    public async Task ResetAsync(string tableCode, CancellationToken ct = default)
    {
        var user = User();
        ValidateTableCode(tableCode);
        await repository.DeleteAsync(user, tableCode, ct);
    }

    private Guid User()
    {
        if (tenant.TenantId is null || currentUser.UserId is not { } id || id == Guid.Empty)
            throw new NoPermissionException("Tablo düzeni için oturum açmış bir kullanıcı gerekir.");
        return id;
    }

    private static void ValidateTableCode(string? tableCode)
    {
        if (string.IsNullOrEmpty(tableCode) || tableCode.Length > MaxCodeLength || !TableCodePattern().IsMatch(tableCode))
            throw new ValidationException(
                "Tablo kodu geçersiz (küçük harf, rakam, nokta ve tire; en fazla 64 karakter).", "tabloKodu");
    }

    /// <summary>Gövdeyi doğrular ve TEMİZ bir kopya üretir (yalnız bilinen alanlar yazılır).</summary>
    private static TabloDuzeniVerisi Validate(TabloDuzeniVerisi? layout)
    {
        if (layout is null) throw new ValidationException("Tablo düzeni boş olamaz.", "duzen");
        if (layout.Sutunlar is null || layout.Sutunlar.Count == 0)
            throw new ValidationException("En az bir sütun gerekir.", "sutunlar");
        if (layout.Sutunlar.Count > MaxColumns)
            throw new ValidationException($"En fazla {MaxColumns} sütun kaydedilebilir.", "sutunlar");
        if (layout.Siralama is null)
            throw new ValidationException("Sıralama listesi eksik (sıralama yoksa boş liste gönderin).", "siralama");
        if (layout.Siralama.Count > MaxSorts)
            throw new ValidationException($"En fazla {MaxSorts} sıralama anahtarı kaydedilebilir.", "siralama");

        var codes = new HashSet<string>(StringComparer.Ordinal);
        var columns = new List<TabloSutunDuzeni>(layout.Sutunlar.Count);
        foreach (var s in layout.Sutunlar)
        {
            if (s is null || !IsCodeValid(s.Kod))
                throw new ValidationException("Sütun kodu geçersiz (harf, rakam, _ . -; en fazla 64 karakter).", "sutunlar");
            if (!codes.Add(s.Kod))
                throw new ValidationException($"Sütun kodu tekrarlanıyor: {s.Kod}", "sutunlar");
            if (s.Genislik is { } g && (g < MinWidth || g > MaxWidthPx))
                throw new ValidationException(
                    $"Sütun genişliği {MinWidth}–{MaxWidthPx} piksel aralığında olmalı ({s.Kod}).", "sutunlar");
            columns.Add(new TabloSutunDuzeni(s.Kod, s.Gorunur, s.Genislik));
        }

        var sorted = new HashSet<string>(StringComparer.Ordinal);
        var sort = new List<TabloSiralamaDuzeni>(layout.Siralama.Count);
        foreach (var s in layout.Siralama)
        {
            if (s is null || !IsCodeValid(s.Kod))
                throw new ValidationException("Sıralama sütun kodu geçersiz.", "siralama");
            if (!codes.Contains(s.Kod))
                throw new ValidationException($"Sıralama düzende olmayan bir sütuna işaret ediyor: {s.Kod}", "siralama");
            if (!sorted.Add(s.Kod))
                throw new ValidationException($"Sıralama anahtarı tekrarlanıyor: {s.Kod}", "siralama");
            sort.Add(new TabloSiralamaDuzeni(s.Kod, s.Azalan));
        }
        return new TabloDuzeniVerisi(columns, sort);
    }

    private static bool IsCodeValid(string? code) => code is not null && ColumnCodePattern().IsMatch(code);
}
