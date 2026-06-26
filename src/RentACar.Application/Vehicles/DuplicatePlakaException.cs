using RentACar.Application.Common;

namespace RentACar.Application.Vehicles;

/// <summary>
/// Aynı tenant içinde plaka benzersizlik ihlali (DB 23505 → bu).
/// ValidationException'dan türer → Web katmanında kullanıcı hatası olarak gösterilir.
/// </summary>
public sealed class DuplicatePlakaException : ValidationException
{
    public DuplicatePlakaException(string plaka)
        : base($"'{plaka}' plakası bu tenant içinde zaten kayıtlı.")
    {
        Plaka = plaka;
    }

    public string Plaka { get; }
}
