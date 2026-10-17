namespace RentACar.Domain.Enums;

/// <summary>
/// İçe aktarılmış HGS geçişinin durumu. Değerler KALICIDIR (DB'de int; değişmezlik trigger'ı
/// <c>Yansitildi</c> = 3 değerine bakar).
/// </summary>
public enum HgsCrossingStatus
{
    /// <summary>Geçiş anı bir kiranın teslim–dönüş aralığında; müşteriye yansıtılmayı bekliyor.</summary>
    Eslesti = 0,

    /// <summary>Hiçbir kiraya düşmüyor (ya da plaka filoda yok) — firma gideri.</summary>
    KiraDisi = 1,

    /// <summary>Birden çok kira aynı anı kapsıyor (veri anomalisi) — otomatik yansıtılmaz.</summary>
    Belirsiz = 2,

    /// <summary>Müşteriye yansıtıldı (Borç Cari / Alacak Gelir). Satır artık değişmez.</summary>
    Yansitildi = 3,
}
