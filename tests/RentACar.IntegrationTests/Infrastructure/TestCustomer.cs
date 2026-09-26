using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Customers;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests.Infrastructure;

/// <summary>
/// Gerçek (kiracıda VAR olan) test carisi. F4.4a adversarial MEDIUM-2'den beri tahsilat/ödeme/depozito cari varlığını
/// doğruluyor (rastgele Guid'e yetim defter kümesi yazılmaz) — para testleri uydurma cari kimliği kullanamaz.
/// </summary>
public static class TestCustomer
{
    /// <summary>Verilen kapsamda (yetkili rol) yeni bireysel cari.</summary>
    public static Task<Guid> NewAsync(IServiceProvider sp, string name = "Test")
        => sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = name, Soyad = "Cari" });

    /// <summary>Kiracıda Admin kapsamında yeni cari (çağıranın kapsamı cari açamayan bir rolse).</summary>
    public static async Task<Guid> NewAsync(TestHost host, Guid tenant, string name = "Test")
    {
        using var s = host.ScopeFor(tenant, role: UserRole.Admin);
        return await NewAsync(s.ServiceProvider, name);
    }
}
