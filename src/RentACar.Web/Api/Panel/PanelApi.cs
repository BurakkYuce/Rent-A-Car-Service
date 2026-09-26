using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Crm;
using RentACar.Application.Dashboard;
using RentACar.Application.Finance;
using RentACar.Application.PublicSite;
using RentACar.Application.Regulation;
using RentACar.Application.Reporting;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Kira;
using RentACar.Web.Components.Pages;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Panel;

/// <summary>
/// <c>GET /api/ui/v1/panel/ozet</c> (F4.1) — ana ekran (Blazor <c>Home.razor</c>) verisi TEK istekte: filo KPI'ları,
/// vade kademeleri, gecikmiş/bugün/yarın kovalı dönüş ve çıkış listeleri, finans özeti ve satır başına
/// "Tahsil Et" verisi (<see cref="TahsilatBilgisi"/> — deterministik <c>TahsilatAnahtar</c> SUNUCUDA üretilir).
///
/// <para><b>Kapılar içerikte:</b> uç her oturuma açık (Blazor <c>/</c> yalnız <c>[Authorize]</c>; menü kaydında
/// "Panel" izinsiz). Finans özeti + filo havuz KPI + gelir trendi yalnız <see cref="Permission.ViewReports"/>'ta
/// hesaplanır ve döner (null değilse); tahsilat verisi yalnız <see cref="Permission.FinanceWrite"/>'ta (tahsilat
/// ucunun izni). Blazor bunları rolle (Admin/Yönetici/Muhasebe) kapılıyor — aynı rol kümesi, kullanıcı-bazlı
/// istisnalar artık yansır (F4.6 yönü). Kira/rezervasyon listeleri şube kapsamlı servislerden gelir.</para>
///
/// <para><b>Gün kovası</b> İstanbul gününe göre (<see cref="TenantDay"/>); Blazor sunucu yerel saatini kullanır —
/// üretimde sunucu saat dilimi İstanbul olduğundan aynı sonuç, CI (UTC) ve farklı saat dilimli sunucuda doğru olan bu.</para>
/// </summary>
public static class PanelApi
{
    public const string Reason =
        "Panel: her oturumun ana ekranı (Blazor '/' yalnız [Authorize], menü kaydında izinsiz). Kapılar İÇERİKTE: " +
        "finans özeti ViewReports, tahsilat anahtarı FinanceWrite ile; kira/rezervasyon listeleri şube kapsamlı servislerden.";

    public static RouteGroupBuilder MapPanelApi(this RouteGroupBuilder v1)
    {
        v1.MapGet("/panel/ozet", Summary).PermissionExempt(Reason).WithTags("Panel");
        return v1;
    }

    // ---------------------------------------------------------------- yanıt tipleri

    public sealed record VadeKademesi(int YediGun, int OtuzGun, int Gecmis);

    public sealed record SiteTalebiOzeti(int Yeni, int? EnEskiGun);

    public sealed record PanelKpi(
        int ToplamArac, int Kirada, int Musait, int Serviste, int AcikRezervasyon, int KmGecenBakim,
        int GorulmeyenRezervasyon, SiteTalebiOzeti? SiteTalebi);

    public sealed record PanelVade(VadeKademesi Trafik, VadeKademesi Kasko, VadeKademesi Muayene,
        int GecmisUyari, int YaklasanUyari, int AcikSikayet);

    /// <summary>Dönüş satırı (açık kira, planlanan dönüşe göre). <c>Tahsilat</c>: Tahsil Et verisi ya da null.</summary>
    public sealed record PanelDonusSatiri(
        Guid RentalId, string SozlesmeNo, DateTimeOffset Tarih, string MusteriAd, string Plaka, string? Ofis,
        decimal Bakiye, string? Doviz, TahsilatBilgisi? Tahsilat);

