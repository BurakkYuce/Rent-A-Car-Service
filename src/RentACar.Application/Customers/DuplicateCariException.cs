using RentACar.Application.Common;

namespace RentACar.Application.Customers;

/// <summary>
/// Tenant içinde TC Kimlik / Vergi No benzersizlik ihlali (DB 23505 → bu).
/// ValidationException'dan türer → Web'de kullanıcı hatası olarak gösterilir.
/// </summary>
public sealed class DuplicateCariException : ValidationException
{
    // Değer MESAJA KONMAZ (adversarial L2): mesaj hata URL'ine/geçmişe düşüyordu → düz TC sızıntısı. Değer
    // yalnız Value property'sinde (programatik; kullanıcıya/URL'e gösterilmez).
    public DuplicateCariException(string field, string value)
        : base($"Bu {field} bu tenant içinde zaten kayıtlı.")
    {
        Field = field;
        Value = value;
    }

    public string Field { get; }
    public string Value { get; }
}
