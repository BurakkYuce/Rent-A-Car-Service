using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Common;
using RentACar.Application.Authorization;
using RentACar.Application.Penalties;
using RentACar.Domain.Enums;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Penalties;

/// <summary>Ceza form post uçları (kayıt + yansıt/öde/iptal). Tenant HttpContext claim'inden (RLS).</summary>
public static class PenaltyEndpoints
{
    public static IEndpointRouteBuilder MapPenaltyEndpoints(this IEndpointRouteBuilder app)
    {
        // Çift savunma (adversarial LOW): create/iptal OperationsWrite; yansıt VE ödeme PARA yolu
        // (FinanceWrite) — FAZ-60'ta ödeme deftere yazmaya başladığı için /ode ops'tan fin'e TAŞINDI.
        var ops = app.MapGroup("/cezalar").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();
        var fin = app.MapGroup("/cezalar").RequirePermission(Permission.FinanceWrite).AntiforgeryByEnv();

        ops.MapPost("/create", async (PenaltyService svc,
            [FromForm] string cezaTuru, [FromForm] string? tebligTarihi, [FromForm] string? vadeGun,
            [FromForm] string? vehicleId, [FromForm] string? cariId, [FromForm] string? rentalId,
            [FromForm] string? tutar, [FromForm] string? sebep,
            [FromForm] string? satirTutar2, [FromForm] string? satirSebep2,
            [FromForm] string? satirTutar3, [FromForm] string? satirSebep3,
            [FromForm] string? satirTutar4, [FromForm] string? satirSebep4,
            [FromForm] string? satirTutar5, [FromForm] string? satirSebep5,
            [FromForm] string? saat, [FromForm] string? yer, [FromForm] string? cepTel,
            [FromForm] string? makbuzNo, [FromForm] string? islemSube) =>
        {
            // Kalemler: 1. satır ana tutar/sebep, 2-5 ek kalemler (canlı Ceza_Tutari1-3 paritesi
            // ve fazlası). Boş tutarlı satır ATLANIR — kullanıcı 5 kutuyu doldurmak zorunda değil.
            var satirlar = new List<PenaltySatirInput>();
            void Ekle(string? t, string? s)
            {
                var d = FormParse.Dec(t);
                if (d is decimal v && v != 0m) satirlar.Add(new PenaltySatirInput { Tutar = v, Sebep = s });
            }
            Ekle(tutar, sebep);
            Ekle(satirTutar2, satirSebep2);
            Ekle(satirTutar3, satirSebep3);
            Ekle(satirTutar4, satirSebep4);
            Ekle(satirTutar5, satirSebep5);

            var input = new PenaltyInput
            {
                CezaTuru = cezaTuru,
                TebligTarihi = FormParse.Date(tebligTarihi),
                VadeGun = FormParse.Int(vadeGun) ?? 15,
                VehicleId = FormParse.Id(vehicleId),
                CariId = FormParse.Id(cariId),
                RentalId = FormParse.Id(rentalId),
                Tutar = FormParse.Dec(tutar) ?? 0m,
                Sebep = satirlar.Count > 1 ? null : sebep,   // çok kalemliyse başlık özeti servis üretir
                Satirlar = satirlar,
                Saat = saat, Yer = yer, CepTel = cepTel, MakbuzNo = makbuzNo, IslemSube = islemSube
            };
            try
            {
                await svc.CreateAsync(input);
                return Sonuc.Tamam("/cezalar", "Ceza kaydedildi.");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/cezalar?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        fin.MapPost("/yansit", async (PenaltyService svc, [FromForm] Guid id) => await Act(() => svc.YansitAsync(id)));

        // Tamamını öde (eski davranış) — artık defter yazdığı için FinanceWrite.
        fin.MapPost("/ode", async (PenaltyService svc, [FromForm] Guid id, [FromForm] string? hesap,
            [FromForm] string? tarih, [FromForm] string? makbuzNo, [FromForm] string? islemYapan) =>
            await Act(() => svc.OdeAsync(id, HesapCoz(hesap), FormParse.Date(tarih), makbuzNo, islemYapan)));

        // FAZ-60 — KALEM bazlı kısmi ödeme.
        fin.MapPost("/kismi-ode", async (PenaltyService svc,
            [FromForm] Guid id, [FromForm] Guid satirId, [FromForm] string? tutar, [FromForm] string? hesap,
            [FromForm] string? tarih, [FromForm] string? makbuzNo, [FromForm] string? islemYapan,
            [FromForm] string? kasaKodu, [FromForm] string? hesapNo, [FromForm] string? aciklama,
            [FromForm] string? islemAnahtari) =>
        {
            try
            {
                await svc.KismiOdeAsync(id, new CezaOdemeInput
                {
                    SatirId = satirId,
                    Tutar = FormParse.Dec(tutar),
                    Hesap = HesapCoz(hesap),
                    Tarih = FormParse.Date(tarih),
                    MakbuzNo = makbuzNo, IslemYapan = islemYapan,
                    KasaKodu = kasaKodu, HesapNo = hesapNo, Aciklama = aciklama,
                    IslemAnahtari = FormParse.Id(islemAnahtari)
                });
                return Sonuc.Tamam("/cezalar", "Ceza tahsilatı kaydedildi.");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/cezalar?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        ops.MapPost("/iptal", async (PenaltyService svc, [FromForm] Guid id) => await Act(() => svc.IptalAsync(id), "Ceza iptal edildi.")).RequirePermission(Permission.OperationsDelete);

        return app;
    }

    /// <summary>Yalnız Kasa/Banka; tanınmayan değer Kasa'ya düşmez, servis reddeder.</summary>
    private static LedgerAccountType HesapCoz(string? s)
        => string.Equals(s, "Banka", StringComparison.OrdinalIgnoreCase)
            ? LedgerAccountType.Banka
            : LedgerAccountType.Kasa;

    private static async Task<IResult> Act(Func<Task<bool>> action, string mesaj = "İşlem tamamlandı.")
    {
        try
        {
            await action();
            return Sonuc.Tamam("/cezalar", mesaj);
        }
        catch (ValidationException ex)
        {
            return Results.Redirect($"/cezalar?hata={Uri.EscapeDataString(ex.Message)}");
        }
    }
}
