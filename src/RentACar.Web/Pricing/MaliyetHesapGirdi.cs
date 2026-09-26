using RentACar.Application.Pricing;
using RentACar.Domain.Enums;

namespace RentACar.Web.Pricing;

/// <summary>
/// Form/querystring alanlarını <see cref="MaliyetHesapInput"/>'a çeviren TEK kaynak (FAZ-74).
///
/// <para><b>Neden tek yerde:</b> hesap ekranı (GET, önizleme) ile kaydetme ucu (POST) aynı alan
/// adlarını okur. İki ayrı ayrıştırma yazılsaydı önizlemedeki rakamla kaydedilen rakam sessizce
/// ayrışabilirdi ("önizleme == kayıt" ilkesi).</para>
///
/// <para>Boş/geçersiz alanlar <c>null</c>'a düşer ve alan varsayılanı uygulanır (nullable
/// <c>decimal?</c>/<c>int?</c> parametreler boş string ile 400 verirdi — CLAUDE.md §5 tuzağı).</para>
/// </summary>
public static class MaliyetHesapGirdi
{
    /// <param name="oku">Alan adı → ham metin (form ya da querystring).</param>
    public static MaliyetHesapInput Kur(Func<string, string?> oku)
    {
        decimal D(string ad, decimal varsayilan) => FormParse.Dec(oku(ad)) ?? varsayilan;

        return new MaliyetHesapInput
        {
            AlisBedeli = D("alisBedeli", 0m),
            ResidualYuzde = D("residual", 0.30m),
            SureAy = FormParse.Int(oku("sureAy")) ?? 36,
            FaizOran = D("faiz", 0m),
            KkdfOran = D("kkdf", 0.15m),
            BsmvOran = D("bsmv", 0.15m),
            DamgaOran = D("damga", 0m),
            AylikGider = D("aylikGider", 0m),
            KarMarji = D("kar", 0.20m),
            KdvOran = D("kdv", 0.20m),

            KaskoYillik = D("kasko", 0m),
            TrafikSigortasiYillik = D("trafik", 0m),
            MtvYillik = D("mtv", 0m),
            BakimYillik = D("bakim", 0m),
            LastikYillik = D("lastik", 0m),
            LastikKisYillik = D("lastikKis", 0m),
            AracTakipYillik = D("takip", 0m),
            TescilPlakaYillik = D("tescil", 0m),
            MuayeneEmisyonYillik = D("muayene", 0m),
            YedekAracYillik = D("yedek", 0m),
            YonetimGideriAylik = D("yonetim", 0m),
            BankaDosyaDigerMasraf = D("dosya", 0m),
            EnflasyonOran = D("enflasyon", 0m),

            // Tanınmayan değer sessizce Eşit Taksitli'ye DÜŞMEZ; enum'a çevrilemeyen metin
            // varsayılana düşer ama "Rotatif" yazımı korunur → servis güvenli-red verir.
            KrediHesaplamaSekli = Enum.TryParse<LoanCalculationMethod>(oku("kredi"), ignoreCase: true, out var k)
                ? k : LoanCalculationMethod.EsitTaksitli,
            AracSayisi = FormParse.Int(oku("adet")) ?? 1
        };
    }

    /// <summary>Alan adları — hesap ekranının gizli alanları ve kayıt ucu AYNI listeyi kullanır.</summary>
    public static readonly string[] AlanAdlari =
    [
        "alisBedeli", "residual", "sureAy", "faiz", "kkdf", "bsmv", "damga", "aylikGider", "kar", "kdv",
        "kasko", "trafik", "mtv", "bakim", "lastik", "lastikKis", "takip", "tescil", "muayene", "yedek",
        "yonetim", "dosya", "enflasyon", "kredi", "adet"
    ];
}
