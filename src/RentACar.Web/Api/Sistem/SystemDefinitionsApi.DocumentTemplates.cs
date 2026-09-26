using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.BelgeSablon;
using RentACar.Application.Common;
using RentACar.Application.ReservationSources;
using RentACar.Domain.Enums;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Sistem;

/// <summary>
/// Belge şablonları (marka-özel PDF metinleri) — ManageUsers (Blazor uç + servis paritesi). Metinler DÜZ METİNDİR:
/// PDF'e QuestPDF metni olarak basılır, HTML olarak yorumlanmaz; SPA da metin olarak göstermeli (innerHTML YASAK).
/// Rezervasyon kaynağı "Aşağıya Yansıt" da burada (temel CRUD F11.1a tabanında).
/// </summary>
public static partial class SystemDefinitionsApi
{
    private static readonly (string, string)[] TemplateRules =
    [
        ("Geçersiz belge türü", "belgeTuru"), ("Şablon adı", "ad"), ("'", "ad"), ("Belge başlığı", "belgeBasligi"),
        ("Metin bölümü", "hukukiMetinSol"), ("Alt bilgi", "altBilgi"),
    ];

    private static readonly SortFieldMap<DocumentTemplateDto> TemplateSort = SortFieldMap<DocumentTemplateDto>
        .Create(x => x.Id).Alan("belgeTuru", x => x.BelgeTuru).Alan("ad", x => x.Ad).Alan("aktif", x => x.Aktif);

    private static void MapDocumentTemplates(RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/belge-sablonlari").WithTags(SystemApiCommon.DefinitionsTag).RequirePermission(Permission.ManageUsers);
        g.MapGet("", async (int? sayfa, int? boyut, string? sirala, string? tur, DocumentTemplateService s, CancellationToken ct) =>
        {
            var kind = EnumName<BelgeTuru>(tur, "tur");
            var rows = kind is { } k ? await s.ListByTypeAsync(k, ct) : await s.ListAsync(ct);
            return TypedResults.Ok(F5Shared.Paginate(rows.Select(x => DocumentTemplateDto.From(x, null)).ToList(), TemplateSort, sayfa, boyut, sirala));
        }).MapFields(F5Shared.SortRules);
        g.MapGet("/{id:guid}", async Task<Results<Ok<DocumentTemplateDto>, ProblemHttpResult>> (Guid id, DocumentTemplateService s, CancellationToken ct)
            => await TemplateAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound());
        g.MapPost("", async Task<Results<Created<DocumentTemplateDto>, ProblemHttpResult>> (DocumentTemplateRequest i, DocumentTemplateService s, CancellationToken ct) =>
        {
            var id = await s.CreateAsync(TemplateInput(i), ct);
            return await TemplateAsync(id, s, ct) is { } d ? TypedResults.Created($"{UiApiExtensions.V1}/belge-sablonlari/{id}", d) : SystemApiCommon.NotFound();
        }).MapFields(TemplateRules);
        g.MapPut("/{id:guid}", async Task<Results<Ok<DocumentTemplateDto>, ProblemHttpResult>> (Guid id, DocumentTemplateRequest i, DocumentTemplateService s, CancellationToken ct) =>
        {
            if (await s.GetAsync(id, ct) is null) return SystemApiCommon.NotFound();
            SystemApiCommon.RequireVersion(i.Surum);
            if (!await s.UpdateAsync(id, TemplateInput(i), i.Surum, ct)) return SystemApiCommon.NotFound();
            return await TemplateAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : SystemApiCommon.NotFound();
        }).MapFields(TemplateRules);
        g.MapDelete("/{id:guid}", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, DocumentTemplateService s, CancellationToken ct)
            => await s.DeleteAsync(id, ct) ? TypedResults.NoContent() : SystemApiCommon.NotFound());
    }

    /// <summary>Belge türü ZORUNLU ve adla (Blazor'daki "tanınmazsa KiraSozlesmesi" sessiz varsayılanı yeni yüzeyde yok).
    /// Uzunluk sınırları servisle aynı (Validate) — burada ayrıca yazılmaz.</summary>
    private static BelgeSablonInput TemplateInput(DocumentTemplateRequest i) => new()
    {
        BelgeTuru = EnumName<BelgeTuru>(i.BelgeTuru, "belgeTuru")
            ?? throw new ValidationException("Belge türü zorunludur.", "belgeTuru"),
        Ad = i.Ad ?? "", VarsayilanMi = i.VarsayilanMi, Aktif = i.Aktif, BelgeBasligi = i.BelgeBasligi,
        HukukiMetinSol = i.HukukiMetinSol, HukukiMetinSag = i.HukukiMetinSag, EkKosullarVarsayilan = i.EkKosullarVarsayilan,
        AltBilgi = i.AltBilgi, ImzaAlaniGoster = i.ImzaAlaniGoster,
    };

    private static async Task<DocumentTemplateDto?> TemplateAsync(Guid id, DocumentTemplateService s, CancellationToken ct)
    {
        var version = await s.RowVersionAsync(id, ct);
        return await s.GetAsync(id, ct) is { } x ? DocumentTemplateDto.From(x, version) : null;
    }

    // ------------------------------------------------------------------ rezervasyon kaynağı: aşağıya yansıt

    /// <summary>
    /// Seçili kaynağın kira/hizmet/drop oranlarını diğer AKTİF kaynaklara kopyalar (Blazor <c>/yansit</c>). Yalnız
    /// kaynak tablosuna yazar; kayıtlı rezervasyon/fatura/defter DEĞİŞMEZ. OperationsWrite. Başka kiracının ya da
    /// olmayan kaynağın kimliği 404 (RLS kapsamlı varlık kontrolü servisten ÖNCE).
    /// </summary>
    private static void MapReservationSourceExtras(RouteGroupBuilder v1)
    {
        v1.MapPost("/rezervasyon-kaynaklari/{id:guid}/yansit",
                async Task<Results<Ok<ReflectRatesResult>, ProblemHttpResult>> (Guid id, ReservationSourceService s, CancellationToken ct) =>
                {
                    if (await s.GetAsync(id, ct) is null) return SystemApiCommon.NotFound();
                    return TypedResults.Ok(new ReflectRatesResult(await s.ReflectRatesAsync(id, ct)));
                })
            .WithTags(SystemApiCommon.DefinitionsTag).RequirePermission(Permission.OperationsWrite);
    }
}
