using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using RentACar.Application.Common;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.IntegrationTests;

/// <summary>
/// F1.4 — <c>Idempotency-Key</c> → <c>IslemAnahtari</c> türetmesi (saf; DB yok).
///
/// <para><b>Bağımsız oracle:</b> beklenen UUID'ler üretim kodundan DEĞİL, geliştirme sırasında Python
/// <c>uuid.uuid5(uuid.UUID('2adf1c10-4f5c-4c27-bad3-3294d841ee0c'), "{tenant}|{user}|{başlık}")</c> ile
/// ayrıca hesaplanıp buraya elle yazıldı. RFC 4122 çekirdeği ayrıca dokümandaki standart örnekle
/// (<c>uuid5(NAMESPACE_DNS, "python.org")</c>) kilitli.</para>
/// </summary>
public sealed class IslemAnahtariTureticiTests
{
    private static readonly Guid T1 = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid U1 = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid U2 = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid T2 = new("44444444-4444-4444-4444-444444444444");
    private const string Baslik = "3f0e5a8c-9b1d-4c2e-8f7a-6d5c4b3a2910";

    [Fact]
    public void Rfc4122_ornegi_python_org_dns_ad_alani()
    {
        // uuid.uuid5(uuid.NAMESPACE_DNS, 'python.org') — Python dokümanındaki örnek.
        var dns = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
        Assert.Equal(new Guid("886313e1-3b8a-5372-9b90-0c9aee199e5d"), OperationKeyDeriver.UuidV5(dns, "python.org"));
    }

    [Fact]
    public void Ad_alani_sabit()
        => Assert.Equal(new Guid("2adf1c10-4f5c-4c27-bad3-3294d841ee0c"), OperationKeyDeriver.NameField);

    [Theory]
    [InlineData(Baslik, "a4068cc6-1a4f-574a-a145-937a9b14eaac")]
    [InlineData("tahsilat-form-0001", "d8b25d16-e0a8-5c78-abf3-15a3ec210de4")]
    public void Test_vektorleri_python_ile_ayrica_hesaplandi(string baslik, string beklenen)
        => Assert.Equal(new Guid(beklenen), OperationKeyDeriver.Derive(T1, U1, baslik));

    [Fact]
    public void Ayni_baslik_farkli_kullanici_ya_da_kiraci_ASLA_ayni_anahtar_degil()
    {
        var k = OperationKeyDeriver.Derive(T1, U1, Baslik);
        Assert.Equal(new Guid("a9fdeecb-62a1-5b20-b5f7-b24d13338683"), OperationKeyDeriver.Derive(T1, U2, Baslik));
        Assert.Equal(new Guid("e63a9b3f-adb6-59b2-858f-2c8bd717ed75"), OperationKeyDeriver.Derive(T2, U1, Baslik));
        Assert.NotEqual(k, OperationKeyDeriver.Derive(T1, U2, Baslik));
        Assert.NotEqual(k, OperationKeyDeriver.Derive(T2, U1, Baslik));
        // Aynı üçlü → aynı anahtar (çift gönderim yakalanır).
        Assert.Equal(k, OperationKeyDeriver.Derive(T1, U1, Baslik));
    }

    [Fact]
    public void Ham_istemci_degeri_anahtar_olmaz()
    {
        // İstemci bir GUID yollasa bile anahtar o GUID DEĞİL (PK'ye doğrudan yazılmaz).
        Assert.NotEqual(new Guid(Baslik), OperationKeyDeriver.Derive(T1, U1, Baslik));
    }

