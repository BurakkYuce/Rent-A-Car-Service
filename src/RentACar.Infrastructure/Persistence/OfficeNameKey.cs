namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Ofis adı → eşleşme anahtarı — TEK KURAL (F4.1 adversarial N1). <see cref="Interceptors.OfficeBranchInterceptor"/>
/// kayıtlarda türetilmiş şubeyi (CikisSubeId) BU anahtarla çözer; <c>LocationRepository.FindByAdAsync</c> (kira
/// açma/ofis değiştirme şube kapsamı) da AYNI anahtarı kullanmak zorunda. Önceden repository Postgres
/// <c>lower("Ad")</c> ile karşılaştırıyordu: en_US collation'da 'İ' → 'i', .NET <c>ToLowerInvariant</c>'ta farklı —
/// "İzmir Merkez" ofisi eşleşmiyor, operatör KENDİ şubesinin ofisiyle kira açamıyordu (403).
/// Eşleşme bu yüzden SQL'de değil bellekte, aynı .NET fonksiyonuyla yapılır (ofis tablosu küçük).
/// </summary>
public static class OfficeNameKey
{
    public static string Generate(string name) => name.Trim().ToLowerInvariant();

    /// <summary>
    /// F11.1b güvenlik H1 — aynı anahtarlı ofislerin şubesi: hepsi aynı şubeyse o şube, aksi halde <c>null</c>
    /// (belirsiz anahtar hiçbir şubeye ÇÖZÜLMEZ; "en düşük Kod kazanır" kuralı ele geçirmeye açıktı).
    /// </summary>
    public static Guid? UnambiguousBranch(IEnumerable<Guid?> branchIds)
    {
        var distinct = branchIds.Distinct().Take(2).ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }
}
