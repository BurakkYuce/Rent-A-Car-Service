using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Regulation;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;
using S = RentACar.Web.Api.ServiceInsurance.ServiceInsuranceShared;

namespace RentACar.Web.Api.ServiceInsurance;

/// <summary>
/// <c>/api/ui/v1/regulasyon/*</c> + <c>/vade</c> (F9.1) — sigorta poliçesi (+ zeyil), MTV, muayene, vade panosu.
/// İş mantığı <see cref="RegulationService"/> / <see cref="DueService"/>'te.
/// <para><b>İzin:</b> okuma OperationsWrite ∨ FinanceWrite ∨ ViewReports (Blazor sayfası tüm rollere açık); kayıt/zeyil
/// OperationsWrite (bilgi kaydı, deftere yazmaz); ÖDEME FinanceWrite (dengeli defter: Borç Gider[araç] / Alacak
/// Kasa-Banka).</para>
/// <para><b>Kapsam:</b> kayıtlar ARACIN şubesinden geçer (F6.1b alt kayıt kuralı; Blazor süzmüyordu — yeni yüzey o
/// açığı taşımaz). Tekil uçlarda kapsam durumdan ÖNCE (403), başka kiracı 404.</para>
/// </summary>
internal static partial class RegulationApi
{
    private const string Root = UiApiExtensions.V1 + "/regulasyon";
    private static readonly Permission[] ReadAny = [Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports];

    public static void Map(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/regulasyon").WithTags("Sigorta & Regülasyon");
        g.MapGet("/secenekler", Options).RequireAnyPermission(ReadAny);

        g.MapGet("/sigortalar", ListPolicies).AlanlariEsle(F5Ortak.SiralamaKurallari).RequireAnyPermission(ReadAny);
        g.MapGet("/sigortalar/{id:guid}", PolicyDetail).RequireAnyPermission(ReadAny);
        g.MapPost("/sigortalar", CreatePolicy).AlanlariEsle(PolicyRules).RequirePermission(Permission.OperationsWrite)
            .Produces<UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        g.MapPost("/sigortalar/{id:guid}/odeme", PayPolicy).AlanlariEsle(PaymentRules).RequirePermission(Permission.FinanceWrite)
            .Produces<UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        g.MapPost("/sigortalar/{id:guid}/zeyiller", AddEndorsement).AlanlariEsle(EndorsementRules)
            .RequirePermission(Permission.OperationsWrite);
        g.MapDelete("/zeyiller/{id:guid}", DeleteEndorsement).RequirePermission(Permission.OperationsWrite);
        g.MapGet("/zeyiller", ListEndorsements).AlanlariEsle(F5Ortak.SiralamaKurallari).RequireAnyPermission(ReadAny);

        g.MapGet("/mtv", ListMtv).AlanlariEsle(F5Ortak.SiralamaKurallari).RequireAnyPermission(ReadAny);
        g.MapGet("/mtv/{id:guid}", MtvDetailEndpoint).RequireAnyPermission(ReadAny);
        g.MapPost("/mtv", CreateMtv).AlanlariEsle(MtvRules).RequirePermission(Permission.OperationsWrite)
            .Produces<UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        g.MapPost("/mtv/{id:guid}/odeme", PayMtv).AlanlariEsle(PaymentRules).RequirePermission(Permission.FinanceWrite)
            .Produces<UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");

        g.MapGet("/muayeneler", ListInspections).AlanlariEsle(F5Ortak.SiralamaKurallari).RequireAnyPermission(ReadAny);
        g.MapGet("/muayeneler/{id:guid}", InspectionDetailEndpoint).RequireAnyPermission(ReadAny);
        g.MapPost("/muayeneler", CreateInspection).AlanlariEsle(InspectionRules).RequirePermission(Permission.OperationsWrite)
            .Produces<UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");
        g.MapPost("/muayeneler/{id:guid}/odeme", PayInspection).AlanlariEsle(PaymentRules).RequirePermission(Permission.FinanceWrite)
            .Produces<UiHata.MukerrerProblemi>(StatusCodes.Status409Conflict, "application/problem+json");

        v1.MapGroup("").MapGet("/vade", DueBoardEndpoint).WithTags("Sigorta & Regülasyon").AlanlariEsle(F5Ortak.SiralamaKurallari)
            .RequireAnyPermission(ReadAny);
    }

    private static readonly (string, string)[] PolicyRules =
    [
        ("Araç", "vehicleId"), ("Bitiş başlangıçtan", "bitis"), ("Prim", "prim"), ("Araç değeri", "aracDegeri"),
        ("İMM değeri", "immDegeri"), ("Aksesuar değeri", "aksesuarDegeri"), ("Geçersiz para birimi", "doviz"),
    ];

