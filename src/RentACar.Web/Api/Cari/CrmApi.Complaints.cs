using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Crm;
using RentACar.Application.Locations;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Kira;
using RentACar.Web.Api.Rezervasyon;

namespace RentACar.Web.Api.Cari;

/// <summary>
/// <c>/api/ui/v1/sikayetler/*</c> — müşteri şikayeti (Blazor <c>SikayetList</c>). İzin OperationsWrite; şube kapsamı
/// <see cref="CrmScope"/>. Müşteri adı/telefonu repodan DEĞİL <see cref="F5Ortak.CarilerAsync"/>'ten (KVKK tek kural —
/// repo projeksiyonu <c>Anonim*</c> bayraklarını uygulamıyor).
/// </summary>
public static partial class CrmApi
{
    private static readonly (string, string)[] ComplaintFieldRules =
        [("Konu", "konu"), ("Kira sözleşmesi bulunamadı", "rentalId")];

    private static void MapComplaints(RouteGroupBuilder g)
    {
        var s = g.MapGroup("/sikayetler").WithTags("CRM");
        s.MapGet("", ListComplaints).AlanlariEsle(F5Ortak.SiralamaKurallari);
        s.MapGet("/{id:guid}", GetComplaint);
        s.MapPost("", CreateComplaint).AlanlariEsle(ComplaintFieldRules);
        s.MapPut("/{id:guid}", UpdateComplaint).AlanlariEsle(ComplaintFieldRules);
        s.MapDelete("/{id:guid}", DeleteComplaint);
    }

    private static ProblemHttpResult ComplaintNotFound() => F5Ortak.Bulunamadi("Şikayet bulunamadı.");

    private static readonly SiralamaHaritasi<ComplaintRow> ComplaintSort = SiralamaHaritasi<ComplaintRow>
        .Olustur(r => r.Id)
        .Alan("tarih", r => r.Tarih).Alan("konu", r => r.Konu).Alan("durum", r => r.Durum).Alan("puan", r => r.Puan)
        .Alan("sikayetKanali", r => r.SikayetKanali).Alan("cikisOfisi", r => r.CikisOfisi).Alan("sozlesmeNo", r => r.SozlesmeNo)
        .Alan("plaka", r => r.Plaka);

    public sealed class ComplaintListFilter
    {
        [FromQuery(Name = "cariId")] public Guid? CariId { get; set; }
        [FromQuery(Name = "ofis")] public string? Ofis { get; set; }
        /// <summary><c>Kira</c> | <c>Rezervasyon</c>.</summary>
        [FromQuery(Name = "yer")] public string? Yer { get; set; }
        [FromQuery(Name = "kanal")] public string? Kanal { get; set; }
        /// <summary><c>Acik</c> | <c>Cozuldu</c> | <c>Kapali</c>.</summary>
        [FromQuery(Name = "durum")] public string? Durum { get; set; }
        /// <summary>Konu / detay / sözleşme no / plaka içinde geçen metin.</summary>
        [FromQuery(Name = "ara")] public string? Ara { get; set; }
    }

    private static async Task<Ok<Sayfa<ComplaintRow>>> ListComplaints(
        [AsParameters] ComplaintListFilter f, SikayetService complaints, ICurrentUser user, IDbContextFactory<AppDbContext> dbf,
        ILocationRepository locations, int? sayfa, int? boyut, string? sirala, CancellationToken ct)
    {
        var search = F5Ortak.Nz(f.Ara);
        Sinirlar.Metin(search, 100, "ara", "Arama metni");
        var items = await complaints.SearchAsync(new SikayetFilter
        {
            CariId = f.CariId, Ofis = F5Ortak.Nz(f.Ofis), Yer = F5Ortak.EnumAdi<SikayetYeri>(f.Yer, "yer"),
            Kanal = F5Ortak.Nz(f.Kanal), Durum = F5Ortak.EnumAdi<SikayetDurum>(f.Durum, "durum"), Ara = search, EnFazla = 10_000,
        }, ct);
        var inScope = await CrmScope.BuildAsync(user, dbf, locations, items.Select(x => (x.Sikayet.RentalId, x.Sikayet.CikisOfisi)), ct);
        var visible = items.Where(x => inScope(x.Sikayet.RentalId, x.Sikayet.CikisOfisi)).ToList();
        var rows = await ComplaintRowsAsync(dbf, visible, ct);
        return TypedResults.Ok(F5Ortak.Sayfala(rows, ComplaintSort, sayfa, boyut, sirala));
    }

