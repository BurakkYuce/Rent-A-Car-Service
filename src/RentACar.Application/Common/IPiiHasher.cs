namespace RentACar.Application.Common;

/// <summary>
/// PII blind-index özetleyici (KVKK/F2). Deterministik HMAC: aynı değer → aynı özet, böylece
/// şifreli PII üzerinde tenant-içi BENZERSİZLİK indeksi ve tam-eşleşme ARAMA mümkün olur;
/// özetten değer geri türetilemez (anahtar gizli). <see cref="ISecretProtector"/> saklama
/// şifrelemesi yapar (non-deterministik), bu arayüz yalnız eşleşme anahtarı üretir.
/// </summary>
public interface IPiiHasher
{
    /// <summary>Normalize (trim) edilmiş değerin TENANT-TUZLU HMAC özeti (hex). null/boş → null.
    /// Tuz = tenantId: aynı TC farklı tenant'larda FARKLI özet üretir → DB dump'ında
    /// cross-tenant korelasyon kurulamaz (benzersizlik/arama zaten tenant-içi).</summary>
    string? Hash(Guid tenantId, string? value);
}
