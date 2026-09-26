namespace RentACar.Domain.Enums;

/// <summary>Ceza durumu.</summary>
public enum PenaltyStatus
{
    Yeni = 0,
    Yansitildi = 1,  // müşteriye yansıtıldı (cari borçlandı)
    Odendi = 2,
    Iptal = 3,
    /// <summary>
    /// FAZ-60 — kısmen ödendi: en az bir kalemde ödeme var, toplam kalan &gt; 0. Ödeme durumu
    /// yansıtma durumunun ÜSTÜNE yazılır (mevcut Odendi davranışının aynısı).
    /// </summary>
    Kismi = 4
}
