using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Crm;
using RentACar.Application.Locations;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Kira;
using RentACar.Web.Api.Rezervasyon;

namespace RentACar.Web.Api.Cari;

/// <summary>
/// <c>/api/ui/v1/anketler/*</c> — müşteri anketi (Blazor <c>AnketList</c>). İzin OperationsWrite (Blazor ile aynı);
/// şube kapsamı <see cref="CrmScope"/>; müşteri adı <see cref="CustomerView"/> kuralıyla.
/// </summary>
public static partial class CrmApi
{
    private const int MaxAnswers = 50;

    private static readonly (string, string)[] SurveyFieldRules =
    [
        ("Puan", "puan"), ("Anket tarihi", "tarih"), ("Aynı soru sırası", "cevaplar"), ("Kira sözleşmesi bulunamadı", "rentalId"),
    ];

    private static void MapSurveys(RouteGroupBuilder g)
    {
        var s = g.MapGroup("/anketler").WithTags("CRM");
        s.MapGet("", ListSurveys).MapFields(F5Shared.SortRules);
        s.MapGet("/varsayilan-sorular", () => TypedResults.Ok<IReadOnlyList<string>>(SurveyService.DefaultQuestions));
        s.MapGet("/{id:guid}", GetSurvey);
        s.MapPost("", CreateSurvey).MapFields(SurveyFieldRules);
        s.MapPut("/{id:guid}", UpdateSurvey).MapFields(SurveyFieldRules);
        s.MapDelete("/{id:guid}", DeleteSurvey);
    }

    private static ProblemHttpResult SurveyNotFound() => F5Shared.NotFound("Anket bulunamadı.");

    private static readonly SortFieldMap<SurveyRow> SurveySort = SortFieldMap<SurveyRow>
        .Create(r => r.Id)
        .Alan("tarih", r => r.Tarih).Alan("puan", r => r.Puan).Alan("durum", r => r.Durum)
        .Alan("anketTuru", r => r.AnketTuru).Alan("cikisOfisi", r => r.CikisOfisi).Alan("sozlesmeNo", r => r.SozlesmeNo);

    public sealed class SurveyListFilter
    {
        [FromQuery(Name = "cariId")] public Guid? CariId { get; set; }
        [FromQuery(Name = "anketTuru")] public string? AnketTuru { get; set; }
        [FromQuery(Name = "durum")] public string? Durum { get; set; }
        [FromQuery(Name = "tarihBas")] public DateOnly? TarihBas { get; set; }
        [FromQuery(Name = "tarihBit")] public DateOnly? TarihBit { get; set; }
        [FromQuery(Name = "cikisOfisi")] public string? CikisOfisi { get; set; }
    }

    private static async Task<Ok<Sayfa<SurveyRow>>> ListSurveys(
        [AsParameters] SurveyListFilter f, SurveyService surveys, ICurrentUser user, IDbContextFactory<AppDbContext> dbf,
        ILocationRepository locations, int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        var (min, max) = F5Shared.DayRange(f.TarihBas, f.TarihBit);
        var items = await surveys.SearchAsync(new AnketFilter
        {
            CariId = f.CariId, AnketTuru = F5Shared.EnumAdi<SurveyType>(f.AnketTuru, "anketTuru"),
            Durum = F5Shared.EnumAdi<SurveyStatus>(f.Durum, "durum"), TarihMin = min, TarihMax = max,
            CikisOfisi = F5Shared.Nz(f.CikisOfisi),
        }, ct);
        var inScope = await CrmScope.BuildAsync(user, dbf, locations, items.Select(a => (a.RentalId, a.CikisOfisi)), ct);
        var visible = items.Where(a => inScope(a.RentalId, a.CikisOfisi)).ToList();
        var rows = await SurveyRowsAsync(dbf, visible, ct);
        return TypedResults.Ok(F5Shared.Paginate(rows, SurveySort, sayfa, boyut, sirala));
    }

    private static async Task<List<SurveyRow>> SurveyRowsAsync(IDbContextFactory<AppDbContext> dbf, IReadOnlyList<Anket> items, CancellationToken ct)
    {
        var customers = await F5Shared.CustomersAsync(dbf, items.Where(a => a.CariId is not null).Select(a => a.CariId!.Value), ct);
        var contracts = await ContractNumbersAsync(dbf, items.Select(a => a.RentalId), ct);
        return items.Select(a => new SurveyRow(
            a.Id, a.Tarih, a.CariId, a.CariId is { } c ? F5Shared.CustomerName(customers, c) : null, a.RentalId,
            a.RentalId is { } r ? contracts.GetValueOrDefault(r) : null, a.AnketTuru?.ToString(), a.Durum.ToString(), a.Puan,
            a.Kaynak, a.CikisOfisi, a.Yorum)).ToList();
    }

