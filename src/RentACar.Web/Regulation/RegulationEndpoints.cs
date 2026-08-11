using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Regulation;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.Regulation;

/// <summary>Sigorta/MTV/Muayene kayıt form post uçları.</summary>
public static class RegulationEndpoints
{
    public static IEndpointRouteBuilder MapRegulationEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/regulasyon").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/sigorta", async (RegulationService svc, HttpRequest req,
            [FromForm] Guid vehicleId, [FromForm] InsuranceType tip,
            [FromForm] DateTimeOffset baslangic, [FromForm] DateTimeOffset bitis, [FromForm] decimal prim,
            [FromForm] string? policeNo, [FromForm] string? firma, [FromForm] string? acenta, [FromForm] string? doviz) =>
        {
            try
            {
                // FAZ-15 değer tabanı: opsiyonel decimal alanlar "" ile 400 verir → string? + FormParse.
                await svc.AddInsuranceAsync(vehicleId, tip, baslangic, bitis, prim, policeNo, firma, acenta, doviz,
                    FormParse.Dec(FormParse.Str(req.Form, "aracDegeri")),
                    FormParse.Dec(FormParse.Str(req.Form, "immDegeri")),
                    FormParse.Dec(FormParse.Str(req.Form, "aksesuarDegeri")));
                return Results.Redirect("/regulasyon");
            }
            catch (ValidationException ex) { return Results.Redirect($"/regulasyon?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        // FAZ-15 zeyil (poliçe eki) — OperationsWrite grubunda: BİLGİ kaydıdır, deftere yazmaz,
        // bu yüzden FinanceWrite gerektirmez (mali uçlar ayrı /regulasyon-odeme grubunda).
        grp.MapPost("/zeyil", async (RegulationService svc, HttpRequest req, [FromForm] Guid policyId) =>
        {
            try
            {
                await svc.AddZeyilAsync(new ZeyilInput
                {
                    PolicyId = policyId,
                    ZeyilNo = FormParse.Str(req.Form, "zeyilNo"),
                    Tarih = FormParse.Date(FormParse.Str(req.Form, "tarih")),
                    Tanzim = FormParse.Date(FormParse.Str(req.Form, "tanzim")),
                    Deger = FormParse.Dec(FormParse.Str(req.Form, "deger")),
                    Brut = FormParse.Dec(FormParse.Str(req.Form, "brut")),
                    Net = FormParse.Dec(FormParse.Str(req.Form, "net")),
                    FonVergi = FormParse.Dec(FormParse.Str(req.Form, "fonVergi")),
                    Tipi = FormParse.Str(req.Form, "tipi"),
                    Neden = FormParse.Str(req.Form, "neden")
                });
                return Results.Redirect("/regulasyon#zeyil");
            }
            catch (ValidationException ex) { return Results.Redirect($"/regulasyon?hata={Uri.EscapeDataString(ex.Message)}#zeyil"); }
        });

        grp.MapPost("/zeyil/sil", async (RegulationService svc, [FromForm] Guid id) =>
        {
            try { await svc.DeleteZeyilAsync(id); return Results.Redirect("/regulasyon#zeyil"); }
            catch (ValidationException ex) { return Results.Redirect($"/regulasyon?hata={Uri.EscapeDataString(ex.Message)}#zeyil"); }
        });

        grp.MapPost("/mtv", async (RegulationService svc, HttpRequest req,
            [FromForm] Guid vehicleId, [FromForm] string donem, [FromForm] decimal tutar, [FromForm] DateTimeOffset vade) =>
        {
            try { await svc.AddMtvAsync(vehicleId, donem, tutar, vade, FormParse.Str(req.Form, "aciklama")); return Results.Redirect("/regulasyon"); }
            catch (ValidationException ex) { return Results.Redirect($"/regulasyon?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/muayene", async (RegulationService svc, HttpRequest req,
            [FromForm] Guid vehicleId, [FromForm] DateTimeOffset muayeneTarihi, [FromForm] DateTimeOffset bitis, [FromForm] decimal ucret) =>
        {
            try
            {
                await svc.AddInspectionAsync(vehicleId, muayeneTarihi, bitis, ucret,
                    FormParse.Int(FormParse.Str(req.Form, "islemKm")), FormParse.Str(req.Form, "aciklama"));
                return Results.Redirect("/regulasyon");
            }
            catch (ValidationException ex) { return Results.Redirect($"/regulasyon?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        // MTV ödeme→defter (roadmap J1): FinanceWrite (mali işlem).
        var ode = app.MapGroup("/regulasyon-odeme").RequirePermission(Permission.FinanceWrite).AntiforgeryByEnv();
        // FAZ-14: kısmi tutar + evrak/işlem-yapan/kasa bilgileri. Tutar BOŞ bırakılırsa kalanın
        // tamamı ödenir (eski davranış). Opsiyonel sayısal alanlar "" ile 400 vermesin diye
        // string? alınıp FormParse ile çevrilir.
        ode.MapPost("/mtv", async (RegulationService svc, HttpRequest req, [FromForm] Guid id, [FromForm] string? hesap) =>
        {
            var h = string.Equals(hesap, "Banka", StringComparison.OrdinalIgnoreCase) ? LedgerAccountType.Banka : LedgerAccountType.Kasa;
            try
            {
                await svc.MtvOdeAsync(id, h, odemeTarih: FormParse.Date(FormParse.Str(req.Form, "odemeTarihi")),
                    odeme: OdemeGirdisi(req.Form));
                return Results.Redirect("/regulasyon?ok=1");
            }
            catch (ValidationException ex) { return Results.Redirect($"/regulasyon?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        ode.MapPost("/muayene", async (RegulationService svc, HttpRequest req, [FromForm] Guid id, [FromForm] string? hesap, [FromForm] string? ceza) =>
        {
            var h = string.Equals(hesap, "Banka", StringComparison.OrdinalIgnoreCase) ? LedgerAccountType.Banka : LedgerAccountType.Kasa;
            var c = FormParse.Dec(ceza) ?? 0m;
            try
            {
                await svc.MuayeneOdeAsync(id, h, c, odemeTarih: FormParse.Date(FormParse.Str(req.Form, "odemeTarihi")),
                    odeme: OdemeGirdisi(req.Form));
                return Results.Redirect("/regulasyon?ok=1");
            }
            catch (ValidationException ex) { return Results.Redirect($"/regulasyon?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        ode.MapPost("/sigorta", async (RegulationService svc, [FromForm] Guid id, [FromForm] string? hesap,
            [FromForm] string? zeyil, [FromForm] string? kur, [FromForm] string? hesapId) =>
        {
            var h = string.Equals(hesap, "Banka", StringComparison.OrdinalIgnoreCase) ? LedgerAccountType.Banka : LedgerAccountType.Kasa;
            var z = FormParse.Dec(zeyil) ?? 0m;
            var k = FormParse.Dec(kur); // boş → otomatik çözüm (TRY=1; döviz KurService — 1.1)
            try { await svc.SigortaOdeAsync(id, h, z, kur: k, hesapId: FormParse.Id(hesapId)); return Results.Redirect("/regulasyon?ok=1"); }
            catch (ValidationException ex) { return Results.Redirect($"/regulasyon?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        return app;
    }

    /// <summary>Kısmi ödeme form alanları (FAZ-14). Tutar boşsa null → kalanın tamamı ödenir.</summary>
    private static RegulasyonOdemeInput OdemeGirdisi(IFormCollection f) => new()
    {
        Tutar = FormParse.Dec(FormParse.Str(f, "tutar")),
        EvrakNo = FormParse.Str(f, "evrakNo"),
        IslemYapan = FormParse.Str(f, "islemYapan"),
        Aciklama = FormParse.Str(f, "odemeAciklama"),
        KasaKodu = FormParse.Str(f, "kasaKodu"),
        HesapNo = FormParse.Str(f, "hesapNo"),
        HesapId = FormParse.Id(FormParse.Str(f, "hesapId")),   // FAZ-50: defter bağı
        IslemAnahtari = FormParse.Id(FormParse.Str(f, "islemAnahtari"))
    };
}
