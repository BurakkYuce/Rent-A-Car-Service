using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Personnel;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Personnel;

/// <summary>Personel master form post uçları (roadmap C1). PII/HR → ManageUsers (Admin). Opsiyonel
/// tarih/sayısal alanlar boş "" bind 400 vermesin diye IFormCollection + FormParse.</summary>
public static class PersonelEndpoints
{
    public static IEndpointRouteBuilder MapPersonelEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/personel").RequirePermission(Permission.ManageUsers).AntiforgeryByEnv();

        grp.MapPost("/create", async (PersonnelService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(req.Form)), "Kayıt eklendi."));

        grp.MapPost("/update", async (PersonnelService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form)), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (PersonnelService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        return app;
    }

    private static PersonelInput Build(IFormCollection f) => new()
    {
        Kod = f["kod"].ToString(),
        Ad = f["ad"].ToString(),
        Soyad = f["soyad"].ToString(),
        TcKimlik = FormParse.Str(f, "tcKimlik"),
        IseGiris = FormParse.Date(FormParse.Str(f, "iseGiris")),
        IseCikis = FormParse.Date(FormParse.Str(f, "iseCikis")),
        SurucuBelgeNo = FormParse.Str(f, "surucuBelgeNo"),
        Maas = FormParse.Dec(FormParse.Str(f, "maas")),
        Sube = FormParse.Str(f, "sube"),
        // FAZ-40 derinlik
        GorevTanimi = FormParse.Str(f, "gorevTanimi"),
        Adres = FormParse.Str(f, "adres"),
        EvTelefonu = FormParse.Str(f, "evTelefonu"),
        IsTelefonu = FormParse.Str(f, "isTelefonu"),
        CepTel = FormParse.Str(f, "cepTel"),
        MailAdresi = FormParse.Str(f, "mailAdresi"),
        Referans = FormParse.Str(f, "referans"),
        Aciklama = FormParse.Str(f, "aciklama"),
        SSinifi = FormParse.Str(f, "sSinifi"),
        SVerilisYeri = FormParse.Str(f, "sVerilisYeri"),
        DogumYeri = FormParse.Str(f, "dogumYeri"),
        BabaAdi = FormParse.Str(f, "babaAdi"),
        AnaAdi = FormParse.Str(f, "anaAdi"),
        Il = FormParse.Str(f, "il"),
        Ilce = FormParse.Str(f, "ilce"),
        Mahalle = FormParse.Str(f, "mahalle"),
        CiltNo = FormParse.Str(f, "ciltNo"),
        AileSiraNo = FormParse.Str(f, "aileSiraNo"),
        SiraNo = FormParse.Str(f, "siraNo"),
        KanGrubu = FormParse.Str(f, "kanGrubu"),
        RacTabletNo = FormParse.Str(f, "racTabletNo"),
        SVerilisTarihi = FormParse.Date(FormParse.Str(f, "sVerilisTarihi")),
        DogumTarihi = FormParse.Date(FormParse.Str(f, "dogumTarihi")),
        Aktif = (FormParse.Str(f, "aktif") ?? "true") is "true" or "True"
    };


    private static async Task<IResult> Run(Func<Task> action, string mesaj)
    {
        try { await action(); return Sonuc.Tamam("/personel", mesaj); }
        catch (ValidationException ex) { return Results.Redirect($"/personel?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