    [Fact]
    public void Surum_5_ve_RFC_varyanti()
    {
        var s = OperationKeyDeriver.Derive(T1, U1, Baslik).ToString("D");
        Assert.Equal('5', s[14]);                 // xxxxxxxx-xxxx-5xxx
        Assert.Contains(s[19], "89ab");           // varyant 10xx
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kisa-anahtar")]                   // 12 karakter < 16
    [InlineData("içinde boşluk var olan anahtar")]  // boşluk + ASCII-dışı
    [InlineData("Çift-Gönderim-ü-anahtar")]         // ASCII-dışı
    [InlineData("tab\ticeren-anahtar-00")]          // kontrol karakteri
    public void Gecersiz_baslik_reddedilir(string? deger)
        => Assert.False(OperationKeyDeriver.IsValid(deger));

    [Fact]
    public void Uzunluk_sinirlari()
    {
        Assert.True(OperationKeyDeriver.IsValid(new string('a', 16)));
        Assert.False(OperationKeyDeriver.IsValid(new string('a', 15)));
        Assert.True(OperationKeyDeriver.IsValid(new string('a', 128)));
        Assert.False(OperationKeyDeriver.IsValid(new string('a', 129)));
    }

    [Fact]
    public void Turet_gecersiz_girdide_alanli_dogrulama_hatasi()
    {
        var ex = Assert.Throws<ValidationException>(() => OperationKeyDeriver.Derive(T1, U1, "kisa"));
        Assert.Equal("Idempotency-Key", ex.Alan);
        Assert.Throws<ArgumentException>(() => OperationKeyDeriver.Derive(Guid.Empty, U1, Baslik));
        Assert.Throws<ArgumentException>(() => OperationKeyDeriver.Derive(T1, Guid.Empty, Baslik));
    }

    [Fact]
    public void Oncelik_deterministik_anahtar_basligi_ezer()
    {
        var det = new Guid("aaaaaaaa-0000-0000-0000-000000000001");
        var hdr = new Guid("bbbbbbbb-0000-0000-0000-000000000002");
        Assert.Equal(det, OperationKeyDeriver.Select(det, hdr));
        Assert.Equal(det, OperationKeyDeriver.Select(det, null));
        Assert.Equal(hdr, OperationKeyDeriver.Select(null, hdr));
        Assert.Equal(hdr, OperationKeyDeriver.Select(Guid.Empty, hdr));  // boş Guid "yok" sayılır
        Assert.Null(OperationKeyDeriver.Select(null, null));
        Assert.Null(OperationKeyDeriver.Select(Guid.Empty, Guid.Empty));
    }

    // ---------------- Web erişimcisi (IdempotencyBasligi) ----------------

    private static DefaultHttpContext Ctx(Guid? tenant, Guid? user, params string[] basliklar)
    {
        var ctx = new DefaultHttpContext();
        var claims = new List<Claim>();
        if (tenant is { } t) claims.Add(new Claim(IdentityClaims.TenantId, t.ToString()));
        if (user is { } u) claims.Add(new Claim(IdentityClaims.UserId, u.ToString()));
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, claims.Count > 0 ? "test" : null));
        if (basliklar.Length > 0) ctx.Request.Headers[IdempotencyBasligi.Ad] = basliklar;
        return ctx;
    }

    [Fact]
    public void Baslik_yoksa_null_bugunku_anahtarsiz_davranis()
    {
        Assert.Null(IdempotencyBasligi.Anahtar(Ctx(T1, U1)));
        Assert.Null(IdempotencyBasligi.BasliktanTuret(Ctx(null, null)));
    }

    [Fact]
    public void Baslik_claimlerden_kiraci_ve_kullaniciyla_turetilir()
        => Assert.Equal(new Guid("a4068cc6-1a4f-574a-a145-937a9b14eaac"),
            IdempotencyBasligi.Anahtar(Ctx(T1, U1, Baslik)));

    [Fact]
    public void Deterministik_anahtar_varsa_baslik_ezemez()
    {
        var tahsilatAnahtari = new Guid("cccccccc-0000-0000-0000-000000000003");
        Assert.Equal(tahsilatAnahtari, IdempotencyBasligi.Anahtar(Ctx(T1, U1, Baslik), tahsilatAnahtari));
    }

    [Fact]
    public void Bicimsiz_ya_da_cok_degerli_baslik_400()
    {
        var ex = Assert.Throws<ValidationException>(() => IdempotencyBasligi.Anahtar(Ctx(T1, U1, "kisa")));
        Assert.Equal(IdempotencyBasligi.Ad, ex.Alan);
        Assert.Throws<ValidationException>(() => IdempotencyBasligi.Anahtar(Ctx(T1, U1, "")));
        Assert.Throws<ValidationException>(() => IdempotencyBasligi.Anahtar(Ctx(T1, U1, Baslik, Baslik + "-2")));
        // Deterministik anahtar olsa bile bozuk başlık sessizce yutulmaz.
        Assert.Throws<ValidationException>(() => IdempotencyBasligi.Anahtar(Ctx(T1, U1, "kisa"), Guid.NewGuid()));
    }

    [Fact]
    public void Zorunlu_anahtar_basliksiz_ve_deterministiksiz_istekte_400()
    {
        var ex = Assert.Throws<ValidationException>(() => IdempotencyBasligi.ZorunluAnahtar(Ctx(T1, U1)));
        Assert.Equal(IdempotencyBasligi.Ad, ex.Alan);
        Assert.Throws<ValidationException>(() => IdempotencyBasligi.ZorunluAnahtar(Ctx(T1, U1), Guid.Empty));
        var det = new Guid("dddddddd-0000-0000-0000-000000000004");
        Assert.Equal(det, IdempotencyBasligi.ZorunluAnahtar(Ctx(T1, U1), det));
        Assert.Equal(new Guid("a4068cc6-1a4f-574a-a145-937a9b14eaac"), IdempotencyBasligi.ZorunluAnahtar(Ctx(T1, U1, Baslik)));
    }

    [Fact]
    public void Kimliksiz_istekte_turetme_yapilmaz()
        => Assert.Throws<InvalidOperationException>(() => IdempotencyBasligi.Anahtar(Ctx(null, null, Baslik)));
}