    private static readonly (string, string)[] MtvRules = [("Araç", "vehicleId"), ("Dönem", "donem"), ("Tutar", "tutar")];

    private static readonly (string, string)[] InspectionRules =
    [
        ("Araç", "vehicleId"), ("Bitiş muayene", "bitis"), ("Ücret", "ucret"), ("İşlem KM", "islemKm"),
    ];

    private static readonly (string, string)[] EndorsementRules =
    [
        ("Zeyil no", "zeyilNo"), ("Bu poliçede", "zeyilNo"), ("Zeyil tarihi", "tarih"), ("Zeyil tipi", "tipi"),
        ("Zeyil nedeni", "neden"), ("Zeyil değeri", "deger"),
    ];

    private static readonly (string, string)[] PaymentRules =
    [
        ("Ödeme hesabı", "hesap"), ("Seçilen kasa/banka", "hesapId"), ("Ödeme tutarı", "tutar"), ("Ceza", "ceza"),
        ("Zeyil ek prim", "zeyilEkPrim"), ("Kısmi ödemede işlem anahtarı", "Idempotency-Key"),
        ("MTV ödeme tarihi", "odemeTarihi"), ("Muayene ödeme tarihi", "odemeTarihi"), ("Dönem", "odemeTarihi"),
    ];

    // ------------------------------------------------------------------ seçenekler

    private static async Task<Ok<RegulationOptions>> Options(
        RegulationService reg, Application.InsuranceCompanies.InsuranceCompanyService companies, IDbContextFactory<AppDbContext> dbf,
        CancellationToken ct)
    {
        var active = await companies.ListActiveAsync(ct);
        await using var db = await dbf.CreateDbContextAsync(ct);
        var used = await db.InsurancePolicies.AsNoTracking().Where(p => p.Firma != null).Select(p => p.Firma!).Distinct().ToListAsync(ct);
        var usedTypes = await db.InsurancePolicyZeyilleri.AsNoTracking().Where(z => z.Tipi != null).Select(z => z.Tipi!).Distinct().ToListAsync(ct);
        static List<string> Merge(IEnumerable<string?> a) => a.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Create(S.Tr, true)).ToList();
        return TypedResults.Ok(new RegulationOptions(
            Merge(active.Select(c => c.Ad).Concat(used)),
            Merge(new[] { "Zam", "Tenzil", "Değişiklik", "İptal", "Yenileme" }.Concat(usedTypes)),
            ["TRY", "EUR", "USD", "GBP"], Enum.GetNames<InsuranceType>()));
    }

    // ------------------------------------------------------------------ vade panosu

    private static readonly SortFieldMap<DueItemDto> DueSort = SortFieldMap<DueItemDto>
        .Create(x => x.VehicleId).Alan("plaka", x => x.Plaka).Alan("tur", x => x.Tur).Alan("bitis", x => x.Bitis)
        .Alan("kalanGun", x => x.KalanGun);

    private static async Task<Ok<DueBoard>> DueBoardEndpoint(
        string? kova, string? tur, string? plaka, int? sayfa, int? boyut, string? sirala, DueService dues,
        IDbContextFactory<AppDbContext> dbf, ICurrentUser user, CancellationToken ct)
    {
        var bucket = F5Ortak.EnumAdi<DueBucket>(kova, "kova");
        var items = await S.VisibleAsync(dbf, user, await dues.GetAllAsync(ct: ct), i => i.VehicleId, ct);
        var plates = await S.PlatesAsync(dbf, items.Select(i => i.VehicleId), ct);
        var rows = items.Select(i => new DueItemDto(i.VehicleId, F5Ortak.Plaka(plates, i.VehicleId), i.Tur, i.Bitis, i.KalanGun,
            i.Bucket.ToString())).ToList();
        if (F5Ortak.Nz(tur) is { } t) rows = rows.Where(r => string.Equals(r.Tur, t, StringComparison.OrdinalIgnoreCase)).ToList();
        if (F5Ortak.Nz(plaka) is { } p) rows = rows.Where(r => r.Plaka.Contains(p, StringComparison.OrdinalIgnoreCase)).ToList();
        var summary = new DueSummary(rows.Count(r => r.Kova == nameof(DueBucket.Gecmis)),
            rows.Count(r => r.Kova == nameof(DueBucket.YediGun)), rows.Count(r => r.Kova == nameof(DueBucket.OtuzGun)),
            rows.Count(r => r.Kova == nameof(DueBucket.Ileri)));
        if (bucket is { } b) rows = rows.Where(r => r.Kova == b.ToString()).ToList();
        return TypedResults.Ok(new DueBoard(summary, F5Ortak.Sayfala(rows, DueSort, sayfa, boyut, sirala)));
    }
}
