using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Domain.Enums;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Customers;

/// <summary>
/// Cari create/update/delete form post uçları. Tenant HttpContext claim'inden (RLS).
/// Çok sayıda opsiyonel alan (CRM parite zenginleştirme dahil) boş "" ile [FromForm] tipli bind 400
/// vermesin diye tüm form <see cref="IFormCollection"/>'dan okunup FormParse/Enum.TryParse ile çevrilir.
/// NOT: PR #1/#2 smoke kolaylığı için antiforgery devre dışı — üretimde açılmalı.
/// </summary>
public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/cariler").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        group.MapPost("/create", async (CustomerService svc, HttpRequest req) =>
        {
            try { await svc.CreateAsync(Build(req.Form)); return Sonuc.Tamam("/cariler", "Cari kaydedildi."); }
            catch (ValidationException ex) { return Results.Redirect($"/cariler?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        group.MapPost("/update", async (CustomerService svc, HttpRequest req, [FromForm] Guid id) =>
        {
            try
            {
                var ok = await svc.UpdateAsync(id, Build(req.Form));
                return ok ? Sonuc.Tamam("/cariler", "Cari güncellendi.") : Results.NotFound();
            }
            catch (ValidationException ex) { return Results.Redirect($"/cariler/{id}?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        group.MapPost("/delete", async (CustomerService svc, [FromForm] Guid id) =>
        {
            await svc.DeleteAsync(id);
            return Sonuc.Tamam("/cariler", "Cari silindi.");
        }).RequirePermission(Permission.OperationsDelete);

        return app;
    }

    private static CustomerInput Build(IFormCollection f) => new()
    {
        Tip = ParseEnum<CustomerType>(FormParse.Str(f, "tip")) ?? CustomerType.Bireysel,
        Ad = FormParse.Str(f, "ad"),
        Soyad = FormParse.Str(f, "soyad"),
        TcKimlik = FormParse.Str(f, "tcKimlik"),
        Unvan = FormParse.Str(f, "unvan"),
        VergiDairesi = FormParse.Str(f, "vergiDairesi"),
        VergiNo = FormParse.Str(f, "vergiNo"),
        CepTel = FormParse.Str(f, "cepTel"),
        Gsm2 = FormParse.Str(f, "gsm2"),
        Email = FormParse.Str(f, "email"),
        Il = FormParse.Str(f, "il"),
        Ilce = FormParse.Str(f, "ilce"),
        Adres = FormParse.Str(f, "adres"),
        Kaynak = FormParse.Str(f, "kaynak"),
        MusteriTemsilcisi = FormParse.Str(f, "musteriTemsilcisi"),
        IysIzinli = BoolReq(f, "iysIzinli"),
        Uyari = BoolReq(f, "uyari"),
        UyariNedeni = FormParse.Str(f, "uyariNedeni"),
        EhliyetNo = FormParse.Str(f, "ehliyetNo"),
        EhliyetSinifi = FormParse.Str(f, "ehliyetSinifi"),
        EhliyetTarihi = FormParse.Date(FormParse.Str(f, "ehliyetTarihi")),
        EhliyetYeri = FormParse.Str(f, "ehliyetYeri"),
        Tarife = FormParse.Str(f, "tarife"),
        VadeGun = FormParse.Int(FormParse.Str(f, "vadeGun")) ?? 0,
        RiskLimiti = FormParse.Dec(FormParse.Str(f, "riskLimiti")) ?? 0m,
        RiskMesaji = FormParse.Str(f, "riskMesaji"),
        RiskTarihi = FormParse.Date(FormParse.Str(f, "riskTarihi")),
        HgsYansitmaTuru = FormParse.Str(f, "hgsYansitmaTuru"),
        OzelCariTip = FormParse.Str(f, "ozelCariTip"),
        MusteriTipi = FormParse.Str(f, "musteriTipi"),
        EhliyetUlke = FormParse.Str(f, "ehliyetUlke"),
        Dil = FormParse.Str(f, "dil"),
        Doviz = FormParse.Str(f, "doviz"),
        TevkifatDurum = FormParse.Str(f, "tevkifatDurum"),
        KaraListe = BoolReq(f, "karaListe"),
        Pasif = BoolReq(f, "pasif"),
        // CRM parite zenginleştirme
        Sinif = FormParse.Str(f, "sinif"),
        MailIzin = BoolN(f, "mailIzin"),
        SmsIzin = BoolN(f, "smsIzin"),
        TelefonIzin = BoolN(f, "telefonIzin"),
        DogumTarihi = FormParse.Date(FormParse.Str(f, "dogumTarihi")),
        BabaAdi = FormParse.Str(f, "babaAdi"),
        AnaAdi = FormParse.Str(f, "anaAdi"),
        PasaportNo = FormParse.Str(f, "pasaportNo"),
        FaturaDonemi = FormParse.Str(f, "faturaDonemi"),
        TevkifatOrani = FormParse.Dec(FormParse.Str(f, "tevkifatOrani")),
        Kisiler = ParseKisiler(f), // PR-E: değişken sayıda yetkili kişi (kisi[i].*)
        // roadmap K4 — KVKK + ek adres/banka/fatura adresi
        KvkkOnay = BoolN(f, "kvkkOnay"),
        KvkkOnayTarih = FormParse.Date(FormParse.Str(f, "kvkkOnayTarih")),
        EkAdres = FormParse.Str(f, "ekAdres"),
        BankaIban = FormParse.Str(f, "bankaIban"),
        BankaAdi = FormParse.Str(f, "bankaAdi"),
        FaturaAdresi = FormParse.Str(f, "faturaAdresi"),
        FaturaUnvan = FormParse.Str(f, "faturaUnvan"),
        // FAZ-40 derinlik. Checkbox işaretsizken tarayıcı alanı HİÇ göndermez → varlık kontrolü.
        Ulke = FormParse.Str(f, "ulke"),
        Tel2 = FormParse.Str(f, "tel2"),
        OzelKod = FormParse.Str(f, "ozelKod"),
        EntegrasyonKodu = FormParse.Str(f, "entegrasyonKodu"),
        RiskIzin = FormParse.Str(f, "riskIzin"),
        DogumYeri = FormParse.Str(f, "dogumYeri"),
        PasaportYeri = FormParse.Str(f, "pasaportYeri"),
        KurumsalNo = FormParse.Str(f, "kurumsalNo"),
        TevkifatKodu = FormParse.Str(f, "tevkifatKodu"),
        FaturaKiralayanIsim = FormParse.Str(f, "faturaKiralayanIsim"),
        IsTelefonu = FormParse.Str(f, "isTelefonu"),
        KayitliIl = FormParse.Str(f, "kayitliIl"),
        KayitliIlce = FormParse.Str(f, "kayitliIlce"),
        MahalleKoy = FormParse.Str(f, "mahalleKoy"),
        SeriNo = FormParse.Str(f, "seriNo"),
        CiltNo = FormParse.Str(f, "ciltNo"),
        AileSira = FormParse.Str(f, "aileSira"),
        SiraNo = FormParse.Str(f, "siraNo"),
        IsAdresi = FormParse.Str(f, "isAdresi"),
        Aciklama = FormParse.Str(f, "aciklama"),
        UyariSerbest = FormParse.Str(f, "uyariSerbest"),
        PasaportTarihi = FormParse.Date(FormParse.Str(f, "pasaportTarihi")),
        KaraZamani = FormParse.Date(FormParse.Str(f, "karaZamani")),
        BayiKomisyon = FormParse.Dec(FormParse.Str(f, "bayiKomisyon")),
        WebIndirim = FormParse.Dec(FormParse.Str(f, "webIndirim")),
        Sifre = FormParse.Str(f, "sifre"),
        TcDogrulama = f.ContainsKey("tcDogrulama"),
        FaturaAdresFarkli = f.ContainsKey("faturaAdresFarkli"),
        FaturaTekSatir = f.ContainsKey("faturaTekSatir"),
        DogumGunuTakip = f.ContainsKey("dogumGunuTakip"),
        BakiyeGor = f.ContainsKey("bakiyeGor"),
        AracVerilmez = f.ContainsKey("aracVerilmez"),
        YasEhliyetSerbest = f.ContainsKey("yasEhliyetSerbest"),
        MerkezKurumsal = f.ContainsKey("merkezKurumsal"),
        Broker = f.ContainsKey("broker"),
        FindexZorunlu = f.ContainsKey("findexZorunlu"),
        AnonimAd = f.ContainsKey("anonimAd"),
        AnonimTc = f.ContainsKey("anonimTc"),
        AnonimTelefon = f.ContainsKey("anonimTelefon"),
        AnonimMail = f.ContainsKey("anonimMail"),
        AnonimAdres = f.ContainsKey("anonimAdres"),
        AnonimBelge = f.ContainsKey("anonimBelge")
    };


    /// <summary>Checkbox: "true"/"on" işaretli → true; yoksa/boş → false.</summary>
    /// <summary>PR-E: değişken sayıda yetkili kişi — kisi[i].adSoyad/telefon/mail/gorev. AdSoyad'sız satır atlanır
    /// (seyrek indeksler için continue). Üst sınır 30 (kaba DoS koruması).</summary>
    private static List<CustomerContactInput> ParseKisiler(IFormCollection f)
    {
        var list = new List<CustomerContactInput>();
        for (var i = 0; i < 30; i++)
        {
            var ad = FormParse.Str(f, $"kisi[{i}].adSoyad");
            if (string.IsNullOrWhiteSpace(ad)) continue;
            list.Add(new CustomerContactInput
            {
                AdSoyad = ad,
                Telefon = FormParse.Str(f, $"kisi[{i}].telefon"),
                Mail = FormParse.Str(f, $"kisi[{i}].mail"),
                Gorev = FormParse.Str(f, $"kisi[{i}].gorev")
            });
        }
        return list;
    }

    private static bool BoolReq(IFormCollection f, string key)
    {
        var v = FormParse.Str(f, key);
        return v is "true" or "True" or "on";
    }

    /// <summary>3 durumlu nullable bool select: boş → null, "true" → true, diğer dolu → false.</summary>
    private static bool? BoolN(IFormCollection f, string key)
    {
        var v = FormParse.Str(f, key);
        if (v is null) return null;
        return v is "true" or "True" or "on" or "evet" or "Evet";
    }

    /// <summary>Boş/whitespace/geçersiz → null; aksi halde enum değeri.</summary>
    private static T? ParseEnum<T>(string? s) where T : struct, Enum
        => Enum.TryParse<T>((s ?? string.Empty).Trim(), out var v) ? v : null;
}