    private static async Task<List<ComplaintRow>> ComplaintRowsAsync(
        IDbContextFactory<AppDbContext> dbf, IReadOnlyList<SikayetSatirDto> items, CancellationToken ct)
    {
        var customers = await F5Ortak.CarilerAsync(dbf, items.Where(x => x.Sikayet.CariId is not null).Select(x => x.Sikayet.CariId!.Value), ct);
        return items.Select(x =>
        {
            var s = x.Sikayet;
            var c = s.CariId is { } id && customers.TryGetValue(id, out var v) ? v : null;
            return new ComplaintRow(
                s.Id, s.Tarih, s.Konu, s.Detay, s.Durum.ToString(), s.Cozum, s.CariId, c?.Ad, c?.CepTel, s.RentalId,
                x.SozlesmeNo, x.Plaka, s.TeslimAlanPersonelId, x.TeslimAlanAd, s.TeslimEdenPersonelId, x.TeslimEdenAd,
                s.Puan, s.SikayetKanali, s.SikayetYeri?.ToString(), s.CikisOfisi);
        }).ToList();
    }

    private static async Task<Results<Ok<ComplaintCardDto>, ProblemHttpResult>> GetComplaint(
        Guid id, SikayetService complaints, ICurrentUser user, IDbContextFactory<AppDbContext> dbf, ILocationRepository locations,
        CancellationToken ct)
        => await ComplaintCardAsync(id, complaints, user, dbf, locations, ct) is { } c ? TypedResults.Ok(c) : ComplaintNotFound();

    private static async Task<ComplaintCardDto?> ComplaintCardAsync(
        Guid id, SikayetService complaints, ICurrentUser user, IDbContextFactory<AppDbContext> dbf, ILocationRepository locations,
        CancellationToken ct)
    {
        var version = await complaints.GetVersionAsync(id, ct);
        var s = await complaints.GetAsync(id, ct);
        if (s is null) return null;
        await CrmScope.RequireAsync(user, dbf, locations, s.RentalId, s.CikisOfisi, ct);
        // Sözleşme/plaka/personel adları liste satırıyla aynı kaynaktan (kira → araç bağı, personel kaydı).
        string? contract = null, plate = null;
        Dictionary<Guid, string> staff;
        await using (var db = await dbf.CreateDbContextAsync(ct))
        {
            if (s.RentalId is { } rid
                && await db.Rentals.AsNoTracking().Where(r => r.Id == rid).Select(r => new { r.SozlesmeNo, r.VehicleId })
                    .FirstOrDefaultAsync(ct) is { } rental)
            {
                contract = rental.SozlesmeNo;
                plate = await db.Vehicles.AsNoTracking().Where(v => v.Id == rental.VehicleId).Select(v => v.Plaka).FirstOrDefaultAsync(ct);
            }
            var staffIds = new[] { s.TeslimAlanPersonelId, s.TeslimEdenPersonelId }.Where(p => p is not null).Select(p => p!.Value).ToList();
            staff = await db.Personeller.AsNoTracking().Where(p => staffIds.Contains(p.Id))
                .Select(p => new { p.Id, Ad = p.Ad + " " + p.Soyad }).ToDictionaryAsync(p => p.Id, p => p.Ad, ct);
        }
        var dto = new SikayetSatirDto(s, null, null, contract, plate,
            s.TeslimAlanPersonelId is { } a ? staff.GetValueOrDefault(a) : null,
            s.TeslimEdenPersonelId is { } e ? staff.GetValueOrDefault(e) : null);
        return new ComplaintCardDto((await ComplaintRowsAsync(dbf, [dto], ct))[0], version);
    }

