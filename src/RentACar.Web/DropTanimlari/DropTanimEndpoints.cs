using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.DropTanimlari;
using RentACar.Web.Identity;

namespace RentACar.Web.DropTanimlari;

/// <summary>Drop matris master form post uçları (roadmap N2 + FAZ-22). OperationsWrite.</summary>
public static class DropTanimEndpoints
{
    public static IEndpointRouteBuilder MapDropTanimEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/drop-tanimlari").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (DropTanimService svc, HttpRequest req) =>
            await Run(req, () => svc.CreateAsync(Build(req.Form, aktif: true))));

        // FAZ-22: düzenleme ucu YOKTU — kullanıcı bir satırın ücretini değiştirmek için silip
        // yeniden eklemek zorundaydı.
        grp.MapPost("/update", async (DropTanimService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(req, () =>
            {
                // Adversarial M8: alan HİÇ gelmezse eskiden sessizce false yazılıyordu → satır
                // pasife düşer, drop ücreti durur (sessiz para kaybı). Eksik alanı kabul etmek
                // yerine açıkça reddediyoruz (fail-closed).
                if (!req.Form.ContainsKey("aktif"))
                    throw new ValidationException("Eksik form alanı: durum (aktif).");
                return svc.UpdateAsync(id, Build(req.Form,
                    aktif: string.Equals(req.Form["aktif"].ToString(), "true", StringComparison.OrdinalIgnoreCase)));
            }));

        grp.MapPost("/delete", async (DropTanimService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(req, () => svc.DeleteAsync(id)));

        return app;
    }

    private static DropTanimInput Build(IFormCollection f, bool aktif) => new()
    {
        Lokasyon = f["lokasyon"].ToString(),
        Sube = f["sube"].ToString(),
        KarsilamaSekli = FormParse.Str(f, "karsilamaSekli"),
        CalismaSekli = FormParse.Str(f, "calismaSekli"),
        OzelIletisim = FormParse.Str(f, "ozelIletisim"),
        Ucret = FormParse.Dec(FormParse.Str(f, "ucret")),
        CikisLokasyon = FormParse.Str(f, "cikisLokasyon"),
        MinGun = FormParse.Int(FormParse.Str(f, "minGun")),
        ManSuresi = FormParse.Int(FormParse.Str(f, "manSuresi")),
        Drop2 = FormParse.Dec(FormParse.Str(f, "drop2")),
        Aktif = aktif
    };

    /// <summary>Filtre parametrelerini koruyarak dön — kullanıcı baktığı süzgeçte kalır.</summary>
    private static string Geri(HttpRequest req, string? hata = null)
    {
        var q = new List<string>();
        foreach (var ad in new[] { "fDonus", "fCikis", "fSube", "fAktif" })
        {
            var v = FormParse.Str(req.Form, ad);
            if (v is not null) q.Add($"{ad}={Uri.EscapeDataString(v)}");
        }
        if (hata is not null) q.Add($"hata={Uri.EscapeDataString(hata)}");
        return "/drop-tanimlari" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }

    private static async Task<IResult> Run(HttpRequest req, Func<Task> action)
    {
        try { await action(); return Results.Redirect(Geri(req)); }
        catch (ValidationException ex) { return Results.Redirect(Geri(req, ex.Message)); }
    }
}
