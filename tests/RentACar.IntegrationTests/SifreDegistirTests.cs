using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Users;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-83 — Kendi parolanı değiştirme (self-service).
///
/// <para>Bugüne kadar YALNIZ admin'in BAŞKASININ parolasını sıfırladığı akış vardı
/// (<c>ResetPasswordAsync</c>, ManageUsers guard'lı). Kullanıcının kendi parolasını eski parolasıyla
/// değiştirmesi hiç yoktu; <c>IPasswordHasher.Verify</c> koda eklenmiş ama hiçbir yerde
/// çağrılmıyordu.</para>
///
/// <para><b>Bu fazın tek gerçek riski yetki yükseltmedir:</b> uç herhangi role açık olduğu için
/// kimlik ASLA parametreden alınmamalı. Aşağıdaki <c>Imza_id_parametresi_TASIMAZ</c> testi bunu
/// yansımayla (derleme-zamanı sözleşmesi olarak) kilitler — birisi ileride kolaylık olsun diye
/// <c>id</c> parametresi eklerse test kırmızıya döner.</para>
///
/// Bağımsız oracle: parolalar testte elle belirlenir; doğrulama servisin dönüş değerine değil,
/// SAKLANAN HASH'e bakılarak yapılır (<c>IPasswordHasher.Verify</c> — login'in kullandığı aynı
/// ilkel). <c>LoginService</c> kullanılmadı: o tenant'ı KODA göre arıyor, testlerde tenant kodu yok.
/// </summary>
[Collection("postgres")]
public sealed class SifreDegistirTests(PostgresFixture fx)
{
    private const string First = "ilkSifre1";

    /// <summary>Saklanan hash verilen parolayı kabul ediyor mu — login'in yaptığı kontrolün aynısı.</summary>
    private static async Task<bool> IsPasswordValid(IServiceProvider sp, Guid uid, string password)
    {
        var u = await sp.GetRequiredService<IUserRepository>().FindAsync(uid);
        Assert.NotNull(u);
        return sp.GetRequiredService<IPasswordHasher>().Verify(u!.PasswordHash, password);
    }

    private static async Task<Guid> UserAsync(IServiceProvider sp, string name, string password = First)
        => await sp.GetRequiredService<UserService>().CreateAsync(new UserInput
        { UserName = name, DisplayName = name, Rol = UserRole.Operator, Password = password });

    [Fact]
    public async Task Kendi_parolasini_degistirebilir_ve_YENI_parolayla_giris_yapar()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);

        Guid uid;
        using (var admin = host.ScopeFor(tenant))   // kullanıcı oluşturmak ManageUsers ister
            uid = await UserAsync(admin.ServiceProvider, "operator1");

        // Operatör KENDİ oturumunda: admin yetkisi YOK.
        using (var s = host.ScopeFor(tenant, userId: uid, userName: "operator1", role: UserRole.Operator))
            Assert.True(await s.ServiceProvider.GetRequiredService<UserService>()
                .ChangeOwnPasswordAsync(First, "yeniSifre2"));

        // Doğrulama, login'in kullandığı AYNI ilkelle: saklanan hash yeni parolayı kabul etmeli,
        // eskisini ETMEMELİ. (LoginService tenant'ı KODA göre arıyor; testte tenant kodu yok.)
        using (var s = host.ScopeFor(tenant))
        {
            Assert.True(await IsPasswordValid(s.ServiceProvider, uid, "yeniSifre2"));
            Assert.False(await IsPasswordValid(s.ServiceProvider, uid, First));
        }
    }

    [Fact]
    public async Task Eski_parola_YANLISSA_reddedilir_ve_parola_DEGISMEZ()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        Guid uid;
        using (var admin = host.ScopeFor(tenant)) uid = await UserAsync(admin.ServiceProvider, "operator2");

        using (var s = host.ScopeFor(tenant, userId: uid, userName: "operator2", role: UserRole.Operator))
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(() => s.ServiceProvider
                .GetRequiredService<UserService>().ChangeOwnPasswordAsync("yanlisSifre", "yeniSifre2"));
            Assert.Contains("Mevcut parola hatalı", ex.Message);
        }

        // Parola gerçekten değişmemiş olmalı (red sessizce yazmamalı).
        using (var s = host.ScopeFor(tenant))
            Assert.True(await IsPasswordValid(s.ServiceProvider, uid, First));
    }

    [Fact]
    public async Task Kisa_parola_reddedilir()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        Guid uid;
        using (var admin = host.ScopeFor(tenant)) uid = await UserAsync(admin.ServiceProvider, "operator3");

        using var s = host.ScopeFor(tenant, userId: uid, userName: "operator3", role: UserRole.Operator);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.ServiceProvider
            .GetRequiredService<UserService>().ChangeOwnPasswordAsync(First, "kisa1"));
        Assert.Contains("en az 6 karakter", ex.Message);
    }

    [Fact]
    public async Task Oturumsuz_cagri_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid(), userId: null, role: UserRole.Operator);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.ServiceProvider
            .GetRequiredService<UserService>().ChangeOwnPasswordAsync(First, "yeniSifre2"));
        Assert.Contains("Oturum bulunamadı", ex.Message);
    }

    [Fact]
    public async Task BASKASININ_parolasini_degistiremez_yetki_yukseltme_yok()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        Guid a, b;
        using (var admin = host.ScopeFor(tenant))
        {
            a = await UserAsync(admin.ServiceProvider, "kullaniciA");
            b = await UserAsync(admin.ServiceProvider, "kullaniciB");
        }

        // A kendi oturumunda parolasını değiştirir. B'nin id'sini geçirmenin YOLU YOK
        // (metot id parametresi almıyor) — dolayısıyla B etkilenemez.
        using (var s = host.ScopeFor(tenant, userId: a, userName: "kullaniciA", role: UserRole.Operator))
            Assert.True(await s.ServiceProvider.GetRequiredService<UserService>()
                .ChangeOwnPasswordAsync(First, "aNinYeniSifresi"));

        using (var s = host.ScopeFor(tenant))
        {
            Assert.True(await IsPasswordValid(s.ServiceProvider, a, "aNinYeniSifresi"));
            Assert.True(await IsPasswordValid(s.ServiceProvider, b, First));            // B DOKUNULMAMIŞ
            Assert.False(await IsPasswordValid(s.ServiceProvider, b, "aNinYeniSifresi"));
        }
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Imza_id_parametresi_TASIMAZ()
    {
        // Sözleşme kilidi: birisi "kolaylık olsun" diye id parametresi eklerse, giriş yapmış
        // herhangi biri başkasının parolasını değiştirebilir hâle gelir. Test bunu engeller.
        var m = typeof(UserService).GetMethod(nameof(UserService.ChangeOwnPasswordAsync),
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(m);
        Assert.DoesNotContain(m!.GetParameters(),
            p => p.ParameterType == typeof(Guid) || p.ParameterType == typeof(Guid?));
    }
}
