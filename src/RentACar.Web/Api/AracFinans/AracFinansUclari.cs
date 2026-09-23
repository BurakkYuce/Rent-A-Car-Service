namespace RentACar.Web.Api.AracFinans;

/// <summary>F6.1b uç kayıtlarının tek giriş noktası (<c>UiApiExtensions</c>'te tek satır — paralel fazlarla çakışma az).</summary>
internal static class AracFinansUclari
{
    public static void Esle(RouteGroupBuilder v1)
    {
        v1.MapAracKrediApi();
        v1.MapMusteriTaksitApi();
        v1.MapAracSiparisApi();
        v1.MapBafApi();
        v1.MapHasarApi();
        v1.MapFiloPlanApi();
    }
}
