using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.Finance;

/// <summary>Nakit tahsilat/ödeme + virman + ters kayıt form post uçları.</summary>
public static class FinanceEndpoints
{
    /// <summary>Form "Kasa"/"Banka" metnini hesap tipine çevirir (varsayılan Kasa).</summary>
    private static LedgerAccountType ParseHesap(string? s)
        => string.Equals(s, "Banka", StringComparison.OrdinalIgnoreCase)
            ? LedgerAccountType.Banka : LedgerAccountType.Kasa;

    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/finans").RequirePermission(Permission.FinanceWrite).AntiforgeryByEnv();

        grp.MapPost("/tahsilat", async (CashService svc,
            [FromForm] Guid cariId, [FromForm] string? rentalId, [FromForm] decimal tutar,
            [FromForm] string? doviz, [FromForm] string? kur, [FromForm] string? aciklama,
            [FromForm] string? hesap, [FromForm] string? donus) =>
        {
            try
            {
                await svc.CollectAsync(new CashInput
                {
                    CariId = cariId, RentalId = FormParse.Id(rentalId), Tutar = tutar,
                    Doviz = string.IsNullOrWhiteSpace(doviz) ? "TRY" : doviz, Kur = FormParse.Dec(kur) ?? 1m,
                    Aciklama = aciklama, Hesap = ParseHesap(hesap)
                });
                return Results.Redirect(donus ?? $"/cariler/{cariId}/ekstre");
            }
            catch (ValidationException ex)
            {
                var url = donus ?? $"/cariler/{cariId}/ekstre";
                return Results.Redirect($"{url}?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        grp.MapPost("/odeme", async (CashService svc,
            [FromForm] Guid cariId, [FromForm] string? rentalId, [FromForm] decimal tutar,
            [FromForm] string? doviz, [FromForm] string? kur, [FromForm] string? aciklama,
            [FromForm] string? hesap, [FromForm] string? donus) =>
        {
            try
            {
                await svc.PayAsync(new CashInput
                {
                    CariId = cariId, RentalId = FormParse.Id(rentalId), Tutar = tutar,
                    Doviz = string.IsNullOrWhiteSpace(doviz) ? "TRY" : doviz, Kur = FormParse.Dec(kur) ?? 1m,
                    Aciklama = aciklama, Hesap = ParseHesap(hesap)
                });
                return Results.Redirect(donus ?? $"/cariler/{cariId}/ekstre");
            }
            catch (ValidationException ex)
            {
                var url = donus ?? $"/cariler/{cariId}/ekstre";
                return Results.Redirect($"{url}?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        grp.MapPost("/virman", async (CashService svc,
            [FromForm] string? kaynak, [FromForm] string? hedef, [FromForm] decimal tutar,
            [FromForm] string? aciklama) =>
        {
            try
            {
                await svc.TransferAsync(ParseHesap(kaynak), ParseHesap(hedef), tutar, aciklama: aciklama);
                return Results.Redirect("/kasa");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/kasa?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        grp.MapPost("/tahsilat/ters", async (CashService svc, [FromForm] Guid id, [FromForm] Guid cariId) =>
        {
            try { await svc.ReverseAsync(id); return Results.Redirect($"/cariler/{cariId}/ekstre"); }
            catch (ValidationException ex) { return Results.Redirect($"/cariler/{cariId}/ekstre?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/fatura", async (InvoiceService svc, HttpRequest req) =>
        {
            var f = req.Form;
            var rentalId = FormParse.Id(f["rentalId"].ToString()) ?? Guid.Empty;
            string? S(string k) { var v = f[k].ToString(); return string.IsNullOrWhiteSpace(v) ? null : v; }
            bool B(string k) => S(k) is "true" or "True" or "on";
            // Opsiyonel vergi/belge metadata (bilgi amaçlı; defter postlamasına yansımaz).
            var vergi = new InvoiceTaxInfo(
                Otv: FormParse.Dec(S("otv")),
                TevkifatOran: FormParse.Dec(S("tevkifatOran")),
                TevkifatTutar: FormParse.Dec(S("tevkifatTutar")),
                DamgaVergisi: FormParse.Dec(S("damgaVergisi")),
                IadeMi: B("iadeMi"),
                ManuelMi: B("manuelMi"));
            try { await svc.CreateFromRentalAsync(rentalId, vergi: vergi); return Results.Redirect($"/kiralar/{rentalId}"); }
            catch (ValidationException ex) { return Results.Redirect($"/kiralar/{rentalId}?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/cari-virman", async (CashService svc, HttpRequest req) =>
        {
            var f = req.Form;
            var kaynak = FormParse.Id(f["kaynakCariId"].ToString()) ?? Guid.Empty;
            var hedef = FormParse.Id(f["hedefCariId"].ToString()) ?? Guid.Empty;
            var tutar = FormParse.Dec(f["tutar"].ToString()) ?? 0m;
            var doviz = f["doviz"].ToString();
            var kur = FormParse.Dec(f["kur"].ToString()) ?? 1m;
            var aciklama = f["aciklama"].ToString();
            var anahtar = FormParse.Id(f["islemAnahtari"].ToString()); // çift-submit idempotency token
            try
            {
                await svc.TransferBetweenCariAsync(kaynak, hedef, tutar,
                    string.IsNullOrWhiteSpace(doviz) ? "TRY" : doviz, kur,
                    string.IsNullOrWhiteSpace(aciklama) ? null : aciklama, anahtar);
                return Results.Redirect("/cari-virman?ok=1");
            }
            catch (ValidationException ex) { return Results.Redirect($"/cari-virman?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/toplu-tahsilat", async (CashService svc, HttpRequest req) =>
        {
            var f = req.Form;
            var anahtar = FormParse.Id(f["islemAnahtari"].ToString()); // çift-submit idempotency token
            var hesap = string.Equals(f["hesap"].ToString(), "Banka", StringComparison.OrdinalIgnoreCase)
                ? LedgerAccountType.Banka : LedgerAccountType.Kasa;
            // Her satır: "cariId;tutar[;açıklama]" (boş satırlar atlanır).
            var satirlar = new List<CashInput>();
            foreach (var line in (f["satirlar"].ToString() ?? string.Empty)
                         .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var p = line.Split(';', StringSplitOptions.TrimEntries);
                satirlar.Add(new CashInput
                {
                    CariId = FormParse.Id(p.Length > 0 ? p[0] : null) ?? Guid.Empty,
                    Tutar = FormParse.Dec(p.Length > 1 ? p[1] : null) ?? 0m,
                    Hesap = hesap,
                    Doviz = "TRY",
                    Kur = 1m,
                    Aciklama = p.Length > 2 && !string.IsNullOrWhiteSpace(p[2]) ? p[2] : "Toplu tahsilat"
                });
            }
            try
            {
                await svc.BatchCollectAsync(satirlar, anahtar);
                return Results.Redirect($"/toplu-tahsilat?ok={satirlar.Count}");
            }
            catch (ValidationException ex) { return Results.Redirect($"/toplu-tahsilat?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        return app;
    }
}
