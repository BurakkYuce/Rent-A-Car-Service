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
public static class FeatureSnapshot
{
    /// <summary>İlan başına en fazla satır (referans projenin sınırı + `VehiclePhotoService.MaxPhotos` deseni).</summary>
    public const int MaxRows = 30;
    public const int MaxLabel = 60;
    public const int MaxValue = 160;

    public static IReadOnlyList<OzellikSatiri> Generate(IReadOnlyList<Vehicle> vehicles, VehicleGroup? group)
    {
        var first = vehicles[0];
        var rows = new List<OzellikSatiri>();

        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) rows.Add(new OzellikSatiri(label, value.Trim()));
        }

        /// <summary>Tüm üyelerde AYNI ise değeri, değilse null (satır üretilmez).</summary>
        string? Shared<T>(Func<Vehicle, T> select, Func<T, string?> write)
        {
            var initialValue = select(first);
            return vehicles.All(v => EqualityComparer<T>.Default.Equals(select(v), initialValue)) ? write(initialValue) : null;
        }

        // İmzada olanlar → tanım gereği tüm üyelerde aynı.
        Add("Marka", first.Marka);
        Add("Model", first.Tip);
        Add("Vites", first.Vites?.ToString());
        Add("Yakıt", first.Yakit?.ToString());   // PR-21: girilmemişse satır HİÇ eklenmez

        // Yıl: tek yıl ise değer, aralık ise aralık (bilgi kaybı yok, yanlış bilgi de yok).
        Add("Model Yılı", VehicleSignature.YearRange(vehicles));

        // İmzada OLMAYANLAR — yalnız tüm üyelerde aynıysa.
        Add("Kasa Tipi", Shared(v => v.KasaTipi, x => x));
        Add("Renk", Shared(v => v.Renk, x => x));
        Add("Motor Gücü", Shared(v => v.MotorGucu, x => x is > 0 ? $"{x} HP" : null));
        Add("Motor Hacmi", Shared(v => v.SilindirHacmi, x => x is > 0 ? $"{x} cc" : null));
        Add("Günlük KM Limiti", Shared(v => v.KiraKmLimiti, x => x is > 0 ? $"{x} km" : null));

        // Gruptan gelenler (Vehicle'da bu alanlar yok).
        if (group is not null)
        {
            Add("Koltuk Sayısı", group.KoltukSayisi is > 0 ? group.KoltukSayisi.ToString() : null);
            Add("Kapı Sayısı", group.KapiSayisi is > 0 ? group.KapiSayisi.ToString() : null);
            Add("Bagaj", group.BagajSayisi is > 0 ? group.BagajSayisi.ToString() : null);
        }

        return rows.Take(MaxRows).ToList();
    }

    /// <summary>Snapshot ile araçların GÜNCEL hâli farklılaştı mı (ERP'de araç düzeltilince ilan eski
    /// değeri göstermeye devam eder — sessiz kalmasın diye tanılama).</summary>
    public static bool IsStale(
        IReadOnlyList<WebIlanOzellik> existing, IReadOnlyList<Vehicle> vehicles, VehicleGroup? group)
    {
        if (vehicles.Count == 0) return false;
        var fresh = Generate(vehicles, group);
        // Yalnız OTOMATİK üretilen etiketleri karşılaştır: personelin "+" ile eklediği özel satırlar
        // ve kapattığı satırlar bayatlama sayılmaz.
        return fresh.Any(t => existing.FirstOrDefault(m => m.Etiket == t.Etiket) is { } m && m.Deger != t.Deger);
    }
}
