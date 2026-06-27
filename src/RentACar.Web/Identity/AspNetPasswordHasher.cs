using Microsoft.AspNetCore.Identity;
using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Web.Identity;

/// <summary>
/// IPasswordHasher (Application) → ASP.NET Core PasswordHasher&lt;User&gt; köprüsü. Login ve
/// kullanıcı yönetimi aynı algoritmayı kullanır (seed'deki ile tutarlı).
/// </summary>
public sealed class AspNetPasswordHasher : IPasswordHasher
{
    private static readonly User Dummy = new();
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(string password) => _inner.HashPassword(Dummy, password);

    public bool Verify(string hash, string password)
        => _inner.VerifyHashedPassword(Dummy, hash, password) != PasswordVerificationResult.Failed;
}
