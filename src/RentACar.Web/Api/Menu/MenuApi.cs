using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Notifications;
using RentACar.Application.PublicSite;
using RentACar.Domain.Common;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Menu;

/// <summary>
/// <c>GET /api/ui/v1/menu</c> (F1.6) — yeni arayüz kabuğunun menüsü: <see cref="MenuRegistry"/>'nin çağıranın
/// GÖREBİLECEĞİ öğeleri (etkin izin — rol matrisi + kullanıcı-bazlı ek/yasak — ve modül bayrağı) +
/// rozet sayaçları. Uç düzeyinde izin kapısı yok (<see cref="AuthExtensions.PermissionExempt{TBuilder}"/>): her
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
            .PermissionExempt("Menü: her oturumun bir menüsü var; kapı ÖĞE düzeyinde — her öğe kendi izniyle (kullanıcı-bazlı " +
                      "istisnalar dahil) ve modül bayrağıyla süzülür, çağıran yalnız görebileceği öğeleri alır.")
            .WithTags("Kabuk");
        return v1;
    }

    /// <summary>Görünürlük kuralı (test edilebilir, saf): izin yoksa herkes; varsa etkin izin; modül bayrağı açık olmalı.
    /// Bilinmeyen modül adı → gizli (güvenli varsayılan).</summary>
    public static IEnumerable<MenuOgesi> Visible(ClaimsPrincipal user, bool websiteModule)
        => MenuRegistry.Items.Where(o =>
            (o.Izin is not { } permission || AuthExtensions.HasPermission(user, permission))
            && (o.Modul is null || (o.Modul == ModulMetadata.Website && websiteModule)));

    private static async Task<Ok<MenuYaniti>> Menu(
        HttpContext http, ITenantContext tenant, TenantStatusCache durum,
        InAppNotificationService bildirimler, PublicBookingRequestService talepler, ILoggerFactory log, CancellationToken ct)
    {
        var website = tenant.TenantId is { } id && await durum.WebsiteModuleAsync(id, ct);
        var items = Visible(http.User, website).ToList();

        // Rozetler MainLayout'la AYNI kaynaktan; sayaç hatası menüyü düşürmez (0 + uyarı logu).
        var badges = new Dictionary<string, int>();
        var logger = log.CreateLogger("RentACar.Web.Api.Menu");
        async Task Say(string code, Func<Task<int>> read)
        {
            if (items.All(o => o.RozetKodu != code)) return;
            try { badges[code] = await read(); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Menü rozeti okunamadı: {Rozet}", code);
                badges[code] = 0;
            }
        }
        await Say(MenuRegistry.BadgeUnreadNotification, () => bildirimler.UnreadCountAsync(ct));
        // PR-17 ile aynı: talep sayacı yalnız Web Sitesi modülü açıkken sorulur.
        if (website) await Say(MenuRegistry.BadgeNewRequest, async () => (await talepler.SummaryAsync(ct)).Yeni);

        return TypedResults.Ok(new MenuYaniti(
            items.Select(o => new MenuOgesiYaniti(o.Rota, o.Etiket, o.Grup, o.Sira, o.Sahip, o.RozetKodu, o.HizliBaglanti)).ToList(),
            badges));
    }
}
