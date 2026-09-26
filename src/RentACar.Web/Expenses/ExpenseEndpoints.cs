using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Web.Common;
using RentACar.Web.Identity;
using RentACar.Application.Expenses;
using RentACar.Domain.Enums;

namespace RentACar.Web.Expenses;

/// <summary>Gider create form post ucu. Tenant HttpContext claim'inden (RLS).</summary>
public static class ExpenseEndpoints
{
    public static IEndpointRouteBuilder MapExpenseEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/giderler").RequirePermission(Permission.FinanceWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (ExpenseService svc,
            [FromForm] ExpenseType tip, [FromForm] string? vehicleId, [FromForm] string? cariId,
            [FromForm] string? sube, [FromForm] string? evrakNo, [FromForm] decimal netTutar,
            [FromForm] decimal kdvOrani, [FromForm] PaymentMethod odemeYontemi,
            [FromForm] string? doviz, [FromForm] string? kur, [FromForm] string? aciklama,
            [FromForm] string? hesapId,      // FAZ-50: hangi spesifik kasa/banka hesabından ödendi
            [FromForm] string? odemeTarihi, [FromForm] string? hazirAciklama, [FromForm] string? rentalId) => // FAZ-64
        {
            var input = new ExpenseInput
            {
                Tip = tip,
                VehicleId = Guid.TryParse(vehicleId, out var v) ? v : null,
                CariId = Guid.TryParse(cariId, out var c) ? c : null,
                Sube = sube, EvrakNo = evrakNo,
                NetTutar = netTutar, KdvOrani = kdvOrani, OdemeYontemi = odemeYontemi,
                Doviz = string.IsNullOrWhiteSpace(doviz) ? "TRY" : doviz, Kur = FormParse.Dec(kur), Aciklama = aciklama, // boş kur → otomatik (1.1b)
                FinansalHesapId = FormParse.Id(hesapId),  // FAZ-50
                // FAZ-64 bilgi alanları — deftere girmez.
                OdemeTarihi = FormParse.Date(odemeTarihi),
                HazirAciklama = hazirAciklama,
                RentalId = FormParse.Id(rentalId)
            };
            try
            {
                await svc.CreateAsync(input);
                return Sonuc.Tamam("/giderler", "Kayıt eklendi.");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/giderler?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        // FAZ-64 — kısmi ödeme kaydı. DEFTERE YAZMAZ (takip); çift-submit sessizce yutulur.
        grp.MapPost("/odeme", async (ExpenseService svc, HttpRequest req) =>
        {
            var f = req.Form;
            try
            {
                await svc.AddPaymentAsync(new GiderOdemeInput
                {
                    ExpenseId = FormParse.Id(FormParse.Str(f, "expenseId")) ?? Guid.Empty,
                    Tutar = FormParse.Dec(FormParse.Str(f, "tutar")),   // boş → kalanın tamamı
                    Tarih = FormParse.Date(FormParse.Str(f, "tarih")),
                    MakbuzNo = FormParse.Str(f, "makbuzNo"),
                    Aciklama = FormParse.Str(f, "aciklama"),
                    IslemAnahtari = FormParse.Id(FormParse.Str(f, "islemAnahtari"))
                });
                return Sonuc.Tamam("/giderler", "Ödeme kaydedildi.");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/giderler?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        return app;
    }
}