    private static async Task<Dictionary<Guid, string>> ContractNumbersAsync(
        IDbContextFactory<AppDbContext> dbf, IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0) return [];
        await using var db = await dbf.CreateDbContextAsync(ct);
        return await db.Rentals.AsNoTracking().Where(r => list.Contains(r.Id))
            .Select(r => new { r.Id, r.SozlesmeNo }).ToDictionaryAsync(r => r.Id, r => r.SozlesmeNo, ct);
    }

    private static async Task<Results<Ok<SurveyCardDto>, ProblemHttpResult>> GetSurvey(
        Guid id, SurveyService surveys, ICurrentUser user, IDbContextFactory<AppDbContext> dbf, ILocationRepository locations,
        CancellationToken ct)
        => await SurveyCardAsync(id, surveys, user, dbf, locations, ct) is { } c ? TypedResults.Ok(c) : SurveyNotFound();

    /// <summary>Kart: sürüm ÖNCE; kapsam dışı → 403 (kapsam kontrolü içerik dönmeden).</summary>
    private static async Task<SurveyCardDto?> SurveyCardAsync(
        Guid id, SurveyService surveys, ICurrentUser user, IDbContextFactory<AppDbContext> dbf, ILocationRepository locations,
        CancellationToken ct)
    {
        var version = await surveys.GetVersionAsync(id, ct);
        var d = await surveys.GetDetailAsync(id, ct);
        if (d is null) return null;
        await CrmScope.RequireAsync(user, dbf, locations, d.Anket.RentalId, d.Anket.CikisOfisi, ct);
        var row = (await SurveyRowsAsync(dbf, [d.Anket], ct))[0];
        return new SurveyCardDto(row, version,
            d.Cevaplar.Select(c => new SurveyAnswerDto(c.SoruNo, c.Soru, c.Cevap, c.Aciklama)).ToList());
    }

    private static AnketInput SurveyInput(SurveyRequest r)
    {
        RentalLimits.Text(r.Yorum, 1024, "yorum", "Yorum");
        RentalLimits.Text(r.Kaynak, 64, "kaynak", "Kaynak");
        RentalLimits.Text(r.CikisOfisi, 128, "cikisOfisi", "Çıkış ofisi");
        var answers = r.Cevaplar ?? [];
        if (answers.Count > MaxAnswers) throw new ValidationException($"En fazla {MaxAnswers} soru girilebilir.", "cevaplar");
        foreach (var a in answers)
        {
            RentalLimits.Text(a.Soru, 512, "cevaplar", "Soru");
            RentalLimits.Text(a.Cevap, 1024, "cevaplar", "Cevap");
            RentalLimits.Text(a.Aciklama, 1024, "cevaplar", "Açıklama");
        }
        return new AnketInput
        {
            CariId = r.CariId, RentalId = r.RentalId == Guid.Empty ? null : r.RentalId, Puan = r.Puan, Yorum = r.Yorum,
            Tarih = F5Shared.Utc(r.Tarih), Kaynak = r.Kaynak,
            AnketTuru = F5Shared.EnumAdi<SurveyType>(r.AnketTuru, "anketTuru"),
            Durum = F5Shared.EnumAdi<SurveyStatus>(r.Durum, "durum") ?? SurveyStatus.Yapildi,
            CikisOfisi = r.CikisOfisi,
            Cevaplar = answers.Select(a => new AnketCevapInput { SoruNo = a.SoruNo, Soru = a.Soru, Cevap = a.Cevap, Aciklama = a.Aciklama }).ToList(),
        };
    }

    private static async Task<Results<Created<SurveyCardDto>, ProblemHttpResult>> CreateSurvey(
        SurveyRequest request, SurveyService surveys, RentalService rentals, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, ILocationRepository locations, CancellationToken ct)
    {
        var input = SurveyInput(request);
        await CrmScope.RequireCustomerAsync(dbf, input.CariId, "cariId", ct);
        var id = await surveys.CreateAsync(input, ct); // hedef kapsamı serviste (CrmScopeGuard)
        return await SurveyCardAsync(id, surveys, user, dbf, locations, ct) is { } c
            ? TypedResults.Created($"{UiApiExtensions.V1}/anketler/{id}", c) : SurveyNotFound();
    }

    /// <summary>Tam değiştirme. Sıra: varlık (404) → mevcut kaydın kapsamı (403) → surum → girdi/hedef → kilit altında sürüm (409).</summary>
    private static async Task<Results<Ok<SurveyCardDto>, ProblemHttpResult>> UpdateSurvey(
        Guid id, SurveyUpdateRequest request, SurveyService surveys, RentalService rentals, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, ILocationRepository locations, CancellationToken ct)
    {
        var current = await surveys.GetAsync(id, ct);
        if (current is null) return SurveyNotFound();
        await CrmScope.RequireAsync(user, dbf, locations, current.RentalId, current.CikisOfisi, ct);
        if (string.IsNullOrWhiteSpace(request.Surum))
            throw new ValidationException("Kayıt sürümü (surum) zorunludur; kaydı yeniden açın.", "surum");
        var input = SurveyInput(request);
        await CrmScope.RequireCustomerAsync(dbf, input.CariId, "cariId", ct);
        // Bağ korunması + hedef kapsamı serviste (CrmScopeGuard; Blazor yoluyla aynı kural).
        if (!await surveys.UpdateAsync(id, input, request.Surum, ct)) return SurveyNotFound();
        return await SurveyCardAsync(id, surveys, user, dbf, locations, ct) is { } c ? TypedResults.Ok(c) : SurveyNotFound();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteSurvey(
        Guid id, SurveyService surveys, ICurrentUser user, IDbContextFactory<AppDbContext> dbf, ILocationRepository locations,
        CancellationToken ct)
    {
        var current = await surveys.GetAsync(id, ct);
        if (current is null) return SurveyNotFound();
        await CrmScope.RequireAsync(user, dbf, locations, current.RentalId, current.CikisOfisi, ct);
        return await surveys.DeleteAsync(id, ct) ? TypedResults.NoContent() : SurveyNotFound();
    }
}
