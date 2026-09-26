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
/// <para><b>Gün kovası</b> İstanbul gününe göre (<see cref="TenantGun"/>); Blazor sunucu yerel saatini kullanır —
/// üretimde sunucu saat dilimi İstanbul olduğundan aynı sonuç, CI (UTC) ve farklı saat dilimli sunucuda doğru olan bu.</para>
/// </summary>
public static class PanelApi
{
    public const string Gerekce =
        "Panel: her oturumun ana ekranı (Blazor '/' yalnız [Authorize], menü kaydında izinsiz). Kapılar İÇERİKTE: " +
        "finans özeti ViewReports, tahsilat anahtarı FinanceWrite ile; kira/rezervasyon listeleri şube kapsamlı servislerden.";

    public static RouteGroupBuilder MapPanelApi(this RouteGroupBuilder v1)
    {
        v1.MapGet("/panel/ozet", Ozet).IzinMuaf(Gerekce).WithTags("Panel");
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

    /// <summary><c>VarsayilanSekme</c>: seçim yokken açılacak kova (<see cref="PanelSekme"/> kuralı: dönüşlerde
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

    private static async Task<Ok<PanelOzetiYaniti>> Ozet(
        HttpContext http, RentalService kiralar, ReservationService rezervasyonlar, CashService kasa,
        ReportService raporlar, DashboardService pano, DueService vade, ComplaintService sikayet,
        PublicBookingRequestService talepler, TenantStatusCache kiraciDurum, ITenantContext kiraci,
        CancellationToken ct)
    {
        var simdi = DateTimeOffset.UtcNow;
        var bugun = TenantGun.Gun(simdi);
        var finansYazma = AuthExtensions.HasPermission(http.User, Permission.FinanceWrite);
        var raporOkuma = AuthExtensions.HasPermission(http.User, Permission.ViewReports);

        // Dönüşler: açık kiralar (Kirada ⇒ gerçek dönüş yok: ReturnAsync ikisini AYNI anda yazar), şube kapsamlı.
        var acikKira = await kiralar.SearchAsync(new RentalFilter { Durum = RentalStatus.Kirada }, ct);
        var islemSayilari = finansYazma && acikKira.Count > 0
            ? await kasa.GetRentalTransactionCountsAsync(acikKira.Select(r => r.Id).ToList(), ct)
            : new Dictionary<Guid, int>();
        List<PanelDonusSatiri> Donus(Func<DateOnly, bool> kosul) => acikKira
            .Where(r => kosul(TenantGun.Gun(r.BitTar)))
            .OrderBy(r => r.BitTar)
            .Select(r => new PanelDonusSatiri(r.Id, r.SozlesmeNo, r.BitTar,
                MusteriGorunumu.ListeAdi(r.MusteriAd, r.MusteriAnonimAd), r.Plaka, r.DonusOfisi,
                r.Bakiye, r.Doviz,
                // Home.razor: finans yetkisi + bakiye > 0 (kayıtsız kira sayaç sözlüğünde yok → 0)
                finansYazma && r.Bakiye > 0m
                    ? KiraApi.TahsilatVerisi(r.Id, r.MusteriId, r.Bakiye, r.Doviz, islemSayilari.GetValueOrDefault(r.Id))
                    : null))
            .ToList();
        var donusGec = Donus(g => g < bugun);
        var donusBugun = Donus(g => g == bugun);
        var donusYarin = Donus(g => g == bugun.AddDays(1));

        // Çıkışlar: açık rezervasyonlar (Rezerv/Onaylı), şube kapsamlı.
        var acikRez = (await rezervasyonlar.SearchAsync(new ReservationFilter(), ct))
            .Where(r => r.Rez.Durum is ReservationStatus.Rezerv or ReservationStatus.Onayli).ToList();
        List<PanelCikisSatiri> Cikis(Func<DateOnly, bool> kosul) => acikRez
            .Where(r => kosul(TenantGun.Gun(r.Rez.BasTar)))
            .OrderBy(r => r.Rez.BasTar)
            .Select(r => new PanelCikisSatiri(r.Rez.Id, r.Rez.ReservationNo, r.Rez.BasTar,
                MusteriGorunumu.ListeAdi(r.MusteriAd, r.MusteriAnonimAd), r.Plaka, r.Rez.CikisOfisi))
            .ToList();
        var cikisGec = Cikis(g => g < bugun);
        var cikisBugun = Cikis(g => g == bugun);
        var cikisYarin = Cikis(g => g == bugun.AddDays(1));

        // Site talebi: yalnız Web Sitesi modülü açıkken; yetkisiz rol panoyu DÜŞÜRMEZ (Home ile aynı savunma).
        SiteTalebiOzeti? siteTalebi = null;
        if (kiraci.TenantId is { } tid && await kiraciDurum.WebSitesiModuluAsync(tid, ct))
        {
            try
            {
                var o = await talepler.SummaryAsync(ct);
                siteTalebi = new SiteTalebiOzeti(o.Yeni, o.EnEskiGun);
            }
            catch (ValidationException) { siteTalebi = null; }
        }

        var filo = await raporlar.GetFleetUtilizationAsync(ct);
        var kmGecen = (await raporlar.GetPeriodicServiceAsync(ct: ct)).Count(s => s.KalanKm < 0);

        var vadeler = await vade.GetAllAsync(ct: ct);
        VadeKademesi Kademe(string tur) => new(
            vadeler.Count(v => v.Tur == tur && v.Bucket == DueBucket.YediGun),
            vadeler.Count(v => v.Tur == tur && v.Bucket == DueBucket.OtuzGun),
            vadeler.Count(v => v.Tur == tur && v.Bucket == DueBucket.Gecmis));
        var uyarilar = await vade.GetWarningsAsync(ct: ct);
        var acikSikayet = (await sikayet.ListAsync(ct)).Count(s => s.Durum == ComplaintStatus.Acik);

        PanelFinans? finans = null;
        if (raporOkuma)
        {
            var d = await pano.GetAsync(simdi, ct);
            var havuz = (await raporlar.GetFleetAnalysisAsync(ct: ct)).HavuzKpi;
            var trend = await raporlar.GetMonthlyRevenueTrendAsync(6, ct: ct);
            finans = new PanelFinans(d.KasaBakiye, d.BankaBakiye, d.AcikBakiye, d.BugunTahsilatTutar, d.BugunTahsilatAdet,
                havuz?.DolulukYuzde, havuz?.RevPacd, havuz?.Adr,
                trend.Select(t => new AylikGelir(t.AyBas, t.Gelir)).ToList());
        }

        return TypedResults.Ok(new PanelOzetiYaniti(
            bugun,
            new PanelKpi(filo.Toplam, filo.Kirada, filo.Musait, filo.Serviste, acikRez.Count, kmGecen, cikisGec.Count, siteTalebi),
            new PanelVade(Kademe("Trafik"), Kademe("Kasko"), Kademe("Muayene"),
                uyarilar.Count(w => w.Bucket == DueBucket.Gecmis), uyarilar.Count(w => w.Bucket != DueBucket.Gecmis), acikSikayet),
            new PanelDonusKovalari(donusGec, donusBugun, donusYarin, PanelSekme.Etkin(null, donusGec.Count)),
            new PanelCikisKovalari(cikisGec, cikisBugun, cikisYarin, PanelSekme.CikisEtkin(null)),
            finans));
    }
}
