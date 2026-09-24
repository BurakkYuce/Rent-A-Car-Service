using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Crm;
using RentACar.Application.Locations;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Kira;
using RentACar.Web.Api.Rezervasyon;

namespace RentACar.Web.Api.Cari;

/// <summary>
/// <c>/api/ui/v1/assistans-talepleri/*</c> — yol yardım talebi (Blazor <c>AssistansTalepList</c>). İzin OperationsWrite;
/// şube kapsamı <see cref="CrmScope"/> (bağlı kiranın kapsamı). Ad/telefon SNAPSHOT'tır; bağlı kiranın müşterisi KVKK ile
/// anonimleştirilmişse yanıtta gizlenir (snapshot o müşteriden kopyalanmış olabilir).
/// </summary>
public static partial class CrmApi
{
    private static readonly (string, string)[] AssistanceFieldRules =
        [("Mesaj zorunludur", "mesaj"), ("Assistans talebi tarihi", "zaman"), ("Kira sözleşmesi bulunamadı", "rentalId")];

    private static void MapAssistance(RouteGroupBuilder g)
    {
        var s = g.MapGroup("/assistans-talepleri").WithTags("CRM");
        s.MapGet("", ListAssistance).AlanlariEsle(F5Ortak.SiralamaKurallari);
        s.MapGet("/{id:guid}", GetAssistance);
        s.MapPost("", CreateAssistance).AlanlariEsle(AssistanceFieldRules);
        s.MapPut("/{id:guid}", UpdateAssistance).AlanlariEsle(AssistanceFieldRules);
        s.MapDelete("/{id:guid}", DeleteAssistance);
    }

    private static ProblemHttpResult AssistanceNotFound() => F5Ortak.Bulunamadi("Assistans talebi bulunamadı.");

    private static readonly SiralamaHaritasi<AssistanceRow> AssistanceSort = SiralamaHaritasi<AssistanceRow>
        .Olustur(r => r.Id)
        .Alan("zaman", r => r.Zaman).Alan("plaka", r => r.Plaka).Alan("kapandi", r => r.Kapandi)
        .Alan("sozlesmeNo", r => r.SozlesmeNo).Alan("sebep", r => r.Sebep);

    public sealed class AssistanceListFilter
    {
        [FromQuery(Name = "plaka")] public string? Plaka { get; set; }
        [FromQuery(Name = "tarihBas")] public DateOnly? TarihBas { get; set; }
        [FromQuery(Name = "tarihBit")] public DateOnly? TarihBit { get; set; }
        /// <summary>Mesaj / sebep / ad / telefon içinde geçen metin.</summary>
        [FromQuery(Name = "ara")] public string? Ara { get; set; }
        [FromQuery(Name = "kapandi")] public bool? Kapandi { get; set; }
        [FromQuery(Name = "yedekLastik")] public bool? YedekLastik { get; set; }
        /// <summary>true → yalnız hareket EDEMEYEN (çekici gereken) araçlar.</summary>
        [FromQuery(Name = "hareketEdemiyor")] public bool? HareketEdemiyor { get; set; }
    }

    private static async Task<Ok<Sayfa<AssistanceRow>>> ListAssistance(
        [AsParameters] AssistanceListFilter f, AssistansTalepService requests, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, ILocationRepository locations, int? sayfa, int? boyut, string? sirala,
        CancellationToken ct)
    {
        Sinirlar.Metin(f.Ara, 100, "ara", "Arama metni");
        Sinirlar.Metin(f.Plaka, 32, "plaka", "Plaka");
        var (min, max) = F5Ortak.GunAraligi(f.TarihBas, f.TarihBit);
        var items = await requests.SearchAsync(new AssistansFilter
        {
            Plaka = F5Ortak.Nz(f.Plaka), TarihMin = min, TarihMax = max, Ara = F5Ortak.Nz(f.Ara), Kapandi = f.Kapandi,
            YedekLastikMi = f.YedekLastik, HareketEdemiyor = f.HareketEdemiyor,
        }, ct);
        var inScope = await CrmScope.BuildAsync(user, dbf, locations, items.Select(a => (a.RentalId, (string?)null)), ct);
        var rows = await AssistanceRowsAsync(dbf, items.Where(a => inScope(a.RentalId, null)).ToList(), ct);
        return TypedResults.Ok(F5Ortak.Sayfala(rows, AssistanceSort, sayfa, boyut, sirala));
    }

