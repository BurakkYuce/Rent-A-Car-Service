using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ4-4.1 — Manuel provizyon yaşam döngüsü: Yok → Alindi → (Kapandi | IadeEdildi); başka geçiş
/// GÜRÜLTÜLÜ red. POS'suz kayıt (IPosService çağrılmaz); DEFTERE YAZMAZ — GenelToplam/Bakiye
/// değişmez (bilgi/iz). Whitelist: RentalUpdateInput'ta provizyon-durum alanları TİPTE YOK —
/// mega-form güncellemesi durumu değiştiremez. BAĞIMSIZ ORACLE: elle kurulan geçiş matrisi.
/// </summary>
[Collection("postgres")]
public sealed class ManuelProvizyonTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(3);

    private static async Task<Guid> KiraAsync(IServiceProvider sp, string plaka, decimal? provizyon)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Prov", Soyad = "M" });
        return await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 100m, Provizyon = provizyon });
    }

    [Fact]
    public async Task Gecis_matrisi_ve_defter_etkisizligi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        var id = await KiraAsync(sp, "34 PV 01", provizyon: 2000m);

        // Yok → Kapat: RED (yalnız Alındı kapatılabilir).
        await Assert.ThrowsAsync<ValidationException>(() => rentals.ClosePreAuthAsync(id));

        // Yok → Alındı: OK; tarih dolar.
        await rentals.TakePreAuthAsync(id);
        var c1 = (await rentals.GetAsync(id))!;
        Assert.Equal(PreAuthStatus.Alindi, c1.ProvizyonDurum);
        Assert.NotNull(c1.ProvizyonTarih);

        // Alındı → Alındı: RED (çift alma yok).
        await Assert.ThrowsAsync<ValidationException>(() => rentals.TakePreAuthAsync(id));

        // Alındı → Kapandı (kısmi çekim 1800): OK; kapama tarih+tutar dolar. DEFTER ETKİSİZ.
        await rentals.ClosePreAuthAsync(id, closingAmount: 1800m);
        var c2 = (await rentals.GetAsync(id))!;
        Assert.Equal(PreAuthStatus.Kapandi, c2.ProvizyonDurum);
        Assert.Equal(1800m, c2.ProvizyonKapamaTutar);
        Assert.NotNull(c2.ProvizyonKapamaTarih);
        Assert.Equal(300m, c2.GenelToplam);   // 3×100 — provizyon akışı paraya dokunmadı
        Assert.Equal(300m, c2.Bakiye);

        // Kapandı → Al / Kapat: RED (terminal).
        await Assert.ThrowsAsync<ValidationException>(() => rentals.TakePreAuthAsync(id));
        await Assert.ThrowsAsync<ValidationException>(() => rentals.ClosePreAuthAsync(id));
    }

    [Fact]
    public async Task Iade_yolu_ve_giris_guardlari()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();

        // Provizyon tutarı girilmemiş kirada alma reddi (neyin bloke edildiği belli olmalı).
        var tutarsiz = await KiraAsync(sp, "34 PV 02", provizyon: null);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => rentals.TakePreAuthAsync(tutarsiz));
        Assert.Contains("provizyon", ex.Message);

        // Alındı → İadeEdildi: kapama tutarı 0 (çekim yok — serbest bırakma).
        var id = await KiraAsync(sp, "34 PV 03", provizyon: 2000m);
        await rentals.TakePreAuthAsync(id);
        await rentals.ClosePreAuthAsync(id, refund: true);
        var c = (await rentals.GetAsync(id))!;
        Assert.Equal(PreAuthStatus.IadeEdildi, c.ProvizyonDurum);
        Assert.Equal(0m, c.ProvizyonKapamaTutar);

        // Negatif kapama tutarı red; kapatılmışta tekrar iade red.
        var id2 = await KiraAsync(sp, "34 PV 04", provizyon: 500m);
        await rentals.TakePreAuthAsync(id2);
        await Assert.ThrowsAsync<ValidationException>(() => rentals.ClosePreAuthAsync(id2, closingAmount: -1m));
        await rentals.ClosePreAuthAsync(id2);           // tutar boş → bloke tutarın tamamı (500)
        Assert.Equal(500m, (await rentals.GetAsync(id2))!.ProvizyonKapamaTutar);
        await Assert.ThrowsAsync<ValidationException>(() => rentals.ClosePreAuthAsync(id2, refund: true));
    }

    [Fact]
    public async Task Update_whitelist_durumu_degistiremez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        var id = await KiraAsync(sp, "34 PV 05", provizyon: 2000m);
        await rentals.TakePreAuthAsync(id);

        // Mega-form güncellemesi (RentalUpdateInput — provizyon-durum alanları TİPTE YOK) durumu
        // ve kapama izlerini DEĞİŞTİREMEZ; Provizyon (bilgi tutarı) güncellenebilir alan olarak kalır.
        await rentals.UpdateOpenAsync(id, new RentalUpdateInput { Aciklama = "not", Provizyon = 2500m });
        var c = (await rentals.GetAsync(id))!;
        Assert.Equal(PreAuthStatus.Alindi, c.ProvizyonDurum);
        Assert.Null(c.ProvizyonKapamaTarih);
        Assert.Equal(2500m, c.Provizyon);
    }
}
