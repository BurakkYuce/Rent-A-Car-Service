using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// F5.1 bağımsız adversarial incelemesinin (PR #271) kalıcı çitleri. Beklenenler elle kurulmuş senaryodan:
/// "8 eşzamanlı kabul → 1 rezervasyon", "08:00 İstanbul = 05:00Z", "SubeB operatörü SubeA aracını görmez".
/// </summary>
public sealed partial class UiRezervasyonTests
{
    private async Task<T> OkuAsync<T>(Guid tenantId, Func<AppDbContext, Task<T>> q)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        return await q(db);
    }

    [Fact]
    public async Task H1_eszamanli_teklif_kabulu_tek_rezervasyon_acar()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var s = await GirisAsync(o, Kim.OperatorA);
        var g = RezGovde(o, arac, Yarin(4)); g.Remove("talepTuru"); g.Remove("projeAdi");
        var tid = (await Json(await Gonder(s, HttpMethod.Post, Teklif, g), HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var yanitlar = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Gonder(s, HttpMethod.Post, $"{Teklif}/{tid}/kabul")));
        var kodlar = yanitlar.Select(r => (int)r.StatusCode).ToList();
        Assert.True(kodlar.Count(k => k == 200) == 1 && kodlar.Count(k => k == 409) == 7, string.Join(",", kodlar));
        foreach (var r in yanitlar.Where(r => r.StatusCode == HttpStatusCode.Conflict))
            await ProblemBekle(r, HttpStatusCode.Conflict, "cakisma");

        var rezler = await OkuAsync(o.TenantId, db => db.Reservations.AsNoTracking().Where(x => x.VehicleId == arac).ToListAsync());
        var rez = Assert.Single(rezler);
        Assert.Equal(tid, rez.KaynakTeklifId);
        var teklif = await OkuAsync(o.TenantId, db => db.Quotations.AsNoTracking().FirstAsync(x => x.Id == tid));
        Assert.Equal(rez.Id, teklif.ReservationId);
        Assert.Equal(QuotationStatus.Kabul, teklif.Durum);
    }

    [Fact]
    public async Task H1_yapisal_cit_ayni_teklife_ikinci_rezervasyon_DB_tarafindan_reddedilir()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var teklifId = Guid.NewGuid();
        Reservation Yeni(int gun) => new()
        {
            ReservationNo = "T-" + Guid.NewGuid().ToString("N")[..8], MusteriId = o.MusteriId, VehicleId = arac,
            BasTar = Yarin(gun), BitTar = Yarin(gun + 1), Gun = 1, GunlukUcret = 100m, Tutar = 100m,
            Durum = ReservationStatus.Rezerv, KaynakTeklifId = teklifId,
        };
        await VeriYazAsync(o.TenantId, db => db.Reservations.Add(Yeni(10)));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => VeriYazAsync(o.TenantId, db => db.Reservations.Add(Yeni(20))));
        Assert.Contains("UX_Reservations_TenantId_KaynakTeklifId", ex.InnerException?.Message ?? "");
    }

    [Fact]
    public async Task L6_eszamanli_kiraya_cevir_tek_kira_ve_durum_ezilmez()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Admin);
        var arac0 = await AracAsync(o);
        var id0 = await RezAcAsync(s, o, arac0, Yarin(3));
        var kodlar = (await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Gonder(s, HttpMethod.Post, $"{Rez}/{id0}/kiraya-cevir"))))
            .Select(r => (int)r.StatusCode).ToList();
        Assert.Equal(1, await OkuAsync(o.TenantId, db => db.Rentals.CountAsync(x => x.ReservationId == id0)));
        Assert.True(kodlar.Count(k => k == 200) == 1 && !kodlar.Contains(500), string.Join(",", kodlar));

        var bozuk = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            var arac = await AracAsync(o);
            var id = await RezAcAsync(s, o, arac, Yarin(5 + i));
            var t = new[] { "onayla", "kiraya-cevir", "iptal" }.Select(e => Gonder(s, HttpMethod.Post, $"{Rez}/{id}/{e}")).ToArray();
            await Task.WhenAll(t);
            var r = await OkuAsync(o.TenantId, db => db.Reservations.AsNoTracking().FirstAsync(x => x.Id == id));
            var kira = await OkuAsync(o.TenantId, db => db.Rentals.CountAsync(x => x.ReservationId == id));
            // Oracle: kira açıldıysa rezervasyon KirayaCevrildi olmalı; açılmadıysa KirayaCevrildi olamaz.
            if ((kira > 0) != (r.Durum == ReservationStatus.KirayaCevrildi) || kira > 1 || t.Any(x => (int)x.Result.StatusCode == 500))
                bozuk.Add($"{i}:{r.Durum} kira={kira} [{string.Join(",", t.Select(x => (int)x.Result.StatusCode))}]");
        }
        Assert.True(bozuk.Count == 0, "Durum ezildi: " + string.Join(" | ", bozuk));
    }

    [Fact]
    public async Task M2_filo_tarih_sinirlari_ve_bozuk_eski_satir_listeyi_dusurmez()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var s = await GirisAsync(o, Kim.Admin);
        var uzak = new DateTimeOffset(9999, 6, 1, 0, 0, 0, TimeSpan.Zero);
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Filo, new { musteriId = o.MusteriId, vehicleId = arac, basTar = uzak, sureAy = 12, aylikUcret = 1000m }),
            HttpStatusCode.BadRequest, "dogrulama", "basTar");
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Filo, new { musteriId = o.MusteriId, vehicleId = arac, basTar = Yarin(400), sureAy = 12, aylikUcret = 1000m }),
            HttpStatusCode.BadRequest, "dogrulama", "basTar");
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Filo, new
        { musteriId = o.MusteriId, vehicleId = arac, sureAy = 12, aylikUcret = 1000m, sozlesmeTarihi = new DateTimeOffset(1, 1, 2, 0, 0, 0, TimeSpan.Zero) }),
            HttpStatusCode.BadRequest, "dogrulama", "sozlesmeTarihi");
        var id = (await Json(await Gonder(s, HttpMethod.Post, Filo, new { musteriId = o.MusteriId, vehicleId = arac, sureAy = 2, aylikUcret = 1000m }),
            HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var surum = (await Json(await s.C.GetAsync($"{Filo}/{id}"))).GetProperty("surum").GetString();
        await ProblemBekle(await Gonder(s, HttpMethod.Put, $"{Filo}/{id}/kunye", new { surum, imzaTarih = uzak }),
            HttpStatusCode.BadRequest, "dogrulama", "imzaTarih");

        // Sınır öncesi yazılmış bozuk satır (doğrudan DB): liste ve detay 200, toplam doğru (12 × 1.200 = 14.400).
        var bozuk = new FiloKiralama
        {
            No = "FK-BOZUK-" + Guid.NewGuid().ToString("N")[..4], MusteriId = o.MusteriId, VehicleId = arac, BasTar = uzak,
            SureAy = 12, AylikUcret = 1000m, KdvOrani = 0.20m, Currency = "TRY", Kur = 1m, Durum = FleetRentalStatus.Aktif,
        };
        await VeriYazAsync(o.TenantId, db => db.FiloKiralamalar.Add(bozuk));
        var liste = await Json(await s.C.GetAsync(Filo));
        var satir = liste.GetProperty("kayitlar").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == bozuk.Id);
        Assert.Equal(14400m, satir.GetProperty("genelToplam").GetDecimal());
        await Json(await s.C.GetAsync($"{Filo}/{bozuk.Id}"));
    }
}
