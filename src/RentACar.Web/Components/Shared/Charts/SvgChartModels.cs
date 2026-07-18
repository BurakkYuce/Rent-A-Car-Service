namespace RentACar.Web.Components.Shared.Charts;

/// <summary>Çubuk grafik serisi: Ad (lejant + gizli tablo başlığı), Degerler (Etiketler ile aynı sıra/uzunluk),
/// RenkIndeks 1..6 → dataviz token'ı --dv-N (app.css .dv-fN fill sınıfı).</summary>
public sealed record SvgSeri(string Ad, IReadOnlyList<decimal> Degerler, int RenkIndeks);

/// <summary>Yatay kırılım dilimi: Etiket + Deger (negatif = iade netlemesi, kırmızı sola çizilir).</summary>
public sealed record SvgDilim(string Etiket, decimal Deger, int RenkIndeks);
