using Microsoft.AspNetCore.Mvc;
using RentACar.Application.AracSiparisleri;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.AracSiparisleri;

/// <summary>Araç sipariş/tedarik form post uçları (roadmap L3 + FAZ-17). OperationsWrite.
/// Opsiyonel sayısal/tarih/Guid alanlar boş "" bind'de 400 vermesin diye IFormCollection +
/// FormParse kullanılır.</summary>
public static class AracSiparisEndpoints
{
    /// <summary>Liste filtresi alanları — POST sonrası kullanıcı baktığı süzgeçte kalsın diye
    /// formlarda gizli alan olarak taşınır ve dönüş adresine geri yazılır (AracKredi deseni).</summary>
    private static readonly string[] FiltreAlanlari = ["cariF", "araF", "aracF", "dosyaF", "durumF", "bas", "bit"];

    public static IEndpointRouteBuilder MapAracSiparisEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/arac-siparis").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (AracSiparisService svc, HttpRequest req) =>
            await Durum(req, async () => await svc.CreateAsync(Build(req.Form)), "ok=1"));

        // FAZ-17: alan güncelleme (aynı ekranın "Düzenle" formu). Durum BURADAN değişmez.
        grp.MapPost("/update", async (AracSiparisService svc, HttpRequest req, [FromForm] Guid id) =>
            await Durum(req, async () => await svc.UpdateAsync(id, Build(req.Form)), "ok=1"));

        grp.MapPost("/onayla", async (AracSiparisService svc, HttpRequest req, [FromForm] Guid id) =>
            await Durum(req, () => svc.OnaylaAsync(id)));
        grp.MapPost("/teslim-al", async (AracSiparisService svc, HttpRequest req, [FromForm] Guid id) =>
            await Durum(req, () => svc.TeslimAlAsync(id)));
        grp.MapPost("/iptal", async (AracSiparisService svc, HttpRequest req, [FromForm] Guid id) =>
            await Durum(req, () => svc.IptalAsync(id)));

        return app;
    }

    /// <summary>Form → giriş eşlemesi TEK yerde: create ve update AYNI alan kümesini okur (biri
    /// eksik kalırsa o form kaydedildiğinde alan sessizce sıfırlanırdı — wire-in bütünlüğü).</summary>
    private static AracSiparisInput Build(IFormCollection f)
    {
        string? S(string k) => FormParse.Str(f, k);
        return new AracSiparisInput
        {
            Tedarikci = S("tedarikci"),
            TedarikciCariId = FormParse.Id(S("tedarikciCariId")),
            SiparisTarihi = FormParse.Date(S("siparisTarihi")),
            BeklenenTeslim = FormParse.Date(S("beklenenTeslim")),
            ImzaTarih = FormParse.Date(S("imzaTarih")),
            DosyaNo = S("dosyaNo"),
            SatisTemsilci = S("satisTemsilci"),
            OzelTemsilci = S("ozelTemsilci"),
            Marka = S("marka"),
            Tip = S("tip"),
            Grup = S("grup"),
            Versiyon = S("versiyon"),
            Opsiyon = S("opsiyon"),
            Renk = S("renk"),
            IcRenk = S("icRenk"),
            KaynakTip = S("kaynakTip"),
            SatisTipi = S("satisTipi"),
            TsbKayitNo = S("tsbKayitNo"),
            KrediId = FormParse.Id(S("krediId")),
            Adet = FormParse.Int(S("adet")) ?? 1,
            BirimFiyat = FormParse.Dec(S("birimFiyat")) ?? 0m,
            // Fiyat katmanları BİLGİ: boş bırakılabilir → null ("girilmemiş"), 0'a çevrilmez.
            PiyasaFiyat = FormParse.Dec(S("piyasaFiyat")),
            OpsFiyat = FormParse.Dec(S("opsFiyat")),
            FiloFiyat = FormParse.Dec(S("filoFiyat")),
            Doviz = S("doviz") ?? "TRY",
            Kur = FormParse.Dec(S("kur")) ?? 1m,
            Aciklama = S("aciklama")
        };
    }

    /// <summary>Filtreyi koruyarak dön. Adres SABİT ("/arac-siparis") — kullanıcı girdisi yol olarak
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
        return "/arac-siparis" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }

    private static async Task<IResult> Durum(HttpRequest req, Func<Task> action, string? basariEk = null)
    {
        try { await action(); return Results.Redirect(Geri(req, basariEk)); }
        catch (ValidationException ex) { return Results.Redirect(Geri(req, $"hata={Uri.EscapeDataString(ex.Message)}")); }
    }
}
