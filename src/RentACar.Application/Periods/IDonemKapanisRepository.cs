namespace RentACar.Application.Periods;

/// <summary>
/// Dönem-sonu kapanış fişinin ATOMİK yazımı (PR-A). Uygulama katmanı yetki/ön-doğrulama yapar; asıl
/// oku-bakiye → fiş-kur → post → dönem-kilidi işi TEK transaction'da, tenant başına advisory-lock ile
/// serileştirilmiş şekilde burada gerçekleşir (eşzamanlı/yeniden-kapatma çift-sayımı önlenir).
/// </summary>
public interface IDonemKapanisRepository
{
    /// <summary>Verilen tarihe (dahil) kadarki Gelir/Gider bakiyesini kapatan dengeli fişi yaz + dönemi kilitle.</summary>
    Task KapatAsync(DateTimeOffset kapanisTarihi, CancellationToken ct = default);
}
