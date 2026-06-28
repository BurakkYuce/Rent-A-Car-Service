using System.Globalization;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Pricing;
using RentACar.Web.Identity;

namespace RentACar.Web.Pricing;

/// <summary>Fiyat motoru v1 hesaplama ucu. Formu IFormCollection'dan okur, RentalQuoteEngine ile
/// kalemli teklif hesaplar, özet sonucu query string ile /fiyat-hesapla sayfasına döndürür (salt-hesap,
/// defter postalamaz). Salt-okunur hesap → ViewReports yeterli.</summary>
public static class QuoteEndpoints
{
    public static IEndpointRouteBuilder MapQuoteEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/fiyat-hesapla").RequirePermission(Permission.ViewReports).AntiforgeryByEnv();

        grp.MapPost("/hesapla", async (RentalQuoteEngine engine, HttpRequest req) =>
        {
            var f = req.Form;
            var kodlar = (f["sigortaKodlari"].ToString() ?? string.Empty)
                .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var input = new QuoteRequest
            {
                AracGrupKod = f["aracGrupKod"].ToString(),
                Kanal = Str(f, "kanal"),
                Sube = Str(f, "sube"),
                BasTar = FormParse.Date(Str(f, "basTar")) ?? default,
                BitTar = FormParse.Date(Str(f, "bitTar")) ?? default,
                SurucuYas = FormParse.Int(Str(f, "surucuYas")),
                TahminiKm = FormParse.Int(Str(f, "tahminiKm")),
                SigortaUrunKodlari = kodlar
            };
            try
            {
                var q = await engine.QuoteAsync(input);
                var qs =
                    $"?ok=1&grup={Esc(input.AracGrupKod)}&gun={q.Gun}&gunluk={Num(q.GunlukUcret)}" +
                    $"&baz={Num(q.BazTutar)}&km={Num(q.KmAsimTutar)}&sig={Num(q.SigortaToplam)}" +
                    $"&ara={Num(q.AraToplam)}&iskOran={Num(q.IskontoOran)}&isk={Num(q.IskontoTutar)}" +
                    $"&toplam={Num(q.GenelToplam)}&prov={Num(q.Provizyon)}&muaf={Num(q.Muafiyet)}" +
                    $"&not={Esc(string.Join(" | ", q.Notlar))}";
                return Results.Redirect("/fiyat-hesapla" + qs);
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/fiyat-hesapla?hata={Esc(ex.Message)}");
            }
        });

        return app;
    }

    private static string? Str(IFormCollection f, string key)
    {
        var v = f[key].ToString();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static string Num(decimal d) => d.ToString(CultureInfo.InvariantCulture);
    private static string Esc(string s) => Uri.EscapeDataString(s);
}
