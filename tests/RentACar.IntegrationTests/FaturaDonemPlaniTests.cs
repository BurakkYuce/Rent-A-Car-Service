using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.FaturaDonemleri;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ4-4.2-B1 — Fatura dönem planı (parasız). BAĞIMSIZ ORACLE (elle): 15 Oca 2027 + 90 gün
/// (15 Nis) → 3 ay-çıpalı dönem [15O-15Ş)[15Ş-15M)[15M-15N); 31 Oca çıpası: [31O-28Şub)[28Şub-31Mar)
/// (AddMonths origin'den — çıpa Mart'ta 31'e döner); ProRataAccrual [30,30,30] gün + 100 TL →
/// 33.33+33.33+33.34 (kalan-yöntemi; Σ == tutar KURUŞ-BİREBİR — invaryant döngüsü). Uygunluk:
/// "Uzun Kiralama"/"Aylık" VEYA Gun>=28 (kısa günlük kirada plan YOK). Uzatma planı büyütür;
/// Kesildi/Atlandi satırlar yeniden üretilmez (B2'de kesim; burada koruma davranışı repo düzeyinde).
/// </summary>
[Collection("postgres")]
public sealed class FaturaDonemPlaniTests(PostgresFixture fx)
{
    private static async Task<Guid> RentalAsync(IServiceProvider sp, string plate,
        DateTimeOffset start, DateTimeOffset bit, string? rentalType = null)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plate });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Donem", Soyad = "M" });
        return await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = m, VehicleId = v, BasTar = start, BitTar = bit, GunlukUcret = 100m, KiralamaTuru = rentalType });
    }

    [Fact]
    public void Pro_rata_ve_ay_cipasi_saf_oracle()
    {
        // 100 / [30,30,30] → 33.33 + 33.33 + 33.34 (son dönem kalan-yöntemi; elle).
        var t = InvoicePeriodPlanService.ProRataAccrual(100m, [30, 30, 30]);
        Assert.Equal([33.33m, 33.33m, 33.34m], t);
        Assert.Equal(100m, t.Sum());

        // İnvaryant döngüsü: keyfi tutar/gün kombinasyonlarında Σ == tutar KURUŞ-BİREBİR.
        foreach (var (amount, gunler) in new (decimal, int[])[]
                 { (30000m, [31, 28, 31]), (999.99m, [7, 30, 1]), (0.03m, [10, 10, 10]), (12345.67m, [29, 31, 30, 2]) })
        {
            var d = InvoicePeriodPlanService.ProRataAccrual(amount, gunler);
            Assert.Equal(amount, d.Sum());
            Assert.Equal(gunler.Length, d.Count);
        }

        // Ay-sonu çıpası: 31 Oca 2027 → [31O, 28Şub) [28Şub, 31Mar) — çıpa Mart'ta 31'e DÖNER (elle).
        var start = new DateTimeOffset(2027, 1, 31, 10, 0, 0, TimeSpan.Zero);
        var ranges = InvoicePeriodPlanService.PeriodRanges(start, start.AddMonths(2));
        Assert.Equal(2, ranges.Count);
        Assert.Equal(new DateTimeOffset(2027, 2, 28, 10, 0, 0, TimeSpan.Zero), ranges[0].Bit);
        Assert.Equal(new DateTimeOffset(2027, 3, 31, 10, 0, 0, TimeSpan.Zero), ranges[1].Bit);
    }

    [Fact]
    public async Task Uygun_kirada_plan_kurulur_kisa_kirada_kurulmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var start = new DateTimeOffset(2027, 1, 15, 10, 0, 0, TimeSpan.Zero);

        // 90 gün (Gun>=28 uygunluk) → 3 dönem: 15 Oca / 15 Şub / 15 Mar çıpaları (elle).
        var id = await RentalAsync(sp, "34 FD 01", start, start.AddDays(90));
        var repo = sp.GetRequiredService<IInvoicePeriodRepository>();
        var plan = await repo.ListForRentalAsync(id);
        Assert.Equal(3, plan.Count);
        Assert.All(plan, d => Assert.Equal(InvoicePeriodStatus.Planlandi, d.Durum));
        Assert.Equal(new DateTimeOffset(2027, 2, 15, 10, 0, 0, TimeSpan.Zero), plan[0].DonemBit);
        Assert.Equal(new DateTimeOffset(2027, 3, 15, 10, 0, 0, TimeSpan.Zero), plan[1].DonemBit);
        Assert.Equal(start.AddDays(90), plan[2].DonemBit);   // son dönem BitTar'da biter

        // Önizleme: Tutar 9000 (90×100) gün-bazlı pro-rata; Σ == Tutar (elle: 31/28/31 gün →
        // 3100.00 + 2800.00 + 3100.00). [15O-15Ş)=31g, [15Ş-15M)=28g, [15M-15N)=31g.
        var preview = await sp.GetRequiredService<InvoicePeriodPlanService>().PreviewAsync(id);
        Assert.Equal(3100.00m, preview[0].Tahakkuk);
        Assert.Equal(2800.00m, preview[1].Tahakkuk);
        Assert.Equal(3100.00m, preview[2].Tahakkuk);
        Assert.Equal(9000m, preview.Sum(o => o.Tahakkuk));

        // KISA günlük kira (3 gün, tür yok): plan YOK.
        var brief = await RentalAsync(sp, "34 FD 02", start, start.AddDays(3));
        Assert.Empty(await repo.ListForRentalAsync(brief));

        // "Aylık" türü 28 gün altında da uygun: 20 günlük ama Aylık → 1 dönem.
        var monthly = await RentalAsync(sp, "34 FD 03", start, start.AddDays(20), rentalType: "Aylık");
        Assert.Single(await repo.ListForRentalAsync(monthly));
    }

    [Fact]
    public async Task Uzatma_plani_buyutur_kesildi_korunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        // Geçmişe-açık kira penceresi: şimdiden 40 gün önce başlar (uzatma bitişi ileri iter).
        var start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-40);
        var id = await RentalAsync(sp, "34 FD 04", start, start.AddDays(35));
        var repo = sp.GetRequiredService<IInvoicePeriodRepository>();
        Assert.Equal(2, (await repo.ListForRentalAsync(id)).Count);   // 35 gün → 2 dönem

        // 1. dönem KESİLDİ işaretlenir (B2 kesimini simüle eden repo yazımı) — uzatmada KORUNMALI.
        var plans = await repo.ListForRentalAsync(id);
        var issuedId = plans[0].Id;
        await repo.ReplacePlannedAsync(id, [new Domain.Entities.FaturaDonemi
        {
            Id = issuedId, RentalId = id, DonemSira = 1,
            DonemBas = plans[0].DonemBas, DonemBit = plans[0].DonemBit,
            Durum = InvoicePeriodStatus.Kesildi, KesilenTutar = 3100m
        }, new Domain.Entities.FaturaDonemi
        {
            RentalId = id, DonemSira = 2, DonemBas = plans[1].DonemBas, DonemBit = plans[1].DonemBit
        }]);

        // Uzatma: +30 gün → 65 gün → 3 dönem; sıra-1 Kesildi satırı DOKUNULMADAN kalır.
        await sp.GetRequiredService<RentalService>().ExtendAsync(id, start.AddDays(65));
        var newItem = await repo.ListForRentalAsync(id);
        Assert.Equal(3, newItem.Count);
        Assert.Equal(InvoicePeriodStatus.Kesildi, newItem[0].Durum);
        Assert.Equal(issuedId, newItem[0].Id);                          // aynı satır — yeniden üretilmedi
        Assert.Equal(3100m, newItem[0].KesilenTutar);
        Assert.All(newItem.Skip(1), d => Assert.Equal(InvoicePeriodStatus.Planlandi, d.Durum));
        Assert.Equal(start.AddDays(65), newItem[2].DonemBit);
    }
}
