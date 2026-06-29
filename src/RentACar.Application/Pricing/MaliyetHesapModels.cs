namespace RentACar.Application.Pricing;

/// <summary>
/// Filo (uzun-dönem) maliyet hesaplayıcı girişi (roadmap L2). Oranlar kesir (0.30 = %30).
/// MAKUL VARSAYIM modeli — canlı TürevRent Gün_Hesapla/kuruş paritesi DEĞİL (403 ile engelli).
/// </summary>
public sealed class MaliyetHesapInput
{
    public decimal AlisBedeli { get; set; }
    public decimal ResidualYuzde { get; set; } = 0.30m;   // dönem sonu kalıntı değer oranı
    public int SureAy { get; set; } = 36;
    public decimal FaizOran { get; set; }                 // yıllık basit faiz (kesir)
    public decimal KkdfOran { get; set; } = 0.15m;        // faiz üzerinden KKDF
    public decimal BsmvOran { get; set; } = 0.15m;        // faiz üzerinden BSMV
    public decimal DamgaOran { get; set; }                // alış bedeli üzerinden damga
    public decimal AylikGider { get; set; }               // aylık işletme gideri (sigorta/bakım…)
    public decimal KarMarji { get; set; } = 0.20m;        // toplam maliyet üzerine kâr
    public decimal KdvOran { get; set; } = 0.20m;
}

/// <summary>Maliyet hesabı sonucu (salt-hesap; deftere/bakiyeye yazmaz).</summary>
public sealed record MaliyetHesapSonuc(
    decimal ResidualDeger,
    decimal NetAmortisman,
    decimal FinansmanFaiz,
    decimal FinansmanVergi,
    decimal Damga,
    decimal ToplamGider,
    decimal ToplamMaliyet,
    decimal BasaBasAylik,
    decimal Kar,
    decimal TeklifNet,
    decimal TeklifAylikNet,
    decimal TeklifKdvli);
