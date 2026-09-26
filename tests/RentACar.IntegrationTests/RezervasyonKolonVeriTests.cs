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
    private static readonly DateTimeOffset Bas =
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

        var rez = sp.GetRequiredService<ReservationService>();
        await rez.CreateAsync(new BookingInput
        {
            MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3),
            GunlukUcret = 1000m, Kaynak = "Web", CikisOfisi = "İstanbul Merkez Ofis",
        });

        var kayit = Assert.Single(await rez.ListAsync());
        Assert.Equal("Web", kayit.Kaynak);
        Assert.Equal("İstanbul Merkez Ofis", kayit.CikisOfisi);
        // Teslim kolonu ayrı gösteriliyor → bitiş tarihi de kayıtta olmalı.
        Assert.Equal(Bas.AddDays(3), kayit.BitTar);

        var cari = Assert.Single(await sp.GetRequiredService<CustomerService>().ListAsync(),
            c => c.Id == m);
        Assert.Equal("05551112233", cari.CepTel);
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
        var rez = sp.GetRequiredService<ReservationService>();

        foreach (var (plaka, kaynak) in new[] { ("34 KN 01", "Web"), ("34 KN 02", "Acente"), ("34 KN 03", (string?)null) })
        {
            var v = await veh.CreateAsync(new VehicleInput { Plaka = plaka });
            await rez.CreateAsync(new BookingInput
            {
                MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(2),
                GunlukUcret = 500m, Kaynak = kaynak,
            });
        }

        var hepsi = await rez.ListAsync();
        Assert.Equal(3, hepsi.Count);
        // Sayfadaki filtrenin uyguladığı predicate ile AYNI karşılaştırma (OrdinalIgnoreCase).
        Assert.Single(hepsi, r => string.Equals(r.Kaynak, "Web", StringComparison.OrdinalIgnoreCase));
        Assert.Single(hepsi, r => string.Equals(r.Kaynak, "Acente", StringComparison.OrdinalIgnoreCase));
        Assert.Single(hepsi, r => r.Kaynak is null);
    }

    [Fact]
    public void Sayfa_yeni_kolonlari_ve_filtreyi_ICERIR()
    {
        // Kaynak çiti: kolonlar/filtre sayfadan sessizce düşerse test kırmızıya döner.
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        var sayfa = File.ReadAllText(Path.Combine(d!.FullName,
            "src/RentACar.Web/Components/Pages/Bookings/ReservationList.razor"));

        foreach (var beklenen in new[] { "<th>Cep Tel</th>", "<th>Teslim</th>", "<th>Alış Şube</th>", "<th>Kaynak</th>" })
            Assert.Contains(beklenen, sayfa, StringComparison.Ordinal);
        Assert.Contains("name=\"kaynak\"", sayfa, StringComparison.Ordinal);

        // Başlık ve gövde hücre sayısı eşit olmalı (colspan dahil) — kolon eklerken en sık hata bu.
        var basSayisi = Regex.Matches(sayfa.Split("<tbody>")[0], "<th>").Count;
        Assert.Equal(basSayisi, Regex.Matches(sayfa, @"colspan=""(\d+)""") is { Count: > 0 } m
            ? int.Parse(m[0].Groups[1].Value) : -1);
    }
}
