namespace RentACar.Web.Api.Sistem;

/// <summary>
/// F11.1b — sistem ayarları, kullanıcı/yetki yönetimi, denetim, mesaj şablonları, web sitesi yönetimi ve
/// F11 envanterinin ikinci yarısındaki tanım ekranlarının <c>/api/ui/v1</c> uçları. Tek kayıt noktası;
/// alt alanlar kendi dosyalarında eşlenir.
/// </summary>
public static class SystemApi
{
    public static void MapSystemApi(this RouteGroupBuilder v1)
    {
        v1.MapSystemAdminApi();       // ayarlar, kullanıcılar, yetki, denetim, mesaj şablonları, bildirim, arama
        v1.MapWebsiteApi();           // web sitesi: ilanlar, site içeriği, blog, gelen talepler
        v1.MapSystemDefinitionsApi(); // tanımlar (ikinci yarı)
    }
}
