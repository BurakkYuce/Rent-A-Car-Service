using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Application.Legal;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Kira;
using RentACar.Web.Api.Rezervasyon;

namespace RentACar.Web.Api.Cari;

/// <summary>
/// <c>/api/ui/v1/hukuk-dosyalari/*</c> — hukuk dosyası (Blazor <c>HukukList</c>). İzin OperationsWrite. Şube kolonu yok
/// (cariye bağlı, firma geneli). Tutar/tahsilat BİLGİ alanıdır (deftere yazmaz — servis çiti). Müşteri adı/telefonu
/// <see cref="F5Shared.CustomersAsync"/> ile KVKK kuralından geçer.
/// </summary>
public static partial class CrmApi
{
    private static readonly (string, string)[] LegalFieldRules =
    [
        ("Dosya no", "dosyaNo"), ("'", "dosyaNo"), ("Tutar", "tutar"), ("Tahsilat", "tahsilat"), ("Fatura no", "faturaNoTemp"),
        ("2. avukat", "avukat2Ad"), ("Telefon", "avukatTel"), ("E-posta", "avukatMail"),
    ];

    private static void MapLegalFiles(RouteGroupBuilder g)
    {
        var s = g.MapGroup("/hukuk-dosyalari").WithTags("CRM");
        s.MapGet("", ListLegalFiles).MapFields(F5Shared.SortRules);
        s.MapGet("/{id:guid}", GetLegalFile);
        s.MapPost("", CreateLegalFile).MapFields(LegalFieldRules);
        s.MapPut("/{id:guid}", UpdateLegalFile).MapFields(LegalFieldRules);
        s.MapDelete("/{id:guid}", DeleteLegalFile);
    }

    private static ProblemHttpResult LegalNotFound() => F5Shared.NotFound("Hukuk dosyası bulunamadı.");

    private static readonly SortFieldMap<LegalFileRow> LegalSort = SortFieldMap<LegalFileRow>
        .Create(r => r.Id)
        .Alan("dosyaNo", r => r.DosyaNo).Alan("tarih", r => r.Tarih).Alan("tur", r => r.Tur).Alan("durum", r => r.Durum)
        .Alan("tutar", r => r.Tutar).Alan("tahsilat", r => r.Tahsilat).Alan("kalan", r => r.Kalan).Alan("avukat", r => r.Avukat);

    public sealed class LegalFileListFilter
    {
        [FromQuery(Name = "cariId")] public Guid? CariId { get; set; }
        [FromQuery(Name = "tarihBas")] public DateOnly? TarihBas { get; set; }
        [FromQuery(Name = "tarihBit")] public DateOnly? TarihBit { get; set; }
        [FromQuery(Name = "faturaNo")] public string? FaturaNo { get; set; }
        [FromQuery(Name = "dosyaNo")] public string? DosyaNo { get; set; }
        /// <summary>Avukat adı / açıklama içinde geçen metin.</summary>
        [FromQuery(Name = "ara")] public string? Ara { get; set; }
        [FromQuery(Name = "tur")] public string? Tur { get; set; }
        [FromQuery(Name = "durum")] public string? Durum { get; set; }
    }

    private static async Task<Ok<Sayfa<LegalFileRow>>> ListLegalFiles(
        [AsParameters] LegalFileListFilter f, LegalCaseService files, IDbContextFactory<AppDbContext> dbf,
        int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        RentalLimits.Text(f.Ara, 100, "ara", "Arama metni");
        RentalLimits.Text(f.FaturaNo, 64, "faturaNo", "Fatura no");
        RentalLimits.Text(f.DosyaNo, 64, "dosyaNo", "Dosya no");
        var (min, max) = F5Shared.DayRange(f.TarihBas, f.TarihBit);
        var items = await files.SearchAsync(new HukukDosyaFilter
        {
            CariId = f.CariId, Bas = min, Bit = max, FaturaNo = F5Shared.Nz(f.FaturaNo), DosyaNo = F5Shared.Nz(f.DosyaNo),
            Ara = F5Shared.Nz(f.Ara), Tur = F5Shared.EnumAdi<LegalType>(f.Tur, "tur"),
            Durum = F5Shared.EnumAdi<LegalStatus>(f.Durum, "durum"), EnFazla = 10_000,
        }, ct);
        var rows = await LegalRowsAsync(dbf, items.Select(x => x.Dosya).ToList(), ct);
        return TypedResults.Ok(F5Shared.Paginate(rows, LegalSort, sayfa, boyut, sirala));
    }

