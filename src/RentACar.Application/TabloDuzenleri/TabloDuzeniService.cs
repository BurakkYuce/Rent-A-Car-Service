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
public interface ITabloDuzeniRepository
{
    Task<TabloDuzeniKaydi?> GetirAsync(Guid userId, string tabloKodu, CancellationToken ct = default);

    /// <summary>Upsert (kullanıcı × tablo tek satır; son yazan kazanır). Yazılan anı döner.</summary>
    Task<DateTimeOffset> YazAsync(Guid userId, string tabloKodu, string duzenJson, DateTimeOffset simdi, CancellationToken ct = default);

    /// <summary>Satırı siler; yoksa sessizce hiçbir şey yapmaz.</summary>
    Task SilAsync(Guid userId, string tabloKodu, CancellationToken ct = default);
}

/// <summary>
/// Yeni arayüzün kişisel tablo düzenleri (F3.5): sütun sırası/görünürlüğü/genişliği + sıralama.
///
/// <para><b>Yetki modeli:</b> izin matrisine bağlı DEĞİL — her oturum kendi düzenini yönetir. Kapı
/// KİMLİKTİR: kullanıcı yalnız <see cref="ICurrentUser.UserId"/>'den gelir; hiçbir metot kullanıcı
/// parametresi almaz, dolayısıyla başkasının düzenini okumak/yazmak yapısal olarak ifade edilemez.
/// Oturumsuz ya da tenant'sız çağrı <see cref="YetkiYokException"/>.</para>
///
/// <para><b>Doğrulama sunucuda:</b> istemci JSON'u olduğu gibi saklanmaz — şemalı DTO doğrulanıp
/// yeniden serileştirilir (boyut sınırlı: ≤ 200 sütun, ≤ 5 sıralama, kodlar kısıtlı karakter kümesinde).</para>
/// </summary>
public sealed partial class TabloDuzeniService(
    ITabloDuzeniRepository repository, ICurrentUser currentUser, ITenantContext tenant)
{
    public const int EnFazlaSutun = 200;
    public const int EnFazlaSiralama = 5;
    public const int EnAzGenislik = 24;
    public const int EnFazlaGenislik = 2000;
    public const int EnFazlaKodUzunlugu = 64;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // \A…\z: .NET'te "$" sondaki "\n"yi de kabul eder ("tablo\n" geçerdi).
    [GeneratedRegex(@"\A[a-z0-9]+([.-][a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex TabloKoduDeseni();

    [GeneratedRegex(@"\A[A-Za-z0-9_.-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SutunKoduDeseni();

    public async Task<TabloDuzeniYaniti> GetirAsync(string tabloKodu, CancellationToken ct = default)
    {
        var kullanici = Kullanici();
        TabloKoduDogrula(tabloKodu);
        var kayit = await repository.GetirAsync(kullanici, tabloKodu, ct);
        if (kayit is null) return new TabloDuzeniYaniti(tabloKodu, null, null);

        TabloDuzeniVerisi? duzen;
        try
        {
            duzen = JsonSerializer.Deserialize<TabloDuzeniVerisi>(kayit.Duzen, Json);
        }
        catch (JsonException)
        {
            // Bozuk satır (elle müdahale) sayfayı düşürmez: varsayılan düzen gösterilir, sonraki kayıt üzerine yazar.
            duzen = null;
        }
        return new TabloDuzeniYaniti(tabloKodu, duzen, duzen is null ? null : kayit.GuncellemeUtc);
    }

    public async Task<TabloDuzeniYaniti> KaydetAsync(string tabloKodu, TabloDuzeniVerisi? duzen, CancellationToken ct = default)
    {
        var kullanici = Kullanici();
        TabloKoduDogrula(tabloKodu);
        var temiz = Dogrula(duzen);
        // PG timestamptz mikrosaniye tutar; Linux'ta DateTimeOffset 100 ns — yanıt ile sonraki GET aynı anı göstersin.
        var simdi = DateTimeOffset.UtcNow;
        simdi = simdi.AddTicks(-(simdi.Ticks % 10));
        var an = await repository.YazAsync(kullanici, tabloKodu, JsonSerializer.Serialize(temiz, Json), simdi, ct);
        return new TabloDuzeniYaniti(tabloKodu, temiz, an);
    }

    public async Task SifirlaAsync(string tabloKodu, CancellationToken ct = default)
    {
        var kullanici = Kullanici();
        TabloKoduDogrula(tabloKodu);
        await repository.SilAsync(kullanici, tabloKodu, ct);
    }

    private Guid Kullanici()
    {
        if (tenant.TenantId is null || currentUser.UserId is not { } id || id == Guid.Empty)
            throw new YetkiYokException("Tablo düzeni için oturum açmış bir kullanıcı gerekir.");
        return id;
    }

    private static void TabloKoduDogrula(string? tabloKodu)
    {
        if (string.IsNullOrEmpty(tabloKodu) || tabloKodu.Length > EnFazlaKodUzunlugu || !TabloKoduDeseni().IsMatch(tabloKodu))
            throw new ValidationException(
                "Tablo kodu geçersiz (küçük harf, rakam, nokta ve tire; en fazla 64 karakter).", "tabloKodu");
    }

    /// <summary>Gövdeyi doğrular ve TEMİZ bir kopya üretir (yalnız bilinen alanlar yazılır).</summary>
    private static TabloDuzeniVerisi Dogrula(TabloDuzeniVerisi? duzen)
    {
        if (duzen is null) throw new ValidationException("Tablo düzeni boş olamaz.", "duzen");
        if (duzen.Sutunlar is null || duzen.Sutunlar.Count == 0)
            throw new ValidationException("En az bir sütun gerekir.", "sutunlar");
        if (duzen.Sutunlar.Count > EnFazlaSutun)
            throw new ValidationException($"En fazla {EnFazlaSutun} sütun kaydedilebilir.", "sutunlar");
        if (duzen.Siralama is null)
            throw new ValidationException("Sıralama listesi eksik (sıralama yoksa boş liste gönderin).", "siralama");
        if (duzen.Siralama.Count > EnFazlaSiralama)
            throw new ValidationException($"En fazla {EnFazlaSiralama} sıralama anahtarı kaydedilebilir.", "siralama");

        var kodlar = new HashSet<string>(StringComparer.Ordinal);
        var sutunlar = new List<TabloSutunDuzeni>(duzen.Sutunlar.Count);
        foreach (var s in duzen.Sutunlar)
        {
            if (s is null || !KodGecerli(s.Kod))
                throw new ValidationException("Sütun kodu geçersiz (harf, rakam, _ . -; en fazla 64 karakter).", "sutunlar");
            if (!kodlar.Add(s.Kod))
                throw new ValidationException($"Sütun kodu tekrarlanıyor: {s.Kod}", "sutunlar");
            if (s.Genislik is { } g && (g < EnAzGenislik || g > EnFazlaGenislik))
                throw new ValidationException(
                    $"Sütun genişliği {EnAzGenislik}–{EnFazlaGenislik} piksel aralığında olmalı ({s.Kod}).", "sutunlar");
            sutunlar.Add(new TabloSutunDuzeni(s.Kod, s.Gorunur, s.Genislik));
        }

        var sirali = new HashSet<string>(StringComparer.Ordinal);
        var siralama = new List<TabloSiralamaDuzeni>(duzen.Siralama.Count);
        foreach (var s in duzen.Siralama)
        {
            if (s is null || !KodGecerli(s.Kod))
                throw new ValidationException("Sıralama sütun kodu geçersiz.", "siralama");
            if (!kodlar.Contains(s.Kod))
                throw new ValidationException($"Sıralama düzende olmayan bir sütuna işaret ediyor: {s.Kod}", "siralama");
            if (!sirali.Add(s.Kod))
                throw new ValidationException($"Sıralama anahtarı tekrarlanıyor: {s.Kod}", "siralama");
            siralama.Add(new TabloSiralamaDuzeni(s.Kod, s.Azalan));
        }
        return new TabloDuzeniVerisi(sutunlar, siralama);
    }

    private static bool KodGecerli(string? kod) => kod is not null && SutunKoduDeseni().IsMatch(kod);
}
