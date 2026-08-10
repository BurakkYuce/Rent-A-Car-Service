using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.ReservationSources;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.ReservationSources;

/// <summary>Rezervasyon kaynağı master form post uçları. OperationsWrite.</summary>
public static class ReservationSourceEndpoints
{
    public static IEndpointRouteBuilder MapReservationSourceEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/rezervasyon-kaynaklari").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (ReservationSourceService svc, HttpRequest req,
            [FromForm] string kod, [FromForm] string ad) =>
            await Run(() => svc.CreateAsync(Build(req.Form, kod, ad, aktif: true))));

        grp.MapPost("/update", async (ReservationSourceService svc, HttpRequest req, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form, kod, ad, aktif))));

        grp.MapPost("/delete", async (ReservationSourceService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        // FAZ-24 "Aşağıya Yansıt" — seçili kaynağın oranlarını diğer AKTİF kaynaklara kopyalar.
        // Yalnız bu tabloya yazar; kayıtlı rezervasyon/fatura/defter DEĞİŞMEZ.
        grp.MapPost("/yansit", async (ReservationSourceService svc, [FromForm] Guid id) =>
            await Run(() => svc.OranlariYansitAsync(id)));

        return app;
    }

    /// <summary>Oran/tutar/gün alanları OPSİYONEL decimal/int — boş string ("") gelirse [FromForm] 400
    /// verirdi, bu yüzden string olarak alınıp FormParse.Dec/Int ile çevriliyor (CLAUDE.md §5 tuzağı).
    /// Checkbox'lar işaretsizken form'a HİÇ gelmez → <see cref="Chk"/> false döner (master formda
    /// üçlü/null semantiği yok: tam-durum gönderilir).</summary>
    private static ReservationSourceInput Build(IFormCollection f, string kod, string ad, bool aktif) => new()
    {
        Kod = kod,
        Ad = ad,
        Aktif = aktif,
        Tedarikci = FormParse.Str(f, "tedarikci"),
        KiraOrani = FormParse.Dec(FormParse.Str(f, "kiraOrani")),
        HizmetOrani = FormParse.Dec(FormParse.Str(f, "hizmetOrani")),
        DropOrani = FormParse.Dec(FormParse.Str(f, "dropOrani")),

        // ---- FAZ-49 kural matrisi ----------------------------------------------------------
        KaynakGrubu = Grup(FormParse.Str(f, "kaynakGrubu")),

        // KURAL bayrakları (gerçekten uygulanır)
        Uzatamaz = Chk(f, "uzatamaz"),
        RezTarihleriDegisemez = Chk(f, "rezTarihleriDegisemez"),
        ProvizyonYok = Chk(f, "provizyonYok"),
        KmSinirsiz = Chk(f, "kmSinirsiz"),
        AyniYonDrop = Chk(f, "ayniYonDrop"),
        MaxGun = FormParse.Int(FormParse.Str(f, "maxGun")),

        // BİLGİ alanları (hiçbir hesaba girmez)
        MaliyetYansitma = Chk(f, "maliyetYansitma"),
        MatrisErken = Chk(f, "matrisErken"),
        MatrisGecikme = Chk(f, "matrisGecikme"),
        MatrisIptal = Chk(f, "matrisIptal"),
        MatrisNoShow = Chk(f, "matrisNoShow"),
        MatrisUzatma = Chk(f, "matrisUzatma"),
        SigortaKaynakNo = FormParse.Str(f, "sigortaKaynakNo"),
        DropKaynakNo = FormParse.Str(f, "dropKaynakNo"),
        ProvizyonSecenek = FormParse.Str(f, "provizyonSecenek"),
        MuafiyatSecenek = FormParse.Str(f, "muafiyatSecenek"),
        ScdwDahil = Chk(f, "scdwDahil"),
        CdwDahil = Chk(f, "cdwDahil"),
        LcfDahil = Chk(f, "lcfDahil"),
        PaiDahil = Chk(f, "paiDahil"),
        BebekKoltugu = FormParse.Dec(FormParse.Str(f, "bebekKoltugu")),
        Navigasyon = FormParse.Dec(FormParse.Str(f, "navigasyon")),
        EkSurucu = FormParse.Dec(FormParse.Str(f, "ekSurucu")),
        Wifi = FormParse.Dec(FormParse.Str(f, "wifi")),
        KomisyonOrani = FormParse.Dec(FormParse.Str(f, "komisyonOrani")),
        OnOdemeOrani = FormParse.Dec(FormParse.Str(f, "onOdemeOrani")),
        IndirimOrani = FormParse.Dec(FormParse.Str(f, "indirimOrani")),
        PuanOrani = FormParse.Dec(FormParse.Str(f, "puanOrani")),
        MailAdres = FormParse.Str(f, "mailAdres"),
        OtomatikMailGitme = Chk(f, "otomatikMailGitme"),
        RiskAnalizYapma = Chk(f, "riskAnalizYapma"),
        SubeGor = Chk(f, "subeGor"),
        AcenteFiyatDegistir = Chk(f, "acenteFiyatDegistir"),
        Gizle = Chk(f, "gizle"),
        SadeceMusteriOdeme = Chk(f, "sadeceMusteriOdeme")
    };

    /// <summary>Checkbox → bool. İşaretli kutu "true" (ya da tarayıcı varsayılanı "on") gönderir;
    /// işaretsiz kutu HİÇ gönderilmez → false.</summary>
    private static bool Chk(IFormCollection f, string key)
    {
        foreach (var s in f[key])
            if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "on") return true;
        return false;
    }

    /// <summary>Boş seçim → null ("belirtilmemiş"); tanınmayan değer de null (enjeksiyon sessizce yok sayılır).</summary>
    private static RezKaynakGrubu? Grup(string? s)
        => Enum.TryParse<RezKaynakGrubu>((s ?? "").Trim(), ignoreCase: true, out var g)
           && Enum.IsDefined(g) ? g : null;

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/rezervasyon-kaynaklari"); }
        catch (ValidationException ex) { return Results.Redirect($"/rezervasyon-kaynaklari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
