using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Personnel;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Api.Sistem;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Rapor;

/// <summary>
/// <c>/api/ui/v1/vardiyalar</c> — F10.3 personel çalışma (vardiya) yazma uçları; Blazor
/// <c>/raporlar/personel-calisma/create|update|delete</c> form uçlarının karşılığı (EKLEMELİ; Blazor uçları kesişe
/// kadar kalır). İş kuralları <see cref="StaffShiftService"/>'te (Blazor ile AYNI yol): personel ve tarih zorunlu,
/// sıfır uzunluk reddedilir, gece vardiyası (bitiş ≤ başlangıç) geçerlidir, aynı personelde kesişen aralık reddedilir,
/// şube tanımlı olmalı.
/// <list type="bullet">
/// <item><b>İzin</b>: okuma ViewReports VEYA OperationsWrite (rapor ekranıyla aynı), yazma OperationsWrite.</item>
/// <item><b>Kapsam</b>: vardiyanın ŞUBESİNE göre (personelin kadrosuna değil). Kapsam dışı kayıt 403, yok ya da başka
/// kiracının kaydı 404. Kapsam, sürüm ve alan doğrulamasından ÖNCE denetlenir.</item>
/// <item><b>Eşzamanlılık</b>: tam değiştirme PUT'unda <c>surum</c> zorunlu; uyuşmazlık 409 <c>cakisma</c>.</item>
/// <item><b>Tarih/saat</b>: <c>tarih</c> takvim günü (saat dilimi yok), saatler <c>SS:dd</c> duvar saati; DB'ye
/// <c>DateTimeOffset</c> gitmez (date + time kolonları), yani UTC dönüşümü gerekmez.</item>
/// </list>
/// </summary>
public static class ShiftApi
{
    public sealed record ShiftRequest(
        Guid? PersonelId, DateOnly? Tarih, string? BaslangicSaat, string? BitisSaat, string? Sube, string? Aciklama,
        string? Surum = null);

    public sealed record ShiftDto(
        Guid Id, Guid PersonelId, string PersonelAd, DateOnly Tarih, string BaslangicSaat, string BitisSaat, int SureDk,
        string Aralik, string? Sube, string? Aciklama, string Surum);

    private static readonly (string, string)[] FieldRules =
    [
        ("Personel", "personelId"), ("Tarih", "tarih"), ("Vardiya süresi", "bitisSaat"), ("Şube", "sube"),
    ];

    private const int BranchMax = 128;
    private const int NoteMax = 512;

    public static RouteGroupBuilder MapShiftApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/vardiyalar").WithTags("Rapor").AlanlariEsle(FieldRules);

        g.MapGet("/{id:guid}", async Task<Results<Ok<ShiftDto>, ProblemHttpResult>> (
                Guid id, StaffShiftService s, CancellationToken ct)
            => await DtoAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : NotFound())
            .RequireAnyPermission(Permission.OperationsWrite, Permission.ViewReports);

        var w = g.MapGroup("").RequirePermission(Permission.OperationsWrite);

        w.MapPost("", async Task<Results<Created<ShiftDto>, ProblemHttpResult>> (
            ShiftRequest i, StaffShiftService s, CancellationToken ct) =>
        {
            var id = await s.CreateAsync(Input(i), ct);
            return await DtoAsync(id, s, ct) is { } d
                ? TypedResults.Created($"{UiApiExtensions.V1}/vardiyalar/{id}", d)
                : NotFound();
        });

        w.MapPut("/{id:guid}", async Task<Results<Ok<ShiftDto>, ProblemHttpResult>> (
            Guid id, ShiftRequest i, StaffShiftService s, CancellationToken ct) =>
        {
            // Scope (403) / existence (404) first: another branch's row must not reveal version or field errors.
            if (await s.GetAsync(id, ct) is null) return NotFound();
            SystemApiCommon.RequireVersion(i.Surum);
            if (!await s.UpdateVersionedAsync(id, Input(i), i.Surum!, ct)) return NotFound();
            return await DtoAsync(id, s, ct) is { } d ? TypedResults.Ok(d) : NotFound();
        });

        w.MapDelete("/{id:guid}", async Task<Results<NoContent, ProblemHttpResult>> (
                Guid id, StaffShiftService s, CancellationToken ct)
            => await s.DeleteAsync(id, ct) ? TypedResults.NoContent() : NotFound());

        return g;
    }

    private static ProblemHttpResult NotFound() => F5Ortak.Bulunamadi("Vardiya bulunamadı.");

    /// <summary>Endpoint limits + parsing; business rules stay in the service (same path as Blazor).</summary>
    private static VardiyaInput Input(ShiftRequest i)
    {
        ReportPeriod.ValidateDay(i.Tarih, "tarih");
        var branch = F5Ortak.Nz(i.Sube);
        if (branch is { Length: > BranchMax })
            throw new ValidationException($"Şube en fazla {BranchMax} karakter olabilir.", "sube");
        var note = F5Ortak.Nz(i.Aciklama);
        if (note is { Length: > NoteMax })
            throw new ValidationException($"Açıklama en fazla {NoteMax} karakter olabilir.", "aciklama");
        return new VardiyaInput
        {
            PersonelId = i.PersonelId ?? Guid.Empty,
            Tarih = i.Tarih ?? default,
            BaslangicSaat = ParseTime(i.BaslangicSaat, "baslangicSaat", "Başlangıç saati"),
            BitisSaat = ParseTime(i.BitisSaat, "bitisSaat", "Bitiş saati"),
            Sube = branch,
            Aciklama = note,
        };
    }

    /// <summary>"SS:dd" (24 saat). Blazor <c>type="time"</c> girdisi de bu biçimi gönderir.</summary>
    internal static TimeOnly ParseTime(string? value, string field, string label)
    {
        var s = F5Ortak.Nz(value) ?? throw new ValidationException($"{label} zorunludur.", field);
        if (!TimeOnly.TryParseExact(s, ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            throw new ValidationException($"{label} SS:dd biçiminde olmalıdır (ör. 08:30).", field);
        return t;
    }

    private static async Task<ShiftDto?> DtoAsync(Guid id, StaffShiftService s, CancellationToken ct)
    {
        // #302 L2: row and version as one consistent pair (see GetWithStaffAndVersionAsync).
        if (await s.GetWithStaffAndVersionAsync(id, ct) is not { } pair) return null;
        var (row, version) = pair;
        var v = row.Vardiya;
        return new ShiftDto(v.Id, v.PersonelId, row.PersonelAd, v.Tarih,
            v.BaslangicSaat.ToString("HH:mm", CultureInfo.InvariantCulture),
            v.BitisSaat.ToString("HH:mm", CultureInfo.InvariantCulture),
            v.SureDk, ShiftFormat.Range(v), v.Sube, v.Aciklama, version);
    }
}
