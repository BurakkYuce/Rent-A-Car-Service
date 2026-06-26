namespace RentACar.Domain.Common;

/// <summary>
/// İşaretleyici arayüz: bu entity üzerindeki create/update/delete işlemleri
/// AuditLog'a (kim / ne zaman / eski-yeni değer) yazılır.
/// AuditLog'un kendisi bunu uygulamaz (sonsuz döngü olmaması için).
/// </summary>
public interface IAuditable
{
}