    /// <summary>Çıkış satırı (açık rezervasyon — Rezerv/Onaylı — planlanan başlangıca göre).</summary>
    public sealed record PanelCikisSatiri(
        Guid ReservationId, string ReservationNo, DateTimeOffset Tarih, string MusteriAd, string Plaka, string? Ofis);

    /// <summary><c>VarsayilanSekme</c>: seçim yokken açılacak kova (<see cref="PanelTab"/> kuralı: dönüşlerde
    /// gecikmiş varsa "gec", yoksa "bugun"; çıkışlarda daima "bugun").</summary>
    public sealed record PanelDonusKovalari(
        IReadOnlyList<PanelDonusSatiri> Gecikmis, IReadOnlyList<PanelDonusSatiri> Bugun,
        IReadOnlyList<PanelDonusSatiri> Yarin, string VarsayilanSekme);

    public sealed record PanelCikisKovalari(
        IReadOnlyList<PanelCikisSatiri> Gecikmis, IReadOnlyList<PanelCikisSatiri> Bugun,
        IReadOnlyList<PanelCikisSatiri> Yarin, string VarsayilanSekme);

    public sealed record AylikGelir(DateTimeOffset AyBas, decimal Gelir);

    public sealed record PanelFinans(
        decimal KasaBakiye, decimal BankaBakiye, decimal AcikBakiye, decimal BugunTahsilatTutar, int BugunTahsilatAdet,
        decimal? FiloDolulukYuzde, decimal? RevPacd, decimal? Adr, IReadOnlyList<AylikGelir> GelirTrendi);

    public sealed record PanelOzetiYaniti(
        DateOnly Bugun, PanelKpi Kpi, PanelVade Vade, PanelDonusKovalari Donusler, PanelCikisKovalari Cikislar,
        PanelFinans? Finans);

    // ---------------------------------------------------------------- uç

