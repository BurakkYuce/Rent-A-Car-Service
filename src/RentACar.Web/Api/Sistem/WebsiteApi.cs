namespace RentACar.Web.Api.Sistem;

/// <summary>
/// F11.1b — web sitesi yönetimi (Blazor <c>WebSite/*</c>, <c>SiteIcerikYonetim</c>, <c>Blog/*</c>,
/// <c>GelenTalepler</c> paritesi).
/// <list type="bullet">
/// <item><b>Kapılar Blazor'la BİREBİR:</b> ilanlar + site içeriği = OperationsWrite + web sitesi modülü (satın alma,
/// <c>RequireWebSitesiModulu</c> — modülü olmayan firmaya 404) + servisin <c>"web-sitesi"</c> ekran kodu; blog ve gelen
/// talepler = yalnız OperationsWrite (Blazor'da da modül kapısı yok).</item>
/// <item><b>Metin HTML DEĞİLDİR:</b> blog içeriği ve sayfa gövdesi düz metin olarak saklanır; site onları
/// <c>IcerikMetni</c> bloklarına ayırıp Razor kodlamasıyla basar. API de HTML üretmez — önizleme blok listesi döner.
/// Böylece saklanan <c>&lt;script&gt;</c> hiçbir yüzeyde işaretleme olarak yorumlanmaz.</item>
/// <item><b>Tam değiştirme PUT'ları</b> (blog, sayfa, SSS, ilan fiyatı/özellikleri) zorunlu <c>surum</c> ister;
/// uyuşmazlık 409 <c>cakisma</c>.</item>
/// <item><b>Dosyalar:</b> ilan fotoğrafı ve blog kapağı içerikten tür tespitiyle (PNG/JPEG/WebP) ve 2 MB sınırıyla
/// servis katmanında doğrulanır; uç ayrıca istek gövdesini sınırlar.</item>
/// </list>
/// </summary>
public static partial class WebsiteApi
{
    /// <summary>İstek gövdesi üst sınırı: 2 MB görsel + multipart payı (Blazor uçlarıyla aynı).</summary>
    private const long UploadRequestLimit = 3_000_000;

    public static void MapWebsiteApi(this RouteGroupBuilder v1)
    {
        MapListings(v1);
        MapContent(v1);
        MapBlog(v1);
        MapBookingRequests(v1);
    }
}
