namespace RentACar.Domain.Common;

/// <summary>
/// Ofis-kapsamlı belge (FAZ 5-C4): CikisOfisi serbest-metni Location master'ına, oradan Location.SubeId
/// köprüsüyle Branch'a çözülür (türetilmiş şube FK'sı). Metin doğruluk-kaynağı kalır; FK daima türev —
/// OfficeBranchInterceptor merkezî doldurur (IBranchScoped/BranchFkInterceptor deseninin ofis karşılığı).
/// </summary>
public interface IOfficeScoped
{
    /// <summary>Çıkış ofisi serbest-metni (Location.Ad ile case-insensitive eşleşir).</summary>
    string? OfisAdi { get; }

    /// <summary>Türetilmiş şube FK'sı (ofis→Location→SubeId; çözülemezse null → salt-metin davranış).</summary>
    Guid? OfisSubeFk { get; set; }
}
