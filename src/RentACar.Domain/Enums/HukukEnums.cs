namespace RentACar.Domain.Enums;

/// <summary>Hukuk dosyası türü.</summary>
public enum HukukTuru
{
    Dava = 0,
    Icra = 1,
    Diger = 2
}

/// <summary>Hukuk dosyası durumu.</summary>
public enum HukukDurum
{
    Acik = 0,
    Beklemede = 1,
    Kapali = 2
}
