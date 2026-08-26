using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.ServiceRecords;
using RentACar.Domain.Enums;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.ServiceRecords;

/// <summary>
/// Servis/bakım form post uçları (kayıt + akış + kalem + FAZ-16 bilgi blokları). Tenant claim'inden (RLS).
/// <para>FAZ-16: kaza/fatura/ödeme uçları hiçbir mali işlem TETİKLEMEZ (KARARLAR.md) — bu yüzden
/// hepsi OperationsWrite grubundadır, FinanceWrite'a taşınmadı. Deftere yazan tek uç, aşağıdaki
/// ayrı FinanceWrite grubundaki rücu yansıtmasıdır.</para>
/// </summary>
public static class ServiceRecordEndpoints
{
    public static IEndpointRouteBuilder MapServiceRecordEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/servisler").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (ServiceRecordService svc, HttpRequest req) =>
        {
            var f = req.Form;
            var input = new ServiceRecordInput
            {
                VehicleId = FormParse.Id(FormParse.Str(f, "vehicleId")) ?? Guid.Empty,
                Tip = Enum.TryParse<ServisTipi>(FormParse.Str(f, "tip"), out var tip) ? tip : ServisTipi.Periyodik,
                GirisKm = FormParse.Int(FormParse.Str(f, "girisKm")) ?? 0,
                HasarSorumlu = Enum.TryParse<HasarSorumlu>(FormParse.Str(f, "hasarSorumlu"), out var hs) ? hs : HasarSorumlu.Yok,
                KusurOrani = FormParse.Dec(FormParse.Str(f, "kusurOrani")),
                // Checkbox: işaretliyse "on"/"true" gelir, işaretsizse alan HİÇ gelmez.
                Rezervasyon = FormParse.Str(f, "rezervasyon") is not null
            };
            BilgiOku(input, f);
            // Tek kalem satırı (opsiyonel): açıklama doluysa eklenir.
            if (FormParse.Str(f, "kalemAciklama") is { } ka)
                input.Lines.Add(KalemOku(f, ka));
            return await Run(() => svc.CreateAsync(input), "Kayıt eklendi.");
        });

        // FAZ-16 — BİLGİ blokları (kaza/fatura/ödeme/yakıt/plan). Defter etkisi YOK.
        grp.MapPost("/bilgi", async (ServiceRecordService svc, HttpRequest req) =>
        {
            var f = req.Form;
            var id = FormParse.Id(FormParse.Str(f, "id")) ?? Guid.Empty;
            var input = new ServiceRecordBilgiInput();
            BilgiOku(input, f);
            return await Run(() => svc.BilgiGuncelleAsync(id, input), "İşlem tamamlandı.");
        });

        // FAZ-16 — Rezerve → Açık ("Servise Al").
        grp.MapPost("/servise-al", (ServiceRecordService svc, [FromForm] Guid id, [FromForm] string? girisKm)
            => Run(() => svc.ServiseAlAsync(id, FormParse.Int(girisKm)), "İşlem tamamlandı."));

        grp.MapPost("/baslat", (ServiceRecordService svc, [FromForm] Guid id) => Run(() => svc.BaslatAsync(id), "İşlem tamamlandı."));
        grp.MapPost("/tamamla", (ServiceRecordService svc, [FromForm] Guid id, [FromForm] int cikisKm, [FromForm] string? sonrakiBakimKm)
            => Run(() => svc.TamamlaAsync(id, cikisKm, FormParse.Int(sonrakiBakimKm)), "Tamamlandı."));
        grp.MapPost("/iptal", (ServiceRecordService svc, [FromForm] Guid id) => Run(() => svc.IptalAsync(id), "İşlem iptal edildi.")).RequirePermission(Permission.OperationsDelete);
        grp.MapPost("/kalem", async (ServiceRecordService svc, HttpRequest req) =>
        {
            var f = req.Form;
            var id = FormParse.Id(FormParse.Str(f, "id")) ?? Guid.Empty;
            var kalem = KalemOku(f, FormParse.Str(f, "aciklama") ?? string.Empty);
            return await Run(() => svc.KalemEkleAsync(id, kalem), "İşlem tamamlandı.");
        });

        // Servis maliyeti rücu/yansıtma→defter (roadmap J4): FinanceWrite (mali işlem).
        var ode = app.MapGroup("/servis-yansitma").RequirePermission(Permission.FinanceWrite).AntiforgeryByEnv();
        ode.MapPost("/yansit", (ServiceRecordService svc, [FromForm] Guid id, [FromForm] Guid cariId)
            => Run(() => svc.YansitAsync(id, cariId), "Yansıtma yapıldı."));

        return app;
    }

    /// <summary>
    /// FAZ-16 bilgi blokları form → input. Opsiyonel decimal/int/tarih alanları <c>string?</c>
    /// okunup FormParse ile çevrilir (boş string ile [FromForm] 400 verir).
    /// </summary>
    private static void BilgiOku(ServiceRecordBilgiInput b, IFormCollection f)
    {
        b.AtolyeAdi = FormParse.Str(f, "atolyeAdi");
        b.Aciklama = FormParse.Str(f, "aciklama");

        b.BeyanTuru = FormParse.Str(f, "beyanTuru");
        b.KarsiPlaka = FormParse.Str(f, "karsiPlaka");
        b.KarsiTrafikSigortasi = FormParse.Str(f, "karsiTrafikSigortasi");
        b.KazaTarihi = FormParse.Date(FormParse.Str(f, "kazaTarihi"));
        b.KazaSorumlusu = FormParse.Str(f, "kazaSorumlusu");
        b.HasarDosyaNo = FormParse.Str(f, "hasarDosyaNo");
        b.DegerKaybi = FormParse.Dec(FormParse.Str(f, "degerKaybi"));

        b.FaturaTarihi = FormParse.Date(FormParse.Str(f, "faturaTarihi"));
        b.FaturaNo = FormParse.Str(f, "faturaNo");
        b.FaturaTutar = FormParse.Dec(FormParse.Str(f, "faturaTutar"));
        b.FaturaKdv = FormParse.Dec(FormParse.Str(f, "faturaKdv"));
        // faturaGenelToplam OKUNMAZ: sunucu türetir (matrah + KDV) — çelişkili üçüncü sayı olmasın.

        b.OdemeTarihi = FormParse.Date(FormParse.Str(f, "odemeTarihi"));
        b.Odeme = FormParse.Dec(FormParse.Str(f, "odeme"));
        b.OdemeDoviz = FormParse.Str(f, "odemeDoviz");
        b.OdemeKur = FormParse.Dec(FormParse.Str(f, "odemeKur"));
        b.OdemeTuru = Enum.TryParse<OdemeYontemi>(FormParse.Str(f, "odemeTuru"), out var oy) ? oy : null;
        b.KasaKodu = FormParse.Str(f, "kasaKodu");
        b.HesapNo = FormParse.Str(f, "hesapNo");

        b.CikisYakit = FormParse.Int(FormParse.Str(f, "cikisYakit"));
        b.DonusYakit = FormParse.Int(FormParse.Str(f, "donusYakit"));

        b.PlanBasTarihi = FormParse.Date(FormParse.Str(f, "planBasTarihi"));
        b.PlanBitTarihi = FormParse.Date(FormParse.Str(f, "planBitTarihi"));
    }

    private static ServiceLineInput KalemOku(IFormCollection f, string aciklama) => new()
    {
        Aciklama = aciklama,
        // Tutar BOŞ bırakılabilir → birim fiyat × miktar − indirim'den türetilir (null ≠ 0).
        Tutar = FormParse.Dec(FormParse.Str(f, "tutar")),
        BirimFiyat = FormParse.Dec(FormParse.Str(f, "birimFiyat")),
        Miktar = FormParse.Dec(FormParse.Str(f, "miktar")),
        Indirim = FormParse.Dec(FormParse.Str(f, "indirim")),
        KdvOran = FormParse.Dec(FormParse.Str(f, "kdvOran"))
    };

    private static async Task<IResult> Run(Func<Task> action, string mesaj)
    {
        try
        {
            await action();
            return Sonuc.Tamam("/servisler", mesaj);
        }
        catch (ValidationException ex)
        {
            return Results.Redirect($"/servisler?hata={Uri.EscapeDataString(ex.Message)}");
        }
    }
}
