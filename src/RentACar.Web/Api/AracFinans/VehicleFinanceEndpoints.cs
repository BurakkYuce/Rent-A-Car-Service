namespace RentACar.Web.Api.AracFinans;

/// <summary>F6.1b uç kayıtlarının tek giriş noktası (<c>UiApiExtensions</c>'te tek satır — paralel fazlarla çakışma az).</summary>
internal static class VehicleFinanceEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        v1.MapVehicleLoanApi();
        v1.MapCustomerInstallmentApi();
        v1.MapVehicleOrderApi();
        v1.MapBafApi();
        v1.MapDamageApi();
        v1.MapFleetPlanApi();
    }
}
