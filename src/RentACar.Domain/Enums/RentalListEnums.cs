namespace RentACar.Domain.Enums;

/// <summary>
/// FAZ-46 — kira listesinde tarih aralığı filtresinin HANGİ tarih alanına uygulanacağı
/// (canlı <c>kira_listesi.aspx</c> "Tarih Listesi" seçicisi). Varsayılan <see cref="Baslangic"/>
/// = bu fazdan önceki davranış.
/// </summary>
public enum DateListType
{
    Baslangic = 0,
    Bitis = 1,
    /// <summary>Sözleşmenin sisteme girildiği an (CreatedAtUtc).</summary>
    Islem = 2,
    Vade = 3
}

/// <summary>
/// FAZ-46 — ofis filtresinin çıkış mı dönüş ofisine mi uygulanacağı. Seçilmezse (null) eski
/// davranış korunur: ofis metni ÇIKIŞ ya da DÖNÜŞ ofisinden herhangi biriyle eşleşir.
/// </summary>
public enum OfficeStatus
{
    Cikis = 0,
    Donus = 1
}

/// <summary>FAZ-46 — kural promosyonunun birden çok kez mi yoksa tek kez mi uygulanacağı (BİLGİ).</summary>
public enum PromotionType
{
    Coklu = 0,
    Tek = 1
}

/// <summary>FAZ-46 — kuponun neyi kapsadığı (BİLGİ).</summary>
public enum CouponValidity
{
    Hepsi = 0,
    SadeceIlkBedel = 1
}

/// <summary>FAZ-46 — kural tutarının oran mı serbest tutar mı olduğu (BİLGİ).</summary>
public enum CalculationType
{
    Oran = 0,
    Serbest = 1
}
