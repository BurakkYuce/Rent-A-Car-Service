using System.Globalization;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Api.Kira;

/// <summary>
/// F4.3b — kira yüzeylerinde (<c>GET /kiralar/{id}</c> müşteri adı + paylaşım barı + hazır mesaj, <c>GET
/// /kiralar/{id}/musteri-ozet</c>) müşteri kişisel verisinin TEK kuralı. Maskeleme ve KVKK anonimleştirme
/// (<c>Customer.Anonim*</c>) yalnız burada uygulanır; yüzeyler cariyi doğrudan okumaz (#262 adversarial M1 —
/// bayraklar önce yalnız özette, yarım uygulanıyordu).
/// <list type="bullet">
/// <item><b>TC kimlik</b> hiçbir yüzeyde dönmez (ne düz ne maskeli — Blazor paritesi, #262 kararı).</item>
/// <item><c>AnonimAd</c> → özette ad <c>null</c>; detayda "Anonim müşteri"; mesajda nötr hitap ("Sayın müşterimiz").</item>
/// <item><c>AnonimTelefon</c> → cep tel <c>null</c> (özet + paylaşım barı; WhatsApp ön-doldurması yok).</item>
/// <item><c>AnonimMail</c> → e-posta <c>null</c> (özet + paylaşım barı).</item>
/// <item><c>AnonimAdres</c> → adres, il, ilçe <c>null</c>.</item>
/// <item><c>AnonimBelge</c> → ehliyet/pasaport numarası VE belge üst bilgisi (sınıf, tarih, yer, ülke, pasaport yeri) <c>null</c>.</item>
/// </list>
/// </summary>
public static class MusteriGorunumu
{
    /// <summary><c>AnonimAd</c> işaretli carinin kira detayındaki görünen adı.</summary>
    public const string AnonimAdEtiketi = RentACar.Application.Customers.CariAnonimlik.AdEtiketi;

    /// <summary><c>AnonimAd</c> işaretli (ya da bulunamayan) cariye hazır mesajdaki nötr hitap.</summary>
    public const string NotrHitap = "müşterimiz";

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Kira detayındaki taraf adı (liste/başlık): anonimse sabit etiket, yoksa "—".</summary>
    public static string TarafAdi(Customer? m)
        => m is null ? "—" : m.AnonimAd ? AnonimAdEtiketi : m.DisplayName;

    /// <summary>
    /// Liste/pano satırlarındaki müşteri adı (satır projeksiyonu <c>AnonimAd</c> bayrağını taşır; cari varlığı
    /// yüklenmez). Kira listesi ve Panel dönüş/çıkış kovaları bu kuraldan geçer; <c>secim/musteri*</c> aynı sabiti
    /// (<c>CariAnonimlik</c>) Application katmanında uygular.
    /// </summary>
    public static string ListeAdi(string ad, bool anonimAd) => RentACar.Application.Customers.CariAnonimlik.Ad(ad, anonimAd);

    public static string? Telefon(Customer? m) => m is null || m.AnonimTelefon ? null : m.CepTel;

    public static string? Eposta(Customer? m) => m is null || m.AnonimMail ? null : m.Email;

    /// <summary>
    /// Belge (ehliyet/pasaport) numarası maskesi. Uzunluk ≥ 8 → son 4; 5–7 → son 2; ≤ 4 → tamamen yıldız; boş →
    /// <c>null</c>. (Blazor <c>SekmeMusteri.Maske</c> kısa numarada da son 4'ü gösteriyordu — 6 haneli numaranın 4'ü
    /// açık kalıyordu; #262 adversarial L1 ile sıkılaştırıldı.) Düz numara bu yüzeyden hiçbir koşulda dönmez.
    /// </summary>
    public static string? Maske(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var acik = v.Length >= 8 ? 4 : v.Length >= 5 ? 2 : 0;
        return new string('*', v.Length - acik) + v[(v.Length - acik)..];
    }

    /// <summary>
    /// WhatsApp/Gmail hazır özet metni — Blazor <c>KiraForm._paylasMesaj</c> metni (link hariç; SPA ekler). Hitap
    /// <see cref="TarafAdi"/> değil: anonim/bulunamayan caride nötr "Sayın müşterimiz". Tarih İstanbul günü, tutar tr-TR.
    /// </summary>
    public static string PaylasimMesaji(RentalContract c, Customer? m)
    {
        static string Gun(DateTimeOffset an) => TimeZoneInfo.ConvertTime(an, TenantGun.Dilim).ToString("dd.MM.yyyy", Tr);
        var hitap = m is null || m.AnonimAd ? NotrHitap : m.DisplayName;
        return $"Sayın {hitap}, {c.SozlesmeNo} nolu kira sözleşmeniz: {Gun(c.BasTar)} - {Gun(c.BitTar)}, " +
               $"genel toplam {c.GenelToplam.ToString("N2", Tr)} {c.Doviz ?? "TL"}.";
    }

    /// <summary>Müşteri sekmesinin salt-okunur özeti — yukarıdaki kuralların tamamı.</summary>
    public static KiraMusteriOzeti Ozet(Customer m)
    {
        var belge = !m.AnonimBelge;
        var adres = !m.AnonimAdres;
        return new KiraMusteriOzeti(
            m.Id,
            m.AnonimAd ? null : m.DisplayName,
            m.Tip.ToString(),
            Telefon(m),
            Eposta(m),
            belge ? Maske(m.EhliyetNo) : null,
            belge ? Maske(m.PasaportNo) : null,
            belge ? m.EhliyetSinifi : null,
            belge ? m.EhliyetTarihi : null,
            belge ? m.EhliyetYeri : null,
            belge ? m.EhliyetUlke : null,
            belge ? m.PasaportYeri : null,
            adres ? m.Adres : null,
            adres ? m.Il : null,
            adres ? m.Ilce : null,
            m.MusteriTipi, m.RiskLimiti, m.KaraListe, m.Uyari, m.UyariNedeni);
    }
}
