using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.BelgeSablon;
using RentACar.Application.Common;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Marka-özel belge şablonu master — bağımsız oracle. CRUD + (Tür,Ad) benzersizlik + tür-başına tek
/// varsayılan + yetki (ManageUsers) + tenant izolasyon (racar_app) + çözümleyici fallback + token.
/// Beklenen değerler senaryodan kurulur, koddan türetilmez.
/// </summary>
[Collection("postgres")]
public sealed class BelgeSablonTests(PostgresFixture fx)
{
    private static BelgeSablonInput Yeni(BelgeTuru turu, string ad, bool varsayilan = false) => new()
    {
        BelgeTuru = turu, Ad = ad, VarsayilanMi = varsayilan, Aktif = true
    };

    [Fact]
    public async Task Create_roundtrips_and_trims_name()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BelgeSablonService>();

        var id = await svc.CreateAsync(new BelgeSablonInput
        {
            BelgeTuru = BelgeTuru.KiraSozlesmesi, Ad = "  Kurumsal  ", VarsayilanMi = true, Aktif = true,
            BelgeBasligi = "KURUMSAL SÖZLEŞME", HukukiMetinSol = "Sol metin", HukukiMetinSag = "Sağ metin",
            EkKosullarVarsayilan = "Standart ek koşul", AltBilgi = "{FirmaMarka} — {BelgeNo}"
        });

