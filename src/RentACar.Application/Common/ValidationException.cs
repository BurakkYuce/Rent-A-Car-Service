namespace RentACar.Application.Common;

/// <summary>
/// İş kuralı / doğrulama ihlali. Web katmanı bunu kullanıcıya gösterilebilir
/// hata olarak ele alır (500 değil, form hatası).
/// </summary>
public class ValidationException : Exception
{
    public ValidationException(string message) : base(message)
    {
    }
}
