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
    private async Task<T> ReadAsync<T>(Guid tenantId, Func<AppDbContext, Task<T>> q)
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
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var s = await LoginAsync(o, Kim.OperatorA);
        var g = ReservationBody(o, vehicle, Tomorrow(4)); g.Remove("talepTuru"); g.Remove("projeAdi");
        var tid = (await Json(await Gonder(s, HttpMethod.Post, TestQuotation, g), HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Gonder(s, HttpMethod.Post, $"{TestQuotation}/{tid}/kabul")));
        var codes = responses.Select(r => (int)r.StatusCode).ToList();
        Assert.True(codes.Count(k => k == 200) == 1 && codes.Count(k => k == 409) == 7, string.Join(",", codes));
        foreach (var r in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
            await ExpectProblem(r, HttpStatusCode.Conflict, "cakisma");

        var reservations = await ReadAsync(o.TenantId, db => db.Reservations.AsNoTracking().Where(x => x.VehicleId == vehicle).ToListAsync());
        var res = Assert.Single(reservations);
        Assert.Equal(tid, res.KaynakTeklifId);
        var quotation = await ReadAsync(o.TenantId, db => db.Quotations.AsNoTracking().FirstAsync(x => x.Id == tid));
        Assert.Equal(res.Id, quotation.ReservationId);
        Assert.Equal(QuotationStatus.Kabul, quotation.Durum);
    }

    [Fact]
    public async Task H1_yapisal_cit_ayni_teklife_ikinci_rezervasyon_DB_tarafindan_reddedilir()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var quotationId = Guid.NewGuid();
        Reservation New(int day) => new()
        {
            ReservationNo = "T-" + Guid.NewGuid().ToString("N")[..8], MusteriId = o.MusteriId, VehicleId = vehicle,
            BasTar = Tomorrow(day), BitTar = Tomorrow(day + 1), Gun = 1, GunlukUcret = 100m, Tutar = 100m,
            Durum = ReservationStatus.Rezerv, KaynakTeklifId = quotationId,
        };
        await WriteDataAsync(o.TenantId, db => db.Reservations.Add(New(10)));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => WriteDataAsync(o.TenantId, db => db.Reservations.Add(New(20))));
        Assert.Contains("UX_Reservations_TenantId_KaynakTeklifId", ex.InnerException?.Message ?? "");
    }

    [Fact]
    public async Task L6_eszamanli_kiraya_cevir_tek_kira_ve_durum_ezilmez()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Admin);
        var vehicle0 = await VehicleAsync(o);
        var id0 = await OpenReservationAsync(s, o, vehicle0, Tomorrow(3));
        var codes = (await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Gonder(s, HttpMethod.Post, $"{TestReservation}/{id0}/kiraya-cevir"))))
            .Select(r => (int)r.StatusCode).ToList();
        Assert.Equal(1, await ReadAsync(o.TenantId, db => db.Rentals.CountAsync(x => x.ReservationId == id0)));
        Assert.True(codes.Count(k => k == 200) == 1 && !codes.Contains(500), string.Join(",", codes));

        var corrupt = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            var vehicle = await VehicleAsync(o);
            var id = await OpenReservationAsync(s, o, vehicle, Tomorrow(5 + i));
            var t = new[] { "onayla", "kiraya-cevir", "iptal" }.Select(e => Gonder(s, HttpMethod.Post, $"{TestReservation}/{id}/{e}")).ToArray();
            await Task.WhenAll(t);
            var r = await ReadAsync(o.TenantId, db => db.Reservations.AsNoTracking().FirstAsync(x => x.Id == id));
            var rental = await ReadAsync(o.TenantId, db => db.Rentals.CountAsync(x => x.ReservationId == id));
            // Oracle: kira açıldıysa rezervasyon KirayaCevrildi olmalı; açılmadıysa KirayaCevrildi olamaz.
            if ((rental > 0) != (r.Durum == ReservationStatus.KirayaCevrildi) || rental > 1 || t.Any(x => (int)x.Result.StatusCode == 500))
                corrupt.Add($"{i}:{r.Durum} kira={rental} [{string.Join(",", t.Select(x => (int)x.Result.StatusCode))}]");
        }
        Assert.True(corrupt.Count == 0, "Durum ezildi: " + string.Join(" | ", corrupt));
    }

    [Fact]
    public async Task M2_filo_tarih_sinirlari_ve_bozuk_eski_satir_listeyi_dusurmez()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var s = await LoginAsync(o, Kim.Admin);
        var far = new DateTimeOffset(9999, 6, 1, 0, 0, 0, TimeSpan.Zero);
        await ExpectProblem(await Gonder(s, HttpMethod.Post, Fleet, new { musteriId = o.MusteriId, vehicleId = vehicle, basTar = far, sureAy = 12, aylikUcret = 1000m }),
            HttpStatusCode.BadRequest, "dogrulama", "basTar");
        await ExpectProblem(await Gonder(s, HttpMethod.Post, Fleet, new { musteriId = o.MusteriId, vehicleId = vehicle, basTar = Tomorrow(400), sureAy = 12, aylikUcret = 1000m }),
            HttpStatusCode.BadRequest, "dogrulama", "basTar");
        await ExpectProblem(await Gonder(s, HttpMethod.Post, Fleet, new
        { musteriId = o.MusteriId, vehicleId = vehicle, sureAy = 12, aylikUcret = 1000m, sozlesmeTarihi = new DateTimeOffset(1, 1, 2, 0, 0, 0, TimeSpan.Zero) }),
            HttpStatusCode.BadRequest, "dogrulama", "sozlesmeTarihi");
        var id = (await Json(await Gonder(s, HttpMethod.Post, Fleet, new { musteriId = o.MusteriId, vehicleId = vehicle, sureAy = 2, aylikUcret = 1000m }),
            HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var version = (await Json(await s.C.GetAsync($"{Fleet}/{id}"))).GetProperty("surum").GetString();
        await ExpectProblem(await Gonder(s, HttpMethod.Put, $"{Fleet}/{id}/kunye", new { surum = version, imzaTarih = far }),
            HttpStatusCode.BadRequest, "dogrulama", "imzaTarih");

        // Sınır öncesi yazılmış bozuk satır (doğrudan DB): liste ve detay 200, toplam doğru (12 × 1.200 = 14.400).
        var corrupt = new FiloKiralama
        {
            No = "FK-BOZUK-" + Guid.NewGuid().ToString("N")[..4], MusteriId = o.MusteriId, VehicleId = vehicle, BasTar = far,
            SureAy = 12, AylikUcret = 1000m, KdvOrani = 0.20m, Currency = "TRY", Kur = 1m, Durum = FleetRentalStatus.Aktif,
        };
        await WriteDataAsync(o.TenantId, db => db.FiloKiralamalar.Add(corrupt));
        var list = await Json(await s.C.GetAsync(Fleet));
        var row = list.GetProperty("kayitlar").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == corrupt.Id);
        Assert.Equal(14400m, row.GetProperty("genelToplam").GetDecimal());
        await Json(await s.C.GetAsync($"{Fleet}/{corrupt.Id}"));
    }
}
