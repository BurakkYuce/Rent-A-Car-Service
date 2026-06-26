using RentACar.Application.Common;

namespace RentACar.Application.Customers;

/// <summary>
/// Tenant içinde TC Kimlik / Vergi No benzersizlik ihlali (DB 23505 → bu).
/// ValidationException'dan türer → Web'de kullanıcı hatası olarak gösterilir.
/// </summary>
public sealed class DuplicateCariException : ValidationException
{
    public DuplicateCariException(string field, string value)
        : base($"Bu {field} ({value}) bu tenant içinde zaten kayıtlı.")
    {
        Field = field;
        Value = value;
    }

    public string Field { get; }
    public string Value { get; }
}
