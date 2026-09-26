using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Notifications;
using RentACar.Application.PublicSite;
using RentACar.Domain.Common;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Menu;

/// <summary>
/// <c>GET /api/ui/v1/menu</c> (F1.6) — yeni arayüz kabuğunun menüsü: <see cref="MenuKaydi"/>'nin çağıranın
/// GÖREBİLECEĞİ öğeleri (etkin izin — rol matrisi + kullanıcı-bazlı ek/yasak — ve modül bayrağı) +
/// rozet sayaçları. Uç düzeyinde izin kapısı yok (<see cref="AuthExtensions.IzinMuaf{TBuilder}"/>): her
/// oturum açmış kullanıcının bir menüsü vardır; kapı ÖĞE düzeyindedir.
/// </summary>
public static class MenuApi
{
    public sealed record MenuOgesiYaniti(
        string Rota, string Etiket, string Grup, int Sira, string Sahip, string? RozetKodu, bool HizliBaglanti);

    /// <summary><c>Rozetler</c>: yalnız görünen öğelerin rozet kodları (değer = sayaç).</summary>
    public sealed record MenuYaniti(IReadOnlyList<MenuOgesiYaniti> Ogeler, IReadOnlyDictionary<string, int> Rozetler);

    public static RouteGroupBuilder MapMenuApi(this RouteGroupBuilder v1)
    {
        v1.MapGet("/menu", Menu)
            .IzinMuaf("Menü: her oturumun bir menüsü var; kapı ÖĞE düzeyinde — her öğe kendi izniyle (kullanıcı-bazlı " +
                      "istisnalar dahil) ve modül bayrağıyla süzülür, çağıran yalnız görebileceği öğeleri alır.")
            .WithTags("Kabuk");
        return v1;
    }

    /// <summary>Görünürlük kuralı (test edilebilir, saf): izin yoksa herkes; varsa etkin izin; modül bayrağı açık olmalı.
    /// Bilinmeyen modül adı → gizli (güvenli varsayılan).</summary>
    public static IEnumerable<MenuOgesi> Gorunur(ClaimsPrincipal user, bool webSitesiModulu)
        => MenuKaydi.Ogeler.Where(o =>
            (o.Izin is not { } izin || AuthExtensions.HasPermission(user, izin))
            && (o.Modul is null || (o.Modul == ModulMetadata.WebSitesi && webSitesiModulu)));

    private static async Task<Ok<MenuYaniti>> Menu(
        HttpContext http, ITenantContext tenant, TenantStatusCache durum,
        InAppNotificationService bildirimler, PublicBookingRequestService talepler, ILoggerFactory log, CancellationToken ct)
    {
        var webSitesi = tenant.TenantId is { } id && await durum.WebSitesiModuluAsync(id, ct);
        var ogeler = Gorunur(http.User, webSitesi).ToList();

        // Rozetler MainLayout'la AYNI kaynaktan; sayaç hatası menüyü düşürmez (0 + uyarı logu).
        var rozetler = new Dictionary<string, int>();
        var logger = log.CreateLogger("RentACar.Web.Api.Menu");
        async Task Say(string kod, Func<Task<int>> oku)
        {
            if (ogeler.All(o => o.RozetKodu != kod)) return;
            try { rozetler[kod] = await oku(); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Menü rozeti okunamadı: {Rozet}", kod);
                rozetler[kod] = 0;
            }
        }
        await Say(MenuKaydi.RozetOkunmamisBildirim, () => bildirimler.UnreadCountAsync(ct));
        // PR-17 ile aynı: talep sayacı yalnız Web Sitesi modülü açıkken sorulur.
        if (webSitesi) await Say(MenuKaydi.RozetYeniTalep, async () => (await talepler.SummaryAsync(ct)).Yeni);

        return TypedResults.Ok(new MenuYaniti(
            ogeler.Select(o => new MenuOgesiYaniti(o.Rota, o.Etiket, o.Grup, o.Sira, o.Sahip, o.RozetKodu, o.HizliBaglanti)).ToList(),
            rozetler));
    }
}
