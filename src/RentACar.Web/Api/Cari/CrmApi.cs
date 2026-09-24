using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Reporting;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Kira;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Cari;

/// <summary>
/// F7.1 — CRM uçları: anket, şikayet, assistans, hukuk (OperationsWrite; Blazor sayfalarıyla aynı kapı) + CRM analiz
/// (ViewReports) + fazın seçim ucu (kira sözleşmesi). Cari uçları <see cref="CustomerApi"/>.
/// </summary>
public static partial class CrmApi
{
    private const int PickMax = 20;

    public static RouteGroupBuilder MapCrmApi(this RouteGroupBuilder v1)
    {
        var ops = v1.MapGroup("").RequirePermission(Permission.OperationsWrite);
        MapSurveys(ops);
        MapComplaints(ops);
        MapAssistance(ops);
        MapLegalFiles(ops);
        // #300: gider formunun "Sözleşme" alanı Muhasebe'ye de açık (OperationsWrite VEYA FinanceWrite). Şube kapsamı ve
        // dönen alanlar DEĞİŞMEDİ (sözleşme no, plaka, KVKK'lı müşteri adı; TC/telefon yok).
        v1.MapGet("/crm/secim/kira", PickRental).WithTags("CRM")
            .RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite);

        var reports = v1.MapGroup("/crm/analiz").WithTags("CRM").RequirePermission(Permission.ViewReports);
        reports.MapGet("", Analysis).AlanlariEsle(F5Ortak.SiralamaKurallari);
        reports.MapGet("/secenekler", async (ReportService r, ICurrentUser user, CancellationToken ct) =>
        {
            RequireCompanyWide(user);
            var o = await r.GetMusteriSegmentSecenekleriAsync(ct);
            return TypedResults.Ok(new CrmFilterOptions(o.Kaynaklar, o.Ofisler));
        });
        return v1;
    }

    /// <summary>F7.1 tek kayıt noktası (UiApiExtensions'ta tek satır): cari + CRM uçları.</summary>
    public static void Map(RouteGroupBuilder v1)
    {
        v1.MapCustomerApi();
        v1.MapCrmApi();
    }

    /// <summary>
    /// CRM analiz firma GENELİ agregadır (tüm şubelerin kiraları, müşteri iletişim bilgisi). Şubeye bağlı kullanıcı (kullanıcı
    /// bazlı ViewReports istisnası verilmiş operatör) bunu göremez — başka şubelerin müşteri listesine kapı olurdu.
    /// </summary>
    private static void RequireCompanyWide(ICurrentUser user)
    {
        if (!BranchScope.EffectiveFilter(user).Unrestricted)
            throw new YetkiYokException("CRM analizi firma geneli rapordur; şube kapsamlı kullanıcı göremez.");
    }

    // ================================================================== CRM analiz

    private static readonly SiralamaHaritasi<CrmSegmentRow> SegmentSort = SiralamaHaritasi<CrmSegmentRow>
        .Olustur(r => r.CariId)
        .Alan("ad", r => r.Ad).Alan("kiraSayisi", r => r.KiraSayisi).Alan("toplamCiro", r => r.ToplamCiro)
        .Alan("ortalamaKiraBedeli", r => r.OrtalamaKiraBedeli).Alan("ortalamaKm", r => r.OrtalamaKm)
        .Alan("hizmetBedeli", r => r.HizmetBedeli).Alan("ilkKiraZamani", r => r.IlkKiraZamani)
        .Alan("sonIslem", r => r.SonIslem).Alan("segment", r => r.Segment);

    public sealed class AnalysisFilter
    {
        /// <summary>Kira BAŞLANGICINA uygulanır (gün dahil, İstanbul günü).</summary>
        [FromQuery(Name = "tarihBas")] public DateOnly? TarihBas { get; set; }
        [FromQuery(Name = "tarihBit")] public DateOnly? TarihBit { get; set; }
        [FromQuery(Name = "minKira")] public int? MinKira { get; set; }
        [FromQuery(Name = "kaynak")] public string? Kaynak { get; set; }
        [FromQuery(Name = "ofis")] public string? Ofis { get; set; }
    }

    private static async Task<Ok<CrmAnalysisDto>> Analysis(
        [AsParameters] AnalysisFilter f, ReportService reports, ICurrentUser user, IDbContextFactory<AppDbContext> dbf,
        int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        RequireCompanyWide(user);
        if (f.MinKira is < 0 or > 100_000) throw new ValidationException("Kiralama adedi 0 ile 100.000 arasında olmalıdır.", "minKira");
        var (min, max) = F5Ortak.GunAraligi(f.TarihBas, f.TarihBit);
        var segment = await reports.GetMusteriSegmentAsync(new MusteriSegmentFilter
        {
            Bas = min, Bit = max, MinKiraSayisi = f.MinKira, RezKaynak = F5Ortak.Nz(f.Kaynak), CikisOfis = F5Ortak.Nz(f.Ofis),
        }, ct);
        var flags = await PrivacyFlagsAsync(dbf, segment.Select(s => s.CariId), ct);
        var rows = segment.Select(s =>
        {
            var p = flags.GetValueOrDefault(s.CariId);
            return new CrmSegmentRow(s.CariId, p?.AnonimAd == true ? MusteriGorunumu.AnonimAdEtiketi : s.Ad,
                p?.AnonimMail == true ? null : s.Mail, p?.AnonimTelefon == true ? null : s.Tel, s.KiraSayisi, s.ToplamCiro,
                s.OrtalamaKiraBedeli, s.OrtalamaKm, s.HizmetBedeli, s.DogumTarihi, s.IlkKiraZamani, s.SonIslem, s.Segment);
        }).ToList();
        var staff = (await reports.GetPersonelCalismaAsync(ct)).Select(p => new CrmStaffRow(p.PersonelId, p.Ad, p.TahsisSayisi)).ToList();
        return TypedResults.Ok(new CrmAnalysisDto(rows.Count, rows.Sum(r => r.ToplamCiro), rows.Sum(r => r.HizmetBedeli),
            F5Ortak.Sayfala(rows, SegmentSort, sayfa, boyut, sirala), staff));
    }

    private sealed record PrivacyFlags(bool AnonimAd, bool AnonimTelefon, bool AnonimMail);

    private static async Task<Dictionary<Guid, PrivacyFlags>> PrivacyFlagsAsync(
        IDbContextFactory<AppDbContext> dbf, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];
        await using var db = await dbf.CreateDbContextAsync(ct);
        return await db.Customers.AsNoTracking().Where(c => list.Contains(c.Id))
            .Select(c => new { c.Id, c.AnonimAd, c.AnonimTelefon, c.AnonimMail })
            .ToDictionaryAsync(c => c.Id, c => new PrivacyFlags(c.AnonimAd, c.AnonimTelefon, c.AnonimMail), ct);
    }

    // ================================================================== seçim: kira sözleşmesi

    /// <summary>
    /// Anket/şikayet/assistans formlarının kira seçimi: sözleşme no ya da plaka içinde geçen, en yeni başlangıç önce.
    /// Şube kapsamına süzülür (başka şubenin kirası önerilmez); müşteri adı KVKK kuralıyla; TC/telefon YOK.
    /// </summary>
    private static async Task<Ok<IReadOnlyList<RentalPickItem>>> PickRental(
        string? q, int? limit, ICurrentUser user, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var n = Math.Clamp(limit ?? PickMax, 1, PickMax);
        var term = F5Ortak.Nz(q);
        if (term is { Length: > 64 }) term = term[..64];
        var filter = BranchScope.EffectiveFilter(user);
        List<(RentalContract R, string? Plaka)> candidates;
        await using (var db = await dbf.CreateDbContextAsync(ct))
        {
            var query = from r in db.Rentals.AsNoTracking()
                        join v in db.Vehicles.AsNoTracking() on r.VehicleId equals v.Id into vg
                        from v in vg.DefaultIfEmpty()
                        select new { r, Plaka = v == null ? null : v.Plaka };
            if (term is not null)
            {
                var like = $"%{term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
                var plate = $"%{term.Replace(" ", "").ToUpperInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
                query = query.Where(x => EF.Functions.ILike(x.r.SozlesmeNo, like) || (x.Plaka != null && EF.Functions.ILike(x.Plaka, plate)));
            }
            candidates = (await query.OrderByDescending(x => x.r.BasTar).ThenBy(x => x.r.Id).Take(500).ToListAsync(ct))
                .Select(x => (x.r, (string?)x.Plaka)).ToList();
        }
        var visible = candidates.Where(x => BranchScope.InScope(filter, x.R.CikisSubeId, x.R.CikisOfisi)).Take(n).ToList();
        var names = await F5Ortak.CarilerAsync(dbf, visible.Select(x => x.R.MusteriId), ct);
        return TypedResults.Ok<IReadOnlyList<RentalPickItem>>(visible.Select(x => new RentalPickItem(
            x.R.Id, x.R.SozlesmeNo, x.Plaka, F5Ortak.CariAdi(names, x.R.MusteriId), x.R.MusteriId, x.R.CikisOfisi, x.R.BasTar)).ToList());
    }
}
