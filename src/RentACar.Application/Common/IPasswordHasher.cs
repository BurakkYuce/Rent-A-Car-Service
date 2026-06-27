namespace RentACar.Application.Common;

/// <summary>
/// Parola özetleme soyutlaması (Application, ASP.NET Identity'e bağımlı kalmasın).
/// Web katmanı PasswordHasher&lt;User&gt; ile uygular.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}