    private static async Task<List<AssistanceRow>> AssistanceRowsAsync(
        IDbContextFactory<AppDbContext> dbf, IReadOnlyList<AssistansTalep> items, CancellationToken ct)
    {
        var ids = items.Where(a => a.RentalId is not null).Select(a => a.RentalId!.Value).Distinct().ToList();
        Dictionary<Guid, (string No, bool HideName, bool HidePhone)> rentals = [];
        if (ids.Count > 0)
        {
            await using var db = await dbf.CreateDbContextAsync(ct);
            var list = await (from r in db.Rentals.AsNoTracking()
                              join c in db.Customers.AsNoTracking() on r.MusteriId equals c.Id into cg
                              from c in cg.DefaultIfEmpty()
                              where ids.Contains(r.Id)
                              select new { r.Id, r.SozlesmeNo, HideName = c != null && c.AnonimAd, HidePhone = c != null && c.AnonimTelefon })
                .ToListAsync(ct);
            rentals = list.ToDictionary(x => x.Id, x => (x.SozlesmeNo, x.HideName, x.HidePhone));
        }
        return items.Select(a =>
        {
            var link = a.RentalId is { } id && rentals.TryGetValue(id, out var v) ? v : default;
            return new AssistanceRow(a.Id, a.Zaman, a.RentalId, link.No, a.Plaka, link.HideName ? null : a.AdSoyad,
                link.HidePhone ? null : a.CepTel, a.Mesaj, a.Sebep, a.YedekLastikMi, a.AracHareketMi, a.Kapandi, a.Cozum);
        }).ToList();
    }

    private static async Task<Results<Ok<AssistanceCardDto>, ProblemHttpResult>> GetAssistance(
        Guid id, AssistansTalepService requests, ICurrentUser user, IDbContextFactory<AppDbContext> dbf,
        ILocationRepository locations, CancellationToken ct)
        => await AssistanceCardAsync(id, requests, user, dbf, locations, ct) is { } c ? TypedResults.Ok(c) : AssistanceNotFound();

    private static async Task<AssistanceCardDto?> AssistanceCardAsync(
        Guid id, AssistansTalepService requests, ICurrentUser user, IDbContextFactory<AppDbContext> dbf,
        ILocationRepository locations, CancellationToken ct)
    {
        var version = await requests.GetVersionAsync(id, ct);
        var a = await requests.GetAsync(id, ct);
        if (a is null) return null;
        await CrmScope.RequireAsync(user, dbf, locations, a.RentalId, null, ct);
        return new AssistanceCardDto((await AssistanceRowsAsync(dbf, [a], ct))[0], version);
    }

    private static async Task<AssistansInput> AssistanceInputAsync(
        AssistanceRequest r, ICurrentUser user, RentalService rentals, ILocationRepository locations, CancellationToken ct)
    {
        Sinirlar.Metin(r.Plaka, 32, "plaka", "Plaka");
        Sinirlar.Metin(r.AdSoyad, 256, "adSoyad", "Ad soyad");
        Sinirlar.Metin(r.CepTel, 32, "cepTel", "Cep telefonu");
        Sinirlar.Metin(r.Mesaj, 2048, "mesaj", "Mesaj");
        Sinirlar.Metin(r.Sebep, 512, "sebep", "Sebep");
        Sinirlar.Metin(r.Cozum, 1024, "cozum", "Çözüm");
        if (r.Plaka is { } p && AssistansTalepService.PlakaNormalize(p).Length > 16)
            throw new ValidationException("Plaka en fazla 16 karakter olabilir.", "plaka");
        var rentalId = r.RentalId == Guid.Empty ? null : r.RentalId;
        await CrmScope.RequireTargetAsync(user, rentals, locations, rentalId, null, ct);
        return new AssistansInput
        {
            RentalId = rentalId, Plaka = r.Plaka, AdSoyad = r.AdSoyad, CepTel = r.CepTel, Zaman = F5Ortak.Utc(r.Zaman),
            Mesaj = r.Mesaj, Sebep = r.Sebep, YedekLastikMi = r.YedekLastikMi, AracHareketMi = r.AracHareketMi,
            Kapandi = r.Kapandi, Cozum = r.Cozum,
            // #295 L1: null = dokunma (boşsa sözleşmeden doldurulur), "" = temizle (yeniden doldurulmaz).
            ClearContactName = r.AdSoyad is not null && string.IsNullOrWhiteSpace(r.AdSoyad),
            ClearContactPhone = r.CepTel is not null && string.IsNullOrWhiteSpace(r.CepTel),
        };
    }

