namespace RentACar.Domain.Enums;

/// <summary>
/// Sabit kullanıcı rolleri. Yetki matrisi (hangi rol neyi yapar) PR #18'de servis + web
/// katmanında uygulanır. Admin = tam yetki + kullanıcı yönetimi.
/// </summary>
public enum UserRole
{
    Admin = 0,
    Yonetici = 1,
    Operator = 2,
    Muhasebe = 3
}