        var r = (await svc.ListAsync()).Single(x => x.Id == id);
        Assert.Equal("Kurumsal", r.Ad);                 // trim
        Assert.Equal(BelgeTuru.KiraSozlesmesi, r.BelgeTuru);
        Assert.True(r.VarsayilanMi);
        Assert.Equal("KURUMSAL SÖZLEŞME", r.BelgeBasligi);
        Assert.Equal("Sol metin", r.HukukiMetinSol);
        Assert.Equal("Sağ metin", r.HukukiMetinSag);
        Assert.Equal("Standart ek koşul", r.EkKosullarVarsayilan);
        Assert.Equal("{FirmaMarka} — {BelgeNo}", r.AltBilgi);
    }

    [Fact]
    public async Task Empty_sections_stored_as_null()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BelgeSablonService>();

        var id = await svc.CreateAsync(new BelgeSablonInput
        {
            BelgeTuru = BelgeTuru.Fatura, Ad = "Sade", BelgeBasligi = "   ", HukukiMetinSol = ""
        });
        var r = (await svc.ListAsync()).Single(x => x.Id == id);
        Assert.Null(r.BelgeBasligi);   // whitespace → null ("bu bölümde varsayılan bas")
        Assert.Null(r.HukukiMetinSol);
    }

    [Fact]
    public async Task Duplicate_name_in_same_type_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BelgeSablonService>();

        await svc.CreateAsync(Yeni(BelgeTuru.KiraSozlesmesi, "Kurumsal"));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(Yeni(BelgeTuru.KiraSozlesmesi, "Kurumsal")));
        // Aynı ad FARKLI türde serbest.
        await svc.CreateAsync(Yeni(BelgeTuru.Fatura, "Kurumsal"));
        Assert.Equal(2, (await svc.ListAsync()).Count);
    }

    [Fact]
    public async Task Validation_rejects_empty_name()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BelgeSablonService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Yeni(BelgeTuru.Makbuz, "   ")));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new BelgeSablonInput { BelgeTuru = BelgeTuru.Makbuz, Ad = new string('x', 129) }));
    }

    [Fact]
    public async Task Only_one_default_per_type()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BelgeSablonService>();

        var a = await svc.CreateAsync(Yeni(BelgeTuru.KiraSozlesmesi, "A", varsayilan: true));
        var b = await svc.CreateAsync(Yeni(BelgeTuru.KiraSozlesmesi, "B", varsayilan: true)); // B varsayılan olunca A düşer

        var liste = await svc.ListByTuruAsync(BelgeTuru.KiraSozlesmesi);
        Assert.False(liste.Single(x => x.Id == a).VarsayilanMi);
        Assert.True(liste.Single(x => x.Id == b).VarsayilanMi);
        Assert.Single(liste, x => x.VarsayilanMi);
    }

    [Fact]
    public async Task NonManageUsers_user_cannot_manage()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "op", UserRole.Operator);
        var svc = scope.ServiceProvider.GetRequiredService<BelgeSablonService>();
        await Assert.ThrowsAsync<YetkiYokException>(
            () => svc.CreateAsync(Yeni(BelgeTuru.KiraSozlesmesi, "Yetkisiz")));
    }

    [Fact]
    public async Task Templates_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<BelgeSablonService>()
                .CreateAsync(Yeni(BelgeTuru.KiraSozlesmesi, "T1"));

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<BelgeSablonService>();
        Assert.Empty(await svc2.ListAsync());                    // t2 t1'inkini görmez (RLS/racar_app)
        await svc2.CreateAsync(Yeni(BelgeTuru.KiraSozlesmesi, "T1")); // aynı ad farklı tenant'ta serbest
        Assert.Single(await svc2.ListAsync());
    }

    [Fact]
    public async Task Resolver_uses_selected_then_default_then_empty()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BelgeSablonService>();
        var cozucu = scope.ServiceProvider.GetRequiredService<BelgeSablonCozumleyici>();

        // Şablon hiç yokken → boş (renderer koddaki sabiti basar).
        var bos = await cozucu.KiraAsync(null);
        Assert.Null(bos.HukukiMetinSol);
        Assert.Null(bos.Baslik);

        // Varsayılan şablon → seçim yapılmadan çözülür.
        var vId = await svc.CreateAsync(new BelgeSablonInput
        { BelgeTuru = BelgeTuru.KiraSozlesmesi, Ad = "Vars", VarsayilanMi = true, HukukiMetinSol = "VARS-SOL" });
        Assert.Equal("VARS-SOL", (await cozucu.KiraAsync(null)).HukukiMetinSol);

        // Açık seçim → varsayılanı ezer.
        var secId = await svc.CreateAsync(new BelgeSablonInput
        { BelgeTuru = BelgeTuru.KiraSozlesmesi, Ad = "Secili", BelgeBasligi = "SECILI-BASLIK" });
        var secili = await cozucu.KiraAsync(secId);
        Assert.Equal("SECILI-BASLIK", secili.Baslik);
        Assert.Null(secili.HukukiMetinSol); // seçili şablonda bu bölüm boş → renderer sabiti basar

        // Geçersiz/bulunamayan seçim → varsayılana düşer.
        Assert.Equal("VARS-SOL", (await cozucu.KiraAsync(Guid.NewGuid())).HukukiMetinSol);

        // Fatura varsayılanı kira çözümüne SIZMAZ (tür ayrımı).
        await svc.CreateAsync(new BelgeSablonInput
        { BelgeTuru = BelgeTuru.Fatura, Ad = "F", VarsayilanMi = true, AltBilgi = "FT-ALT" });
        Assert.Equal("FT-ALT", (await cozucu.VarsayilanAsync(BelgeTuru.Fatura)).AltBilgi);
        Assert.Null((await cozucu.KiraAsync(null)).AltBilgi); // kira varsayılanında AltBilgi yok
    }

    [Fact]
    public void Token_substitutes_placeholders()
    {
        var sozluk = new Dictionary<string, string?>
        {
            ["FirmaMarka"] = "YÜCE RENT", ["BelgeNo"] = "RZ-000123", ["Bos"] = null
        };
        Assert.Equal("YÜCE RENT — RZ-000123",
            SablonToken.Uygula("{FirmaMarka} — {BelgeNo}", sozluk));
        Assert.Equal("A", SablonToken.Uygula("A{Bos}", sozluk)); // null değer → yer-tutucu silinir
        Assert.Null(SablonToken.Uygula(null, sozluk));
    }
}
