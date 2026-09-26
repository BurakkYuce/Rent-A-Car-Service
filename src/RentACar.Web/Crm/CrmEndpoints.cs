using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Crm;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.Crm;

/// <summary>CRM (anket + şikayet) form post uçları (roadmap C3). OperationsWrite. IFormCollection + FormParse.</summary>
public static class CrmEndpoints
{
    public static IEndpointRouteBuilder MapCrmEndpoints(this IEndpointRouteBuilder app)
    {
        var an = app.MapGroup("/anketler").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();
        an.MapPost("/create", async (SurveyService svc, HttpRequest req) =>
            await Run("/anketler", () => svc.CreateAsync(BuildSurvey(req.Form))));
        an.MapPost("/update", async (SurveyService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run("/anketler", () => svc.UpdateAsync(id, BuildSurvey(req.Form))));
        an.MapPost("/delete", async (SurveyService svc, [FromForm] Guid id) =>
            await Run("/anketler", () => svc.DeleteAsync(id)));

        var sk = app.MapGroup("/sikayetler").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();
        sk.MapPost("/create", async (ComplaintService svc, HttpRequest req) =>
            await Run("/sikayetler", () => svc.CreateAsync(BuildComplaint(req.Form))));
        sk.MapPost("/update", async (ComplaintService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run("/sikayetler", () => svc.UpdateAsync(id, BuildComplaint(req.Form))));
        sk.MapPost("/delete", async (ComplaintService svc, [FromForm] Guid id) =>
            await Run("/sikayetler", () => svc.DeleteAsync(id)));

        return app;
    }

    private static AnketInput BuildSurvey(IFormCollection f) => new()
    {
        CariId = Guid.TryParse(FormParse.Str(f, "cariId"), out var c) ? c : null,
        Puan = FormParse.Int(FormParse.Str(f, "puan")) ?? 0,
        Yorum = FormParse.Str(f, "yorum"),
        Tarih = FormParse.Date(FormParse.Str(f, "tarih")),
        Kaynak = FormParse.Str(f, "kaynak"),
        // FAZ-42 — sözleşme bağı + tür/durum + 8 soruluk cevap seti
        RentalId = FormParse.Id(FormParse.Str(f, "rentalId")),
        AnketTuru = Enum.TryParse<RentACar.Domain.Enums.SurveyType>(FormParse.Str(f, "anketTuru"), out var at) ? at : null,
        Durum = Enum.TryParse<RentACar.Domain.Enums.SurveyStatus>(FormParse.Str(f, "durum"), out var name)
            ? name : RentACar.Domain.Enums.SurveyStatus.Yapildi,
        CikisOfisi = FormParse.Str(f, "cikisOfisi"),
        Cevaplar = SurveyAnswers(f)
    };

    /// <summary>
    /// FAZ-42 — form 8 satırı soru{n}/cevap{n}/aciklama{n} adlarıyla gönderir. SORUSU BOŞ satır
    /// servis tarafında atılır (boş form satırı kayıt üretmesin).
    /// </summary>
    private static List<AnketCevapInput> SurveyAnswers(IFormCollection f)
    {
        var list = new List<AnketCevapInput>();
        for (var i = 1; i <= 20; i++)   // tavan: form 8 basar, elle gönderim de sınırlı kalsın
        {
            var question = FormParse.Str(f, $"soru{i}");
            if (question is null) continue;
            list.Add(new AnketCevapInput
            {
                SoruNo = i,
                Soru = question,
                Cevap = FormParse.Str(f, $"cevap{i}"),
                Aciklama = FormParse.Str(f, $"aciklama{i}")
            });
        }
        return list;
    }

    private static SikayetInput BuildComplaint(IFormCollection f) => new()
    {
        CariId = Guid.TryParse(FormParse.Str(f, "cariId"), out var c) ? c : null,
        Konu = f["konu"].ToString(),
        Detay = FormParse.Str(f, "detay"),
        Durum = ParseEnum<ComplaintStatus>(FormParse.Str(f, "durum")) ?? ComplaintStatus.Acik,
        Tarih = FormParse.Date(FormParse.Str(f, "tarih")),
        Cozum = FormParse.Str(f, "cozum"),
        // FAZ-43 teslim/dönüş bağı
        RentalId = FormParse.Id(FormParse.Str(f, "rentalId")),
        TeslimAlanPersonelId = FormParse.Id(FormParse.Str(f, "teslimAlanPersonelId")),
        TeslimEdenPersonelId = FormParse.Id(FormParse.Str(f, "teslimEdenPersonelId")),
        Puan = FormParse.Int(FormParse.Str(f, "puan")),
        SikayetKanali = FormParse.Str(f, "sikayetKanali"),
        SikayetYeri = Enum.TryParse<RentACar.Domain.Enums.ComplaintLocation>(FormParse.Str(f, "sikayetYeri"), out var sy) ? sy : null,
        CikisOfisi = FormParse.Str(f, "cikisOfisi")
    };


    private static T? ParseEnum<T>(string? s) where T : struct, Enum
        => Enum.TryParse<T>((s ?? string.Empty).Trim(), out var v) ? v : null;

    private static async Task<IResult> Run(string back, Func<Task> action)
    {
        try { await action(); return Results.Redirect(back); }
        catch (ValidationException ex) { return Results.Redirect($"{back}?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