    private static async Task<List<LegalFileRow>> LegalRowsAsync(
        IDbContextFactory<AppDbContext> dbf, IReadOnlyList<HukukDosya> items, CancellationToken ct)
    {
        var customers = await F5Shared.CustomersAsync(dbf, items.Where(h => h.CariId is not null).Select(h => h.CariId!.Value), ct);
        return items.Select(h =>
        {
            var c = h.CariId is { } id && customers.TryGetValue(id, out var v) ? v : null;
            return new LegalFileRow(h.Id, h.DosyaNo, h.Tarih, h.Tur.ToString(), h.Durum.ToString(), h.Aktif, h.CariId, c?.Ad,
                c?.CepTel, h.Avukat, h.AvukatTel, h.AvukatMail, h.Avukat2Ad, h.Avukat2Tel, h.Avukat2Mail, h.Tutar, h.Tahsilat,
                h.Kalan, h.FaturaNoTemp, h.Aciklama);
        }).ToList();
    }

    private static async Task<Results<Ok<LegalFileCardDto>, ProblemHttpResult>> GetLegalFile(
        Guid id, LegalCaseService files, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
        => await LegalCardAsync(id, files, dbf, ct) is { } c ? TypedResults.Ok(c) : LegalNotFound();

    private static async Task<LegalFileCardDto?> LegalCardAsync(Guid id, LegalCaseService files, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var version = await files.GetVersionAsync(id, ct);
        var h = await files.GetAsync(id, ct);
        return h is null ? null : new LegalFileCardDto((await LegalRowsAsync(dbf, [h], ct))[0], version);
    }

    private static async Task<HukukDosyaInput> LegalInputAsync(LegalFileRequest r, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        RentalLimits.Text(r.DosyaNo, 64, "dosyaNo", "Dosya no");
        RentalLimits.Text(r.Avukat, 128, "avukat", "Avukat");
        RentalLimits.Text(r.Aciklama, 1024, "aciklama", "Açıklama");
        RentalLimits.Text(r.FaturaNoTemp, 64, "faturaNoTemp", "Fatura no");
        RentalLimits.Text(r.AvukatTel, 32, "avukatTel", "Avukat telefonu");
        RentalLimits.Text(r.AvukatMail, 256, "avukatMail", "Avukat e-postası");
        RentalLimits.Text(r.Avukat2Ad, 128, "avukat2Ad", "2. avukat adı");
        RentalLimits.Text(r.Avukat2Tel, 32, "avukat2Tel", "2. avukat telefonu");
        RentalLimits.Text(r.Avukat2Mail, 256, "avukat2Mail", "2. avukat e-postası");
        RentalLimits.Amount(r.Tutar, "tutar", "Tutar");
        RentalLimits.Amount(r.Tahsilat, "tahsilat", "Tahsilat");
        await CrmScope.RequireCustomerAsync(dbf, r.CariId, "cariId", ct);
        return new HukukDosyaInput
        {
            DosyaNo = r.DosyaNo, CariId = r.CariId, Tur = F5Shared.EnumAdi<LegalType>(r.Tur, "tur") ?? LegalType.Dava,
            Avukat = r.Avukat, Tutar = r.Tutar, Durum = F5Shared.EnumAdi<LegalStatus>(r.Durum, "durum") ?? LegalStatus.Acik,
            Tarih = F5Shared.Utc(r.Tarih), Aciklama = r.Aciklama, Aktif = r.Aktif, FaturaNoTemp = r.FaturaNoTemp,
            AvukatTel = r.AvukatTel, AvukatMail = r.AvukatMail, Avukat2Ad = r.Avukat2Ad, Avukat2Tel = r.Avukat2Tel,
            Avukat2Mail = r.Avukat2Mail, Tahsilat = r.Tahsilat,
        };
    }

    private static async Task<Results<Created<LegalFileCardDto>, ProblemHttpResult>> CreateLegalFile(
        LegalFileRequest request, LegalCaseService files, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var id = await files.CreateAsync(await LegalInputAsync(request, dbf, ct), ct);
        return await LegalCardAsync(id, files, dbf, ct) is { } c
            ? TypedResults.Created($"{UiApiExtensions.V1}/hukuk-dosyalari/{id}", c) : LegalNotFound();
    }

    private static async Task<Results<Ok<LegalFileCardDto>, ProblemHttpResult>> UpdateLegalFile(
        Guid id, LegalFileUpdateRequest request, LegalCaseService files, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        if (await files.GetAsync(id, ct) is null) return LegalNotFound();
        if (string.IsNullOrWhiteSpace(request.Surum))
            throw new ValidationException("Kayıt sürümü (surum) zorunludur; kaydı yeniden açın.", "surum");
        if (!await files.UpdateAsync(id, await LegalInputAsync(request, dbf, ct), request.Surum, ct)) return LegalNotFound();
        return await LegalCardAsync(id, files, dbf, ct) is { } c ? TypedResults.Ok(c) : LegalNotFound();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteLegalFile(Guid id, LegalCaseService files, CancellationToken ct)
        => await files.DeleteAsync(id, ct) ? TypedResults.NoContent() : LegalNotFound();
}
