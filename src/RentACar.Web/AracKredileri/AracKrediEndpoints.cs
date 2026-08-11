using Microsoft.AspNetCore.Mvc;
using RentACar.Application.AracKredileri;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.AracKredileri;

/// <summary>Araç kredisi form post uçları (roadmap L4). OperationsWrite.</summary>
public static class AracKrediEndpoints
{
    /// <summary>Liste filtresi alanları — POST sonrası kullanıcı baktığı süzgeçte kalsın diye
    /// formlarda gizli alan olarak taşınır ve dönüş adresine geri yazılır.</summary>
    private static readonly string[] FiltreAlanlari = ["cariF", "plakaF", "dosyaF", "durumF", "bas", "bit"];

    public static IEndpointRouteBuilder MapAracKrediEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/arac-kredi").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (AracKrediService svc, HttpRequest req) =>
        {
            var f = req.Form;
            string? S(string k) { var v = f[k].ToString(); return string.IsNullOrWhiteSpace(v) ? null : v; }
            var input = new AracKrediInput
            {
                BankaAdi = S("bankaAdi"),
                VehicleId = FormParse.Id(S("vehicleId")),
                CariId = FormParse.Id(S("cariId")),
                DosyaNo = S("dosyaNo"),
                KrediTutari = FormParse.Dec(S("krediTutari")) ?? 0m,
                FaizOran = FormParse.Dec(S("faizOran")) ?? 0m,
                TaksitSayisi = FormParse.Int(S("taksitSayisi")) ?? 0,
                BaslangicTarihi = FormParse.Date(S("baslangicTarihi")),
                Doviz = S("doviz") ?? "TRY",
                Kur = FormParse.Dec(S("kur")) ?? 1m,
                Aciklama = S("aciklama")
            };
            return await Durum(req, async () => { await svc.CreateAsync(input); }, "ok=1");
        });

        // Adversarial 1.3 M2: taksit artık DEFTER yazar → FinanceWrite grubu (Muhasebe erişir;
        // grubun geri kalanı — create/iptal/toplu-iptal — OperationsWrite kalır).
        var fin = app.MapGroup("/arac-kredi").RequirePermission(Permission.FinanceWrite).AntiforgeryByEnv();
        fin.MapPost("/taksit-ode", async (AracKrediService svc, HttpRequest req, [FromForm] Guid id,
            [FromForm] string? hesap, [FromForm] string? islemAnahtari, [FromForm] string? hesapId) =>
            await Durum(req, () => svc.TaksitOdeAsync(id,
                string.Equals(hesap, "Banka", StringComparison.OrdinalIgnoreCase) ? Domain.Enums.LedgerAccountType.Banka : Domain.Enums.LedgerAccountType.Kasa,
                null, FormParse.Id(islemAnahtari), FormParse.Id(hesapId))));   // FAZ-50

        grp.MapPost("/iptal", async (AracKrediService svc, HttpRequest req, [FromForm] Guid id) =>
            await Durum(req, () => svc.IptalAsync(id)));

        // FAZ-13 — toplu "Taksitleri İptal Et". Seçim checkbox'ları tablo satırlarının İÇİNDE ama
        // form= attribute'üyle tablonun DIŞINDAKİ bu forma bağlı (satırlarda zaten form var; iç içe
        // form HTML'de yasak). Deftere yazmaz → OperationsWrite grubunda.
        grp.MapPost("/taksit-iptal", async (AracKrediService svc, HttpRequest req) =>
        {
            var ids = req.Form["id"].Select(FormParse.Id).OfType<Guid>().ToList();
            return await Durum(req, async () =>
            {
                var n = await svc.TaksitleriIptalEtAsync(ids);
                // Hiç satır etkilenmediyse sessiz başarı yanıltıcı olur (kullanıcı "iptal ettim"
                // sanır): seçilenlerin hepsi zaten kapalı/iptalse bunu söyle.
                if (n == 0) throw new ValidationException("Seçilen kredilerin hiçbiri aktif değil; iptal edilecek taksit yok.");
            }, "ok=1");
        });

        return app;
    }

    /// <summary>Filtreyi koruyarak dön. Adres SABİT ("/arac-kredi") — kullanıcı girdisi yol olarak
    /// kullanılmaz (açık yönlendirme yüzeyi açılmasın), yalnız bilinen filtre anahtarları eklenir.</summary>
    private static string Geri(HttpRequest req, string? ek = null)
    {
        var q = new List<string>();
        foreach (var ad in FiltreAlanlari)
        {
            var v = FormParse.Str(req.Form, ad);
            if (v is not null) q.Add($"{ad}={Uri.EscapeDataString(v)}");
        }
        if (ek is not null) q.Add(ek);
        return "/arac-kredi" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }

    private static async Task<IResult> Durum(HttpRequest req, Func<Task> action, string? basariEk = null)
    {
        try { await action(); return Results.Redirect(Geri(req, basariEk)); }
        catch (ValidationException ex) { return Results.Redirect(Geri(req, $"hata={Uri.EscapeDataString(ex.Message)}")); }
    }
}
