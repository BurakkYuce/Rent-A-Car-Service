namespace RentACar.Application.Pricing;

/// <summary>
/// FAZ-82 — "Fiyat Türü" (hesaplama tipi) seçeneklerinin TEK kaynağı.
///
/// <para>Neden bir sabit listeye ihtiyaç var: bu değer serbest metin olarak saklanıyor ama motor onu
/// STRING KARŞILAŞTIRMASIYLA okuyor — <c>PricingService</c> "Otomatik"i (manuel ücret yok sayılır),
/// <c>InvoiceService</c> ise "Günlük"/"Toplam"ı (fatura NET moda geçer, KDV tutarın üstüne eklenir)
/// arıyor. Tenant ayarına yazım hatalı ("Gunluk") bir varsayılan kaydedilirse form o değeri ön-seçili
/// gösterir, motor tanımaz ve sessizce BAŞKA bir fiyat/KDV davranışı çalışır. Bu yüzden ayar kaydı
/// bu listeye karşı doğrulanır.</para>
///
/// <para>NOT: <c>null</c> ile "Otomatik" AYNI ŞEY DEĞİLDİR — null'da manuel ücret kazanır, "Otomatik"te
/// tarife motoru kazanır. Liste yalnız GEÇERLİ metinleri tanımlar; "seçilmemiş" hâli null'dır.</para>
/// </summary>
public static class FiyatTuruSecenek
{
    /// <summary>Geçerli fiyat türü metinleri (canlı TürevRent parite sırası; UI dropdown'ları da bu sırayı kullanır).</summary>
    public static readonly string[] Hepsi =
        ["Otomatik", "KDV Dahil Günlük", "Günlük", "KDV Dahil Toplam", "Toplam"];

    /// <summary>Boş/whitespace → null; listede varsa KANONİK yazımıyla (büyük/küçük harf duyarsız eşleşme)
    /// döner; listede yoksa null döner (çağıran isterse reddeder).</summary>
    public static string? Normalize(string? deger)
    {
        if (string.IsNullOrWhiteSpace(deger)) return null;
        var v = deger.Trim();
        return Hepsi.FirstOrDefault(x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase));
    }
}
