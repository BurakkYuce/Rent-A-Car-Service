namespace RentACar.Web.Api.Sistem;

/// <summary>F11.1b — ayarlar, kullanıcılar, yetki, denetim, mesaj şablonları, bildirim merkezi, arama.</summary>
public static partial class SystemAdminApi
{
    public static void MapSystemAdminApi(this RouteGroupBuilder v1)
    {
        MapSettings(v1);
        MapUsers(v1);
        MapPermissions(v1);
        MapAudit(v1);
        MapMessages(v1);
    }
}
