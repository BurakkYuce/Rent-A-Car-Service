using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Baflar;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Application.Personnel;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Operatör rolünün AÇABİLMESİ gereken sayfaların veri yolu.
///
/// <para><b>Neden var:</b> `/baf` ekranı Operatör rolünde HTTP 500 veriyordu — `BafList.razor`
/// personel adlarını <c>PersonelService.ListAsync</c> ile çekiyordu ve o metot
/// <c>ManageUsers</c> istiyor. Seed kullanıcı Admin olduğu için hata gözden kaçmıştı; iki rollü
/// duman taraması yakaladı. AYNI hata daha önce dönüş formunda da olmuş ve
/// <c>PersonelService.ListForSelectAsync</c> tam bu yüzden eklenmişti (servisin kendi
/// yorumunda yazıyor) — yani bu, tekrarlayan bir hata sınıfı. Test onu kilitliyor.</para>
///
/// Bağımsız oracle: beklenti senaryodan kurulur ("operatör menüsünde görünen sayfa operatörde
/// patlamamalı"), servisin kendi mantığından değil.
/// </summary>
[Collection("postgres")]
public sealed class OperatorSayfaErisimTests(PostgresFixture fx)
{
    [Fact]
    public async Task Baf_ekraninin_veri_yolu_Operator_rolunde_PATLAMAZ()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);

        // Admin ile tohum: operatörün göreceği bir personel + araç olsun.
        using (var admin = host.ScopeFor(tenant))
        {
            await admin.ServiceProvider.GetRequiredService<PersonelService>().CreateAsync(
                new PersonelInput { Kod = "OP-001", Ad = "Op", Soyad = "Test" });
            await admin.ServiceProvider.GetRequiredService<VehicleService>().CreateAsync(
                new VehicleInput { Plaka = "34 OP 01" });
        }

        using var s = host.ScopeFor(tenant, role: UserRole.Operator);
        var sp = s.ServiceProvider;

        // BafList.OnInitializedAsync'in yaptığı çağrıların AYNISI — biri bile atarsa sayfa 500 olur.
        var kayitlar = await sp.GetRequiredService<BafService>().ListAsync();
        var personel = await sp.GetRequiredService<PersonelService>().ListForSelectAsync();
        var araclar = await sp.GetRequiredService<VehicleService>().ListAsync();
        var subeler = await sp.GetRequiredService<BranchService>().ListActiveAsync();

        Assert.NotNull(kayitlar);
        Assert.NotNull(subeler);
        Assert.Single(personel);                                  // operatör personel ADINI görebilir
        Assert.Contains(araclar, a => a.Plaka == "34OP01");
    }

    [Fact]
    public async Task Personel_TAM_listesi_operatore_KAPALI_kalir()
    {
        // Düzeltmenin yönü önemli: guard gevşetilmedi, sayfa doğru metoda geçirildi.
        // PII taşıyan tam liste operatöre HÂLÂ kapalı olmalı.
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(tenant, role: UserRole.Operator);

        await Assert.ThrowsAsync<ValidationException>(
            () => s.ServiceProvider.GetRequiredService<PersonelService>().ListAsync());
    }
}