    private static async Task<SikayetInput> ComplaintInputAsync(
        ComplaintRequest r, ICurrentUser user, RentalService rentals, IDbContextFactory<AppDbContext> dbf,
        ILocationRepository locations, CancellationToken ct)
    {
        Sinirlar.Metin(r.Konu, 256, "konu", "Konu");
        Sinirlar.Metin(r.Detay, 2048, "detay", "Detay");
        Sinirlar.Metin(r.Cozum, 2048, "cozum", "Çözüm");
        Sinirlar.Metin(r.SikayetKanali, 64, "sikayetKanali", "Şikayet kanalı");
        Sinirlar.Metin(r.CikisOfisi, 128, "cikisOfisi", "Çıkış ofisi");
        if (r.Puan is < 1 or > 5) throw new ValidationException("Puan 1 ile 5 arasında olmalıdır.", "puan");
        TarihPolitikasi.ParaTarihi(r.Tarih, "Şikayet");
        var input = new SikayetInput
        {
            CariId = r.CariId, Konu = r.Konu, Detay = r.Detay,
            Durum = F5Ortak.EnumAdi<SikayetDurum>(r.Durum, "durum") ?? SikayetDurum.Acik,
            Tarih = F5Ortak.Utc(r.Tarih), Cozum = r.Cozum, RentalId = r.RentalId == Guid.Empty ? null : r.RentalId,
            TeslimAlanPersonelId = r.TeslimAlanPersonelId, TeslimEdenPersonelId = r.TeslimEdenPersonelId, Puan = r.Puan,
            SikayetKanali = r.SikayetKanali, SikayetYeri = F5Ortak.EnumAdi<SikayetYeri>(r.SikayetYeri, "sikayetYeri"),
            CikisOfisi = r.CikisOfisi,
        };
        await CrmScope.RequireCustomerAsync(dbf, input.CariId, "cariId", ct);
        await CrmScope.RequireStaffAsync(dbf, input.TeslimAlanPersonelId, "teslimAlanPersonelId", ct);
        await CrmScope.RequireStaffAsync(dbf, input.TeslimEdenPersonelId, "teslimEdenPersonelId", ct);
        // Hedef kapsamı ve bağ korunması serviste (CrmScopeGuard; Blazor yoluyla aynı kural).
        return input;
    }

    private static async Task<Results<Created<ComplaintCardDto>, ProblemHttpResult>> CreateComplaint(
        ComplaintRequest request, SikayetService complaints, RentalService rentals, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, ILocationRepository locations, CancellationToken ct)
    {
        var input = await ComplaintInputAsync(request, user, rentals, dbf, locations, ct);
        var id = await complaints.CreateAsync(input, ct);
        return await ComplaintCardAsync(id, complaints, user, dbf, locations, ct) is { } c
            ? TypedResults.Created($"{UiApiExtensions.V1}/sikayetler/{id}", c) : ComplaintNotFound();
    }

    private static async Task<Results<Ok<ComplaintCardDto>, ProblemHttpResult>> UpdateComplaint(
        Guid id, ComplaintUpdateRequest request, SikayetService complaints, RentalService rentals, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, ILocationRepository locations, CancellationToken ct)
    {
        var current = await complaints.GetAsync(id, ct);
        if (current is null) return ComplaintNotFound();
        await CrmScope.RequireAsync(user, dbf, locations, current.RentalId, current.CikisOfisi, ct);
        if (string.IsNullOrWhiteSpace(request.Surum))
            throw new ValidationException("Kayıt sürümü (surum) zorunludur; kaydı yeniden açın.", "surum");
        var input = await ComplaintInputAsync(request, user, rentals, dbf, locations, ct);
        if (!await complaints.UpdateAsync(id, input, request.Surum, ct)) return ComplaintNotFound();
        return await ComplaintCardAsync(id, complaints, user, dbf, locations, ct) is { } c ? TypedResults.Ok(c) : ComplaintNotFound();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteComplaint(
        Guid id, SikayetService complaints, ICurrentUser user, IDbContextFactory<AppDbContext> dbf, ILocationRepository locations,
        CancellationToken ct)
    {
        var current = await complaints.GetAsync(id, ct);
        if (current is null) return ComplaintNotFound();
        await CrmScope.RequireAsync(user, dbf, locations, current.RentalId, current.CikisOfisi, ct);
        return await complaints.DeleteAsync(id, ct) ? TypedResults.NoContent() : ComplaintNotFound();
    }
}
