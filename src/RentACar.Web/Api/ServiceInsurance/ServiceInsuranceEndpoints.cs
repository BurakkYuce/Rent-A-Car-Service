namespace RentACar.Web.Api.ServiceInsurance;

/// <summary>
/// F9.1 — single entry point of the F9 (servis &amp; sigorta + vade + fiyat &amp; tarife) <c>/api/ui/v1</c> endpoints
/// (one line in <c>UiApiExtensions</c> — fewer conflicts with parallel phases).
/// </summary>
internal static class ServiceInsuranceEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        RegulationApi.Map(v1);      // /regulasyon/* + /vade
        ServiceRecordApi.Map(v1);   // /servisler/*
        CatalogApi.Map(v1);         // /tarifeler, /tarife-matris, /tarife-gruplari, /sigorta-urunleri, /kira-kurallari,
                                    // /broker-yasaklari, /ek-hizmetler, /servis-tanimlari
        PricingApi.Map(v1);         // /fiyat-hesapla, /maliyet-hesapla, /maliyet-teklifleri, /tarife-aktar
        SelectionApi.Map(v1);       // /secim/sigorta-sirketi, /secim/tarife-grubu, /secim/sigorta-policesi
    }
}
