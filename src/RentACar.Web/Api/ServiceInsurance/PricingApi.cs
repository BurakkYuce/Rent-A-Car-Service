using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Pricing;
using RentACar.Application.RateMatrices;
using RentACar.Domain.Enums;
using RentACar.Web.Api.AracFinans;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Identity;
using RentACar.Web.Import;
using S = RentACar.Web.Api.ServiceInsurance.ServiceInsuranceShared;

namespace RentACar.Web.Api.ServiceInsurance;

/// <summary>
/// F9.1 — <c>/fiyat-hesapla</c>, <c>/maliyet-hesapla</c>, <c>/maliyet-teklifleri</c>, <c>/tarife-aktar</c>. Hesaplar SUNUCU
/// motorundan (<see cref="RentalQuoteEngine"/>, <see cref="MaliyetHesapService"/>); hiçbiri deftere yazmaz.
/// <para><b>İzin:</b> fiyat hesapla OperationsWrite ∨ ViewReports (Blazor sayfası dört role açıktı, POST ViewReports
/// istiyordu → operatör 403 alıyordu); maliyet hesapla/teklif yazma FinanceWrite (maliyet/kâr marjı ticari sır),
/// teklif okuma FinanceWrite ∨ ViewReports; tarife aktar ManageUsers (toplu fiyat yazımı; Blazor ile aynı).</para>
/// <para><b>Onay çiti:</b> içe aktarılan satırlar <c>Bekliyor</c> girer (dosyadaki onay kolonları yok sayılır, motor
/// onaysızı kullanmaz); kanal toplu silme YALNIZ bekleyenleri siler, durum parametresi dışarıdan alınmaz.</para>
/// </summary>
internal static partial class PricingApi
{
    private const long ImportRequestLimit = 5 * 1024 * 1024;

    public static void Map(RouteGroupBuilder v1)
    {
        v1.MapGroup("").MapPost("/fiyat-hesapla", Quote).WithTags("Fiyat & Tarife").AlanlariEsle(QuoteRules)
            .RequireAnyPermission(Permission.OperationsWrite, Permission.ViewReports);
        v1.MapGroup("").MapPost("/maliyet-hesapla", Cost).WithTags("Fiyat & Tarife").AlanlariEsle(CostRules)
            .RequirePermission(Permission.FinanceWrite);
        MapCostOffers(v1);

        var imp = v1.MapGroup("/tarife-aktar").WithTags("Fiyat & Tarife").RequirePermission(Permission.ManageUsers);
        imp.MapGet("", ImportView).AlanlariEsle(F5Ortak.SiralamaKurallari);
        imp.MapPost("/yukle", Upload).DisableAntiforgery() // CSRF: group header filter (X-XSRF-TOKEN)
            .WithMetadata(new RequestSizeLimitAttribute(ImportRequestLimit));
        imp.MapPost("/kanal-sil", DeleteChannel).AlanlariEsle([("Silinecek rezervasyon kaynağı", "kanal"), ("Toplu silme", "kanal")]);
    }

    // ------------------------------------------------------------------ fiyat hesapla

    private static readonly (string, string)[] QuoteRules =
    [
        ("Araç grubu", "aracGrupKod"), ("Kampanya kodu", "kampanyaKodu"), ("'", "kampanyaKodu"),
        ("Bitiş", "bitTar"), ("Başlangıç", "basTar"), ("Sürücü yaşı", "surucuYas"), ("Tahmini KM", "tahminiKm"),
    ];

    private static async Task<Ok<PriceQuoteDto>> Quote(PriceQuoteRequest r, RentalQuoteEngine engine, CancellationToken ct)
    {
        var group = F5Ortak.Nz(r.AracGrupKod) ?? throw new ValidationException("Araç grubu seçilmelidir.", "aracGrupKod");
        S.Text(group, 32, "aracGrupKod"); S.Text(r.Kanal, 64, "kanal"); S.Text(r.Sube, 64, "sube");
        S.Text(r.MusteriSegment, 64, "musteriSegment"); S.Text(r.KampanyaKodu, 64, "kampanyaKodu");
        var start = S.RequiredDate(r.BasTar, "basTar");
        var end = S.RequiredDate(r.BitTar, "bitTar");
        if (end <= start) throw new ValidationException("Bitiş başlangıçtan sonra olmalıdır.", "bitTar");
        if ((end - start).TotalDays > 3650) throw new ValidationException("Kiralama süresi en fazla 10 yıl olabilir.", "bitTar");
        S.IntRange(r.SurucuYas, 16, 120, "surucuYas");
        S.IntRange(r.TahminiKm, 0, 10_000_000, "tahminiKm");
        var codes = (r.SigortaUrunKodlari ?? []).Select(F5Ortak.Nz).Where(c => c is not null).Select(c => c!).Distinct().ToList();
        if (codes.Count > 50 || codes.Any(c => c.Length > 32))
            throw new ValidationException("Sigorta ürün kodları geçersiz (en çok 50 kod, her biri en çok 32 karakter).", "sigortaUrunKodlari");
        var q = await engine.QuoteAsync(new QuoteRequest
        {
            AracGrupKod = group, Kanal = F5Ortak.Nz(r.Kanal), Sube = F5Ortak.Nz(r.Sube), BasTar = start, BitTar = end,
            SurucuYas = r.SurucuYas, TahminiKm = r.TahminiKm, SigortaUrunKodlari = codes,
            MusteriSegment = F5Ortak.Nz(r.MusteriSegment), KampanyaKodu = F5Ortak.Nz(r.KampanyaKodu),
        }, ct);
        return TypedResults.Ok(PriceQuoteDto.From(q));
    }

