using RentACar.Domain.Entities;

namespace RentACar.Application.WebSite;

/// <summary>PR-13 sihirbaz adım-3 satırı: etiket/değer + sitede görünsün mü.</summary>
public sealed record OzellikSatiri(string Etiket, string Deger, bool Gorunur = true);

/// <summary>
/// PR-13 — ilanın teknik özellik listesini araçlardan üretir (adım-3'ün başlangıç hâli).
///
/// <para><b>EN ÖNEMLİ KURAL — üyeler arasında FARKLI olan alan için satır ÜRETİLMEZ.</b> Bir ilan
/// birden çok aracı temsil edebilir; "ilk aracın rengi"ni ilan özelliği yapmak, site "Beyaz" derken
/// müşteriye siyah araç vermek demektir (tüketici şikayeti sınıfı). İmzada olan alanlar (marka/tip/
/// vites/yakıt) zaten tüm üyelerde aynıdır; olmayanlar (renk, motor gücü, silindir hacmi, kasa tipi,
/// km limiti) tek tek kontrol edilir.</para>
///
/// <para>Koltuk/kapı/bagaj <see cref="Vehicle"/>'da YOKTUR — yalnız <see cref="VehicleGroup"/>'ta.
/// Bu yüzden sihirbaz, "sınıf kartları kalksa bile" gruba bağımlı kalır; grup bulunamazsa o satırlar
/// üretilmez (personel "+" ile elle ekleyebilir).</para>
/// </summary>
public static class OzellikSnapshot
{
    /// <summary>İlan başına en fazla satır (referans projenin sınırı + `VehiclePhotoService.MaxPhotos` deseni).</summary>
    public const int MaxSatir = 30;
    public const int MaxEtiket = 60;
    public const int MaxDeger = 160;

    public static IReadOnlyList<OzellikSatiri> Uret(IReadOnlyList<Vehicle> araclar, VehicleGroup? grup)
    {
        var ilk = araclar[0];
        var satirlar = new List<OzellikSatiri>();

        void Ekle(string etiket, string? deger)
        {
            if (!string.IsNullOrWhiteSpace(deger)) satirlar.Add(new OzellikSatiri(etiket, deger.Trim()));
        }

        /// <summary>Tüm üyelerde AYNI ise değeri, değilse null (satır üretilmez).</summary>
        string? Ortak<T>(Func<Vehicle, T> sec, Func<T, string?> yaz)
        {
            var ilkDeger = sec(ilk);
            return araclar.All(v => EqualityComparer<T>.Default.Equals(sec(v), ilkDeger)) ? yaz(ilkDeger) : null;
        }

        // İmzada olanlar → tanım gereği tüm üyelerde aynı.
        Ekle("Marka", ilk.Marka);
        Ekle("Model", ilk.Tip);
        Ekle("Vites", ilk.Vites?.ToString());
        Ekle("Yakıt", ilk.Yakit.ToString());

        // Yıl: tek yıl ise değer, aralık ise aralık (bilgi kaybı yok, yanlış bilgi de yok).
        Ekle("Model Yılı", AracImza.YilAralik(araclar));

        // İmzada OLMAYANLAR — yalnız tüm üyelerde aynıysa.
        Ekle("Kasa Tipi", Ortak(v => v.KasaTipi, x => x));
        Ekle("Renk", Ortak(v => v.Renk, x => x));
        Ekle("Motor Gücü", Ortak(v => v.MotorGucu, x => x is > 0 ? $"{x} HP" : null));
        Ekle("Motor Hacmi", Ortak(v => v.SilindirHacmi, x => x is > 0 ? $"{x} cc" : null));
        Ekle("Günlük KM Limiti", Ortak(v => v.KiraKmLimiti, x => x is > 0 ? $"{x} km" : null));

        // Gruptan gelenler (Vehicle'da bu alanlar yok).
        if (grup is not null)
        {
            Ekle("Koltuk Sayısı", grup.KoltukSayisi is > 0 ? grup.KoltukSayisi.ToString() : null);
            Ekle("Kapı Sayısı", grup.KapiSayisi is > 0 ? grup.KapiSayisi.ToString() : null);
            Ekle("Bagaj", grup.BagajSayisi is > 0 ? grup.BagajSayisi.ToString() : null);
        }

        return satirlar.Take(MaxSatir).ToList();
    }

    /// <summary>Snapshot ile araçların GÜNCEL hâli farklılaştı mı (ERP'de araç düzeltilince ilan eski
    /// değeri göstermeye devam eder — sessiz kalmasın diye tanılama).</summary>
    public static bool Bayatladi(
        IReadOnlyList<WebIlanOzellik> mevcut, IReadOnlyList<Vehicle> araclar, VehicleGroup? grup)
    {
        if (araclar.Count == 0) return false;
        var taze = Uret(araclar, grup);
        // Yalnız OTOMATİK üretilen etiketleri karşılaştır: personelin "+" ile eklediği özel satırlar
        // ve kapattığı satırlar bayatlama sayılmaz.
        return taze.Any(t => mevcut.FirstOrDefault(m => m.Etiket == t.Etiket) is { } m && m.Deger != t.Deger);
    }
}
