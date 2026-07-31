using RentACar.Infrastructure.Security;

namespace RentACar.IntegrationTests;

/// <summary>
/// DataProtection key-ring dizininin ÇÖZÜMÜ — regresyon kilidi.
///
/// <para><b>Neden var:</b> fallback yolu <c>AppContext.BaseDirectory/dp-keys</c> idi, yani key-ring
/// build çıktısının içinde yaşıyordu. Bir <c>obj/bin</c> temizliği anahtarları sildi ve yerel DB'deki
/// 29 cipher (17 <c>TcKimlikEnc</c> + 12 <c>PasaportNoEnc</c>) kalıcı olarak çözülemez hale geldi —
/// düz metin kolonları <c>PiiBackfill</c> çoktan null'lamıştı. Üretimde aynı hata "publish dizinini
/// üzerine yaz" ile tekrar ederdi.</para>
///
/// Bağımsız oracle: beklenen değerler senaryodan ("anahtarlar build çıktısında YAŞAMAMALI"),
/// üretim kodunun kendi ifadesinden değil.
/// </summary>
public sealed class KeyRingPathTests
{
    [Fact]
    public void Acik_env_degeri_kazanir_ve_kirpilir()
    {
        Assert.Equal("/var/lib/racar/dp-keys", KeyRingPath.Resolve("  /var/lib/racar/dp-keys  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Env_bos_ise_fallback_kullanilir(string? env)
    {
        // Boş/whitespace bir env, "dizin yok" demektir — düz `??` bunu yakalamaz, bu yüzden test ediliyor.
        var yol = KeyRingPath.Resolve(env);
        Assert.False(string.IsNullOrWhiteSpace(yol));
        Assert.True(Path.IsPathRooted(yol));
    }

    [Fact]
    public void Fallback_BUILD_CIKTISININ_ALTINDA_DEGIL()
    {
        // Asıl kilit bu: yol bin/Debug/... altına düşerse `dotnet clean` şifreli PII'yi yok eder.
        var yol = Path.GetFullPath(KeyRingPath.Resolve(null));
        var binDizini = Path.GetFullPath(AppContext.BaseDirectory);

        Assert.False(
            yol.StartsWith(binDizini, StringComparison.Ordinal),
            $"Key-ring build çıktısının altına düştü: {yol}");
    }

    [Fact]
    public void Fallback_cagrilar_arasinda_SABIT()
    {
        // Her açılışta değişen bir yol (ör. temp+rastgele) sessizce yeni key-ring üretir → aynı veri kaybı.
        Assert.Equal(KeyRingPath.Resolve(null), KeyRingPath.Resolve(null));
    }
}
