using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// #283 M3-R regresyon çiti: vergi no kuralı (10 hane) yalnız oluşturmada ya da değer GERÇEKTEN değişince uygulanır.
/// Kural öncesinden kalan bireysel kayıt (11 haneli TC ya da 9 hane) düzenlenebilir kalmalı — aksi hâlde ilgisiz bir
/// alanı kaydetmek her seferinde 400 alıp kaydı dondururdu (SPA kartı ve Blazor formu).
/// </summary>
public sealed partial class UiCustomerApiTests
{
    [Theory]
    [InlineData(11)]
    [InlineData(9)]
    public async Task Legacy_individual_tax_number_stays_editable_until_changed(int digits)
    {
        var e = await SetupAsync();
        var legacy = digits == 11 ? RandomTc() : RandomTc()[..9];
        var c = new Customer { Tip = CariType.Bireysel, Ad = "Eski", Soyad = Marker(), VergiNo = legacy, Gsm2 = "05320000000" };
        await WriteAsync(e.TenantId, db => db.Customers.Add(c)); // kural öncesi kayıt: doğrulamadan geçmeden yazılır

        var admin = await LoginAsync(e, Who.Admin);
        var (card, raw) = await Json(await Send(admin, HttpMethod.Get, $"{Customers}/{c.Id}"));
        Assert.DoesNotContain(legacy, raw); // bireyselde yalnız maske

        // SPA: yalnız GSM değişen PUT (kart vergiNo'yu null döndürür → korunur) 200, vergi no DB'de aynı.
        var put = new Dictionary<string, object?>
        {
            ["tip"] = "Bireysel", ["ad"] = "Eski", ["soyad"] = c.Soyad, ["gsm2"] = "05321111111",
            ["surum"] = card.GetProperty("surum").GetString(),
        };
        var (ok, _) = await Json(await Send(admin, HttpMethod.Put, $"{Customers}/{c.Id}", put));
        Assert.Equal("05321111111", ok.GetProperty("gsm2").GetString());
        Assert.Equal(legacy, await ReadAsync(e.TenantId, db => db.Customers.Where(x => x.Id == c.Id).Select(x => x.VergiNo).SingleAsync()));

        // Başka geçersiz değere çevirmek 400 (kayıt değişmez).
        put["surum"] = ok.GetProperty("surum").GetString();
        put["vergiNo"] = "12345";
        await Problem(await Send(admin, HttpMethod.Put, $"{Customers}/{c.Id}", put), HttpStatusCode.BadRequest, "dogrulama", "vergiNo");
        put["vergiNo"] = RandomTc();
        await Problem(await Send(admin, HttpMethod.Put, $"{Customers}/{c.Id}", put), HttpStatusCode.BadRequest, "dogrulama", "vergiNo");
        Assert.Equal(legacy, await ReadAsync(e.TenantId, db => db.Customers.Where(x => x.Id == c.Id).Select(x => x.VergiNo).SingleAsync()));

        // Blazor servis yolu (sürümsüz UpdateAsync; form kayıtlı vergi no'yu aynen geri gönderir) → aynı kural.
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(e.TenantId);
        var service = scope.ServiceProvider.GetRequiredService<CustomerService>();
        CustomerInput Form(string? tax) => new()
        { Tip = CariType.Bireysel, Ad = "Eski", Soyad = c.Soyad, VergiNo = tax, Gsm2 = "05322222222" };
        Assert.True(await service.UpdateAsync(c.Id, Form(legacy)));
        Assert.Equal(legacy, await ReadAsync(e.TenantId, db => db.Customers.Where(x => x.Id == c.Id).Select(x => x.VergiNo).SingleAsync()));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(c.Id, Form("12345")));
        Assert.Equal(legacy, await ReadAsync(e.TenantId, db => db.Customers.Where(x => x.Id == c.Id).Select(x => x.VergiNo).SingleAsync()));
    }
}
