using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.RentalRules;
using RentACar.Domain.Enums;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.RentalRules;

/// <summary>Kiralama kuralı master form post uçları. OperationsWrite. Opsiyonel sayısal/tarih alanlar
/// boş "" ile bind 400 vermesin diye IFormCollection'dan FormParse ile çevrilir (boş → null).</summary>
public static class RentalRuleEndpoints
{
    public static IEndpointRouteBuilder MapRentalRuleEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/kira-kurallari").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (RentalRuleService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(req.Form)), "Kayıt eklendi."));

        grp.MapPost("/update", async (RentalRuleService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form)), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (RentalRuleService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        return app;
    }

    private static RentalRuleInput Build(IFormCollection f) => new()
    {
        Kod = f["kod"].ToString(),
        Ad = f["ad"].ToString(),
        Aciklama = FormParse.Str(f, "aciklama"),
        Kanal = FormParse.Str(f, "kanal"),
        Sube = FormParse.Str(f, "sube"),
        AracGrupKod = FormParse.Str(f, "aracGrupKod"),
        MinGun = FormParse.Int(FormParse.Str(f, "minGun")),
        MaxGun = FormParse.Int(FormParse.Str(f, "maxGun")),
        Iskonto = FormParse.Dec(FormParse.Str(f, "iskonto")),
        HaftaSonuFarkOran = FormParse.Dec(FormParse.Str(f, "haftaSonuFarkOran")),
        SonraOdeOran = FormParse.Dec(FormParse.Str(f, "sonraOdeOran")),
        HediyeGun = FormParse.Int(FormParse.Str(f, "hediyeGun")),
        KampanyaMi = (FormParse.Str(f, "kampanyaMi")) is "true" or "True" or "on",
        KampanyaKodu = FormParse.Str(f, "kampanyaKodu"),
        MusteriSegment = FormParse.Str(f, "musteriSegment"),
        GecerlilikBas = FormParse.Date(FormParse.Str(f, "gecerlilikBas")),
        GecerlilikBit = FormParse.Date(FormParse.Str(f, "gecerlilikBit")),
        SartMetni = FormParse.Str(f, "sartMetni"),
        // ---- FAZ-46 ----
        TalepBas = FormParse.Date(FormParse.Str(f, "talepBas")),
        TalepBit = FormParse.Date(FormParse.Str(f, "talepBit")),
        PromosyonTuru = Enum.TryParse<PromotionType>(FormParse.Str(f, "promosyonTuru"), out var pt) ? pt : null,
        KuponGecerlilik = Enum.TryParse<CouponValidity>(FormParse.Str(f, "kuponGecerlilik"), out var kg) ? kg : null,
        HesaplamaTipi = Enum.TryParse<CalculationType>(FormParse.Str(f, "hesaplamaTipi"), out var ht) ? ht : null,
        HizliIslem = FormParse.Str(f, "hizliIslem") is "true" or "True" or "on",
        // 7 ayrı checkbox aynı adla gelir → virgülle birleştirilir; servis normalize/doğrular.
        HaftaGunKisiti = f["haftaGun"].Count == 0 ? null : string.Join(',', f["haftaGun"].ToArray()),
        // FAZ-73: form artık DURUMU gönderir; Aktif bayrağı servis tarafında ondan TÜRETİLİR
        // (tek senkron noktası). Durum gelmezse eski "aktif" alanına düşülür — geriye uyum.
        TarihTipi = Enum.TryParse<RuleDateType>(FormParse.Str(f, "tarihTipi"), out var tt)
            ? tt : RuleDateType.Rezervasyon,
        KampanyaDurum = Enum.TryParse<CampaignStatus>(FormParse.Str(f, "kampanyaDurum"), out var kd)
            ? kd : null,
        Aktif = (FormParse.Str(f, "aktif") ?? "true") is "true" or "True"
    };


    private static async Task<IResult> Run(Func<Task> action, string message)
    {
        try { await action(); return Result.Ok("/kira-kurallari", message); }
        catch (ValidationException ex) { return Results.Redirect($"/kira-kurallari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
