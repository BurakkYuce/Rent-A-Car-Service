namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Ofis adı → eşleşme anahtarı — TEK KURAL (F4.1 adversarial N1). <see cref="Interceptors.OfficeBranchInterceptor"/>
/// kayıtlarda türetilmiş şubeyi (CikisSubeId) BU anahtarla çözer; <c>LocationRepository.FindByAdAsync</c> (kira
/// açma/ofis değiştirme şube kapsamı) da AYNI anahtarı kullanmak zorunda. Önceden repository Postgres
/// <c>lower("Ad")</c> ile karşılaştırıyordu: en_US collation'da 'İ' → 'i', .NET <c>ToLowerInvariant</c>'ta farklı —
/// "İzmir Merkez" ofisi eşleşmiyor, operatör KENDİ şubesinin ofisiyle kira açamıyordu (403).
/// Eşleşme bu yüzden SQL'de değil bellekte, aynı .NET fonksiyonuyla yapılır (ofis tablosu küçük).
/// </summary>
public static class OfisAdiAnahtari
{
    public static string Uret(string ad) => ad.Trim().ToLowerInvariant();
}
