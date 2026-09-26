using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-85 — Rezervasyon listesine taşınan kolonların ARKASINDAKİ VERİ gerçekten doluyor mu.
///
/// <para>Bu faz D3 ("veri var, görünmüyor") sınıfının örneği: hiçbir domain/servis değişikliği yok,
/// yalnız `ReservationList.razor` var olan alanları grid'e taşıyor. Dolayısıyla asıl risk, alanın
/// aslında hiç dolmadığı hâlde kolon eklenmesi — kolon eklenir, hep "—" gösterir, kimse fark etmez.
/// Bu test o riski kapatır: Kaynak / CikisOfisi / Customer.CepTel create→list turunda korunuyor mu.</para>
///
/// Bağımsız oracle: değerler testte elle verilir ("Web", "İstanbul Merkez Ofis", "05551112233") ve
/// aynen geri okunur; sayfanın kendi render mantığından türetilmez.
/// </summary>
[Collection("postgres")]
public sealed class RezervasyonKolonVeriTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(5).AddHours(9);

    [Fact]
    public async Task Kaynak_cikis_ofisi_ve_cep_tel_create_list_turunda_KORUNUR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;

        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Kolon", Soyad = "Test", CepTel = "05551112233" });
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KL 01" });

        var res = sp.GetRequiredService<ReservationService>();
        await res.CreateAsync(new BookingInput
        {
            MusteriId = m, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(3),
            GunlukUcret = 1000m, Kaynak = "Web", CikisOfisi = "İstanbul Merkez Ofis",
        });

        var record = Assert.Single(await res.ListAsync());
        Assert.Equal("Web", record.Kaynak);
        Assert.Equal("İstanbul Merkez Ofis", record.CikisOfisi);
        // Teslim kolonu ayrı gösteriliyor → bitiş tarihi de kayıtta olmalı.
        Assert.Equal(Start.AddDays(3), record.BitTar);

        var account = Assert.Single(await sp.GetRequiredService<CustomerService>().ListAsync(),
            c => c.Id == m);
        Assert.Equal("05551112233", account.CepTel);
    }

    [Fact]
    public async Task Kaynagi_farkli_rezervasyonlar_AYRI_AYRI_saklanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Kanal", Soyad = "Test" });
        var veh = sp.GetRequiredService<VehicleService>();
        var res = sp.GetRequiredService<ReservationService>();

        foreach (var (plate, source) in new[] { ("34 KN 01", "Web"), ("34 KN 02", "Acente"), ("34 KN 03", (string?)null) })
        {
            var v = await veh.CreateAsync(new VehicleInput { Plaka = plate });
            await res.CreateAsync(new BookingInput
            {
                MusteriId = m, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(2),
                GunlukUcret = 500m, Kaynak = source,
            });
        }

        var all = await res.ListAsync();
        Assert.Equal(3, all.Count);
        // Sayfadaki filtrenin uyguladığı predicate ile AYNI karşılaştırma (OrdinalIgnoreCase).
        Assert.Single(all, r => string.Equals(r.Kaynak, "Web", StringComparison.OrdinalIgnoreCase));
        Assert.Single(all, r => string.Equals(r.Kaynak, "Acente", StringComparison.OrdinalIgnoreCase));
        Assert.Single(all, r => r.Kaynak is null);
    }

    [Fact]
    public void Sayfa_yeni_kolonlari_ve_filtreyi_ICERIR()
    {
        // Kaynak çiti: kolonlar/filtre sayfadan sessizce düşerse test kırmızıya döner.
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        var page = File.ReadAllText(Path.Combine(d!.FullName,
            "src/RentACar.Web/Components/Pages/Bookings/ReservationList.razor"));

        foreach (var expected in new[] { "<th>Cep Tel</th>", "<th>Teslim</th>", "<th>Alış Şube</th>", "<th>Kaynak</th>" })
            Assert.Contains(expected, page, StringComparison.Ordinal);
        Assert.Contains("name=\"kaynak\"", page, StringComparison.Ordinal);

        // Başlık ve gövde hücre sayısı eşit olmalı (colspan dahil) — kolon eklerken en sık hata bu.
        var startCount = Regex.Matches(page.Split("<tbody>")[0], "<th>").Count;
        Assert.Equal(startCount, Regex.Matches(page, @"colspan=""(\d+)""") is { Count: > 0 } m
            ? int.Parse(m[0].Groups[1].Value) : -1);
    }
}