    private static async Task<Ok<PanelOzetiYaniti>> Summary(
        HttpContext http, RentalService kiralar, ReservationService rezervasyonlar, CashService kasa,
        ReportService raporlar, DashboardService pano, DueService vade, ComplaintService sikayet,
        PublicBookingRequestService talepler, TenantStatusCache kiraciDurum, ITenantContext kiraci,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var today = TenantDay.Day(now);
        var financeWrite = AuthExtensions.HasPermission(http.User, Permission.FinanceWrite);
        var reportRead = AuthExtensions.HasPermission(http.User, Permission.ViewReports);

        // Dönüşler: açık kiralar (Kirada ⇒ gerçek dönüş yok: ReturnAsync ikisini AYNI anda yazar), şube kapsamlı.
        var openRental = await kiralar.SearchAsync(new RentalFilter { Durum = RentalStatus.Kirada }, ct);
        var transactionCounts = financeWrite && openRental.Count > 0
            ? await kasa.GetRentalTransactionCountsAsync(openRental.Select(r => r.Id).ToList(), ct)
            : new Dictionary<Guid, int>();
        List<PanelDonusSatiri> Return(Func<DateOnly, bool> condition) => openRental
            .Where(r => condition(TenantDay.Day(r.BitTar)))
            .OrderBy(r => r.BitTar)
            .Select(r => new PanelDonusSatiri(r.Id, r.SozlesmeNo, r.BitTar,
                CustomerView.ListName(r.MusteriAd, r.MusteriAnonimAd), r.Plaka, r.DonusOfisi,
                r.Bakiye, r.Doviz,
                // Home.razor: finans yetkisi + bakiye > 0 (kayıtsız kira sayaç sözlüğünde yok → 0)
                financeWrite && r.Bakiye > 0m
                    ? RentalApi.CollectionData(r.Id, r.MusteriId, r.Bakiye, r.Doviz, transactionCounts.GetValueOrDefault(r.Id))
                    : null))
            .ToList();
        var returnOverdue = Return(g => g < today);
        var returnToday = Return(g => g == today);
        var returnTomorrow = Return(g => g == today.AddDays(1));

        // Çıkışlar: açık rezervasyonlar (Rezerv/Onaylı), şube kapsamlı.
        var openReservation = (await rezervasyonlar.SearchAsync(new ReservationFilter(), ct))
            .Where(r => r.Rez.Durum is ReservationStatus.Rezerv or ReservationStatus.Onayli).ToList();
        List<PanelCikisSatiri> Logout(Func<DateOnly, bool> condition) => openReservation
            .Where(r => condition(TenantDay.Day(r.Rez.BasTar)))
            .OrderBy(r => r.Rez.BasTar)
            .Select(r => new PanelCikisSatiri(r.Rez.Id, r.Rez.ReservationNo, r.Rez.BasTar,
                CustomerView.ListName(r.MusteriAd, r.MusteriAnonimAd), r.Plaka, r.Rez.CikisOfisi))
            .ToList();
        var pickupOverdue = Logout(g => g < today);
        var pickupToday = Logout(g => g == today);
        var pickupTomorrow = Logout(g => g == today.AddDays(1));

        // Site talebi: yalnız Web Sitesi modülü açıkken; yetkisiz rol panoyu DÜŞÜRMEZ (Home ile aynı savunma).
        SiteTalebiOzeti? siteRequest = null;
        if (kiraci.TenantId is { } tid && await kiraciDurum.WebsiteModuleAsync(tid, ct))
        {
            try
            {
                var o = await talepler.SummaryAsync(ct);
                siteRequest = new SiteTalebiOzeti(o.Yeni, o.EnEskiGun);
            }
            catch (ValidationException) { siteRequest = null; }
        }

        var fleet = await raporlar.GetFleetUtilizationAsync(ct);
        var kmElapsed = (await raporlar.GetPeriodicServiceAsync(ct: ct)).Count(s => s.KalanKm < 0);

        var dues = await vade.GetAllAsync(ct: ct);
        VadeKademesi Tier(string type) => new(
            dues.Count(v => v.Tur == type && v.Bucket == DueBucket.YediGun),
            dues.Count(v => v.Tur == type && v.Bucket == DueBucket.OtuzGun),
            dues.Count(v => v.Tur == type && v.Bucket == DueBucket.Gecmis));
        var warnings = await vade.GetWarningsAsync(ct: ct);
        var openComplaint = (await sikayet.ListAsync(ct)).Count(s => s.Durum == ComplaintStatus.Acik);

        PanelFinans? finance = null;
        if (reportRead)
        {
            var d = await pano.GetAsync(now, ct);
            var pool = (await raporlar.GetFleetAnalysisAsync(ct: ct)).HavuzKpi;
            var trend = await raporlar.GetMonthlyRevenueTrendAsync(6, ct: ct);
            finance = new PanelFinans(d.KasaBakiye, d.BankaBakiye, d.AcikBakiye, d.BugunTahsilatTutar, d.BugunTahsilatAdet,
                pool?.DolulukYuzde, pool?.RevPacd, pool?.Adr,
                trend.Select(t => new AylikGelir(t.AyBas, t.Gelir)).ToList());
        }

        return TypedResults.Ok(new PanelOzetiYaniti(
            today,
            new PanelKpi(fleet.Toplam, fleet.Kirada, fleet.Musait, fleet.Serviste, openReservation.Count, kmElapsed, pickupOverdue.Count, siteRequest),
            new PanelVade(Tier("Trafik"), Tier("Kasko"), Tier("Muayene"),
                warnings.Count(w => w.Bucket == DueBucket.Gecmis), warnings.Count(w => w.Bucket != DueBucket.Gecmis), openComplaint),
            new PanelDonusKovalari(returnOverdue, returnToday, returnTomorrow, PanelTab.Active(null, returnOverdue.Count)),
            new PanelCikisKovalari(pickupOverdue, pickupToday, pickupTomorrow, PanelTab.IsPickupActive(null)),
            finance));
    }
}
