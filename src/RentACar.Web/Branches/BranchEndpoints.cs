using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Domain.Enums;

using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Branches;

/// <summary>Şube yönetimi form uçları — yalnız Admin (RequireRole + servis guard çift savunma).</summary>
public static class BranchEndpoints
{
    public static IEndpointRouteBuilder MapBranchEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/subeler")
            .RequirePermission(RentACar.Application.Authorization.Permission.ManageUsers) // sayfa politikasıyla hizalı (etkin izin)
            .AntiforgeryByEnv();

        // FAZ-23: alan sayısı 30'a çıktı → pozisyonel imza yerine form koleksiyonu (diğer
        // uçlardaki desen). Opsiyonel sayısal/tarih alanları FormParse ile çevrilir.
        grp.MapPost("/create", async (BranchService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(req.Form, varsayilanAktif: true)), "Kayıt eklendi."));

        grp.MapPost("/update", async (BranchService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form, varsayilanAktif: null)), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (BranchService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        // ---- FAZ-23: şubeye özel ücretsiz hizmet ----
        grp.MapPost("/hizmet-ekle", async (BranchService svc, HttpRequest req) =>
            await Run(() => svc.AddServiceAsync(new RentACar.Application.Branches.SubeUcretsizHizmetInput
            {
                SubeId = FormParse.Id(FormParse.Str(req.Form, "subeId")) ?? Guid.Empty,
                HizmetAdi = req.Form["hizmetAdi"].ToString(),
                Aciklama = FormParse.Str(req.Form, "aciklama")
            }), "İşlem tamamlandı."));

        grp.MapPost("/hizmet-sil", async (BranchService svc, [FromForm] Guid id) =>
            await Run(() => svc.RemoveServiceAsync(id), "İşlem tamamlandı."));

        // ---- FAZ-23: şube birleştirme ----
        // Onay kutusu ZORUNLU: geri alınamayan toplu bir işlem, kazara tıklamayla çalışmamalı.
        grp.MapPost("/birlestir", async (BranchService svc, HttpRequest req) =>
        {
            var kaynak = FormParse.Id(FormParse.Str(req.Form, "kaynakId")) ?? Guid.Empty;
            var hedef = FormParse.Id(FormParse.Str(req.Form, "hedefId")) ?? Guid.Empty;
            if (FormParse.Str(req.Form, "onay") is not ("true" or "on" or "True"))
                return Results.Redirect("/subeler?hata=" + Uri.EscapeDataString("Birleştirme için onay kutusunu işaretleyin."));
            try
            {
                var n = await svc.MergeAsync(kaynak, hedef);
                return Results.Redirect($"/subeler?bilgi={Uri.EscapeDataString($"{n} kayıt taşındı; kaynak şube pasife alındı.")}");
            }
            catch (ValidationException ex) { return Results.Redirect($"/subeler?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        return app;
    }

    private static RentACar.Application.Branches.BranchInput Build(IFormCollection f, bool? varsayilanAktif) => new()
    {
        Kod = f["kod"].ToString(),
        Ad = f["ad"].ToString(),
        Adres = FormParse.Str(f, "adres"),
        Telefon = FormParse.Str(f, "telefon"),
        Eposta = FormParse.Str(f, "eposta"),
        Il = FormParse.Str(f, "il"),
        Ilce = FormParse.Str(f, "ilce"),
        Yetkili = FormParse.Str(f, "yetkili"),
        CalismaSaatleri = FormParse.Str(f, "calismaSaatleri"),
        KomisyonOran = FormParse.Dec(FormParse.Str(f, "komisyonOran")),
        EvrakNoOnek = FormParse.Str(f, "evrakNoOnek"),
        // FAZ-23 derinlik alanları
        WebIsim = FormParse.Str(f, "webIsim"),
        FirmaUnvani = FormParse.Str(f, "firmaUnvani"),
        WebRezOncesiSaat = FormParse.Int(FormParse.Str(f, "webRezOncesiSaat")),
        Enlem = FormParse.Dec(FormParse.Str(f, "enlem")),
        Boylam = FormParse.Dec(FormParse.Str(f, "boylam")),
        HizmetKomisyonOran = FormParse.Dec(FormParse.Str(f, "hizmetKomisyonOran")),
        RezervasyonRengi = FormParse.Str(f, "rezervasyonRengi"),
        AlisSubesiDegilMi = FormParse.Str(f, "alisSubesiDegilMi") is "true" or "on" or "True",
        WebSira = FormParse.Int(FormParse.Str(f, "webSira")),
        WebOtoparkId = FormParse.Str(f, "webOtoparkId"),
        BayiCariKod = FormParse.Str(f, "bayiCariKod"),
        BayiOfisId = FormParse.Str(f, "bayiOfisId"),
        KomisyonHesabi = FormParse.Str(f, "komisyonHesabi"),
        OnlineRezId = FormParse.Str(f, "onlineRezId"),
        SozlesmeNoFormati = FormParse.Str(f, "sozlesmeNoFormati"),
        NakitHesapId = FormParse.Id(FormParse.Str(f, "nakitHesapId")),
        BankaHesapId = FormParse.Id(FormParse.Str(f, "bankaHesapId")),
        EntegrasyonKodu = FormParse.Str(f, "entegrasyonKodu"),
        ResimDosyasi = FormParse.Str(f, "resimDosyasi"),
        HaftalikCalismaSaatleri = FormParse.Str(f, "haftalikCalismaSaatleri"),
        Aktif = varsayilanAktif ?? ((FormParse.Str(f, "aktif") ?? "true") is "true" or "True")
    };

    private static async Task<IResult> Run(Func<Task> action, string mesaj)
    {
        try
        {
            await action();
            return Sonuc.Tamam("/subeler", mesaj);
        }
        catch (ValidationException ex)
        {
            return Results.Redirect($"/subeler?hata={Uri.EscapeDataString(ex.Message)}");
        }
    }
}