    // ------------------------------------------------------------------ maliyet hesapla (salt hesap)

    private static (string, string)[] CostFieldRules(string prefix) =>
    [
        ("Alış bedeli", prefix + "alisBedeli"), ("Süre", prefix + "sureAy"), ("Araç sayısı", prefix + "aracSayisi"),
        ("Kalıntı değer", prefix + "residualYuzde"), ("Kâr marjı", prefix + "karMarji"), ("KDV oranı", prefix + "kdvOran"),
        ("Enflasyon", prefix + "enflasyonOran"), ("Bilinmeyen kredi", prefix + "krediHesaplamaSekli"),
        ("Teklif başlığı", "baslik"), ("Teklif tarihi", "tarih"), ("Cari bulunamadı", "cariId"), ("Personel", "hazirlayanId"),
    ];

    private static readonly (string, string)[] CostRules = CostFieldRules("");

    private static Ok<CostResultDto> Cost(CostInputDto r)
        => TypedResults.Ok(CostResultDto.From(MaliyetHesapService.Hesapla(CostInput(r, ""))));

    /// <summary>Request → service input. Amounts: 2 decimals and below 10^15; rates (fractions): 4 decimals, |x| &lt; 1000.</summary>
    internal static MaliyetHesapInput CostInput(CostInputDto r, string prefix)
    {
        void Amount(decimal? v, string f) => S.RecordAmount(v, prefix + f);
        void Rate(decimal? v, string f)
        {
            if (v is not { } x) return;
            if (Math.Abs(x) >= 1000m) throw new ValidationException("Oran çok büyük.", prefix + f);
            AracFinansOrtak.EnsureMaxScale(x, 4, prefix + f);
        }
        Amount(r.AlisBedeli, "alisBedeli"); Amount(r.KaskoYillik, "kaskoYillik"); Amount(r.TrafikSigortasiYillik, "trafikSigortasiYillik");
        Amount(r.MtvYillik, "mtvYillik"); Amount(r.BakimYillik, "bakimYillik"); Amount(r.LastikYillik, "lastikYillik");
        Amount(r.LastikKisYillik, "lastikKisYillik"); Amount(r.AracTakipYillik, "aracTakipYillik");
        Amount(r.TescilPlakaYillik, "tescilPlakaYillik"); Amount(r.MuayeneEmisyonYillik, "muayeneEmisyonYillik");
        Amount(r.YedekAracYillik, "yedekAracYillik"); Amount(r.YonetimGideriAylik, "yonetimGideriAylik");
        Amount(r.AylikGider, "aylikGider"); Amount(r.BankaDosyaDigerMasraf, "bankaDosyaDigerMasraf");
        Rate(r.ResidualYuzde, "residualYuzde"); Rate(r.FaizOran, "faizOran"); Rate(r.KkdfOran, "kkdfOran");
        Rate(r.BsmvOran, "bsmvOran"); Rate(r.DamgaOran, "damgaOran"); Rate(r.KarMarji, "karMarji"); Rate(r.KdvOran, "kdvOran");
        Rate(r.EnflasyonOran, "enflasyonOran");
        S.IntRange(r.SureAy, 1, 600, prefix + "sureAy");
        S.IntRange(r.AracSayisi, 1, 100_000, prefix + "aracSayisi");
        var d = new MaliyetHesapInput();
        return new MaliyetHesapInput
        {
            AlisBedeli = r.AlisBedeli ?? 0m, ResidualYuzde = r.ResidualYuzde ?? d.ResidualYuzde, SureAy = r.SureAy ?? d.SureAy,
            FaizOran = r.FaizOran ?? 0m, KkdfOran = r.KkdfOran ?? d.KkdfOran, BsmvOran = r.BsmvOran ?? d.BsmvOran,
            DamgaOran = r.DamgaOran ?? 0m, KarMarji = r.KarMarji ?? d.KarMarji, KdvOran = r.KdvOran ?? d.KdvOran,
            EnflasyonOran = r.EnflasyonOran ?? 0m,
            KrediHesaplamaSekli = F5Ortak.EnumAdi<KrediHesaplamaSekli>(r.KrediHesaplamaSekli, prefix + "krediHesaplamaSekli")
                                  ?? KrediHesaplamaSekli.EsitTaksitli,
            AracSayisi = r.AracSayisi ?? 1, KaskoYillik = r.KaskoYillik ?? 0m, TrafikSigortasiYillik = r.TrafikSigortasiYillik ?? 0m,
            MtvYillik = r.MtvYillik ?? 0m, BakimYillik = r.BakimYillik ?? 0m, LastikYillik = r.LastikYillik ?? 0m,
            LastikKisYillik = r.LastikKisYillik ?? 0m, AracTakipYillik = r.AracTakipYillik ?? 0m,
            TescilPlakaYillik = r.TescilPlakaYillik ?? 0m, MuayeneEmisyonYillik = r.MuayeneEmisyonYillik ?? 0m,
            YedekAracYillik = r.YedekAracYillik ?? 0m, YonetimGideriAylik = r.YonetimGideriAylik ?? 0m,
            AylikGider = r.AylikGider ?? 0m, BankaDosyaDigerMasraf = r.BankaDosyaDigerMasraf ?? 0m,
        };
    }
}