    private static async Task<Results<Created<AssistanceCardDto>, ProblemHttpResult>> CreateAssistance(
        AssistanceRequest request, AssistansTalepService requests, RentalService rentals, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, ILocationRepository locations, CancellationToken ct)
    {
        var input = await AssistanceInputAsync(request, user, rentals, locations, ct);
        var id = await requests.CreateAsync(input, ct);
        return await AssistanceCardAsync(id, requests, user, dbf, locations, ct) is { } c
            ? TypedResults.Created($"{UiApiExtensions.V1}/assistans-talepleri/{id}", c) : AssistanceNotFound();
    }

    private static async Task<Results<Ok<AssistanceCardDto>, ProblemHttpResult>> UpdateAssistance(
        Guid id, AssistanceUpdateRequest request, AssistansTalepService requests, RentalService rentals, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, ILocationRepository locations, CancellationToken ct)
    {
        var current = await requests.GetAsync(id, ct);
        if (current is null) return AssistanceNotFound();
        await CrmScope.RequireAsync(user, dbf, locations, current.RentalId, null, ct);
        if (string.IsNullOrWhiteSpace(request.Surum))
            throw new ValidationException("Kayıt sürümü (surum) zorunludur; kaydı yeniden açın.", "surum");
        CrmScope.RequireBranchKept(user, current.RentalId, null, request.RentalId, null);
        var input = await AssistanceInputAsync(request, user, rentals, locations, ct);
        KeepStoredContact(current, input);
        if (!await requests.UpdateAsync(id, input, request.Surum, ct)) return AssistanceNotFound();
        return await AssistanceCardAsync(id, requests, user, dbf, locations, ct) is { } c ? TypedResults.Ok(c) : AssistanceNotFound();
    }

    /// <summary>
    /// KVKK: anonim müşteriye bağlı talepte ad/telefon yanıtta <c>null</c> döner; tam PUT bu <c>null</c>'ı geri
    /// gönderince kayıtlı değer silinmemeli ya da servis onu anonim müşterinin kartından yeniden doldurmamalı.
    /// Sözleşme (#295b, TC/ehliyet ile aynı): <c>""</c> = temizle (<see cref="AssistansInput.ClearContactName"/>);
    /// <c>null</c> = dokunma — YALNIZ kira değişmediyse saklı değer korunur (temizlenmişse temiz kalır).
    /// Kira değiştiyse ya da kaldırıldıysa saklı değer TAŞINMAZ: gizli değer yalnız bağlı kiranın müşterisi anonim olduğu
    /// için gizliydi; yeni bağla (ya da bağsız) düz görünür ve ManageUsers kapısı atlanırdı. O durumda <c>null</c> alanı
    /// boşaltır; servis yalnız YENİ kiranın görünür müşterisinden doldurabilir (anonim müşteriden asla).
    /// </summary>
    private static void KeepStoredContact(AssistansTalep current, AssistansInput input)
    {
        var requested = input.RentalId == Guid.Empty ? null : input.RentalId;
        if (requested != current.RentalId) return;
        if (input.AdSoyad is null && !input.ClearContactName) input.AdSoyad = current.AdSoyad;
        if (input.CepTel is null && !input.ClearContactPhone) input.CepTel = current.CepTel;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAssistance(
        Guid id, AssistansTalepService requests, ICurrentUser user, IDbContextFactory<AppDbContext> dbf,
        ILocationRepository locations, CancellationToken ct)
    {
        var current = await requests.GetAsync(id, ct);
        if (current is null) return AssistanceNotFound();
        await CrmScope.RequireAsync(user, dbf, locations, current.RentalId, null, ct);
        return await requests.DeleteAsync(id, ct) ? TypedResults.NoContent() : AssistanceNotFound();
    }
}
