using RentACar.Application.Authorization;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Rapor;

/// <summary>
/// <c>/api/ui/v1/raporlar/*</c> — F10.1 rapor uçları (YALNIZ OKUR; para yazmaz). Hesap mantığı rapor servislerindedir
/// (<c>ReportService</c>, <c>CashService</c>, <c>VadeService</c>, <c>JobCalismaLogService</c>,
/// <c>PersonelVardiyaService</c>); uç yalnız ORTAK RAPOR ŞABLONUNU uygular:
/// <list type="number">
/// <item><b>Dönem</b>: <see cref="ReportPeriod"/> — <c>bas</c>/<c>bit</c> İstanbul günü, 1900–2100, gün-kırılımlı
/// raporlarda gün tavanı.</item>
/// <item><b>İzin</b>: Blazor sayfasıyla birebir. <c>izin:ViewReports</c> sayfaları → ViewReports; dört rolün de
/// açtığı sayfalar (araç durum takip, sigorta-muayene, km detay, periyodik servis, karşılaştırmalı analiz,
/// personel çalışma) → OperationsWrite VEYA ViewReports.</item>
/// <item><b>Şube kapsamı</b>: <see cref="ReportScope"/> (firma geneli → kapsamlıya 403; şube boyutlu → zorla).</item>
/// <item><b>Sayfalama/sıralama</b>: <see cref="ReportPageQuery"/> (boyut ≤ 200, beyaz liste sıralama; özet
/// TÜM satırlar üzerinden).</item>
/// <item><b>KVKK</b>: müşteri adı/iletişimi <see cref="CustomerMask"/>; TC hiçbir yanıtta yok.</item>
/// <item><b>Export</b>: <see cref="ReportExport"/> — mevcut sunucu export ucuna aynı süzgeçle bağlantı.</item>
/// </list>
/// </summary>
public static partial class ReportApi
{
    public static RouteGroupBuilder MapReportApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/raporlar").WithTags("Rapor").AlanlariEsle(F5Ortak.SiralamaKurallari);

        // izin:ViewReports sayfaları (Blazor [Authorize(Policy = "izin:ViewReports")]).
        var vr = g.MapGroup("").RequirePermission(Permission.ViewReports);
        // [Authorize(Roles = "Admin,Yonetici,Operator,Muhasebe")] sayfaları — dört rol = iki izinden biri.
        var ops = g.MapGroup("").RequireAnyPermission(Permission.OperationsWrite, Permission.ViewReports);

        MapLedger(vr);
        MapCustomer(vr);
        MapSales(vr);
        MapFleet(vr);
        MapOperations(ops);
        return g;
    }
}
