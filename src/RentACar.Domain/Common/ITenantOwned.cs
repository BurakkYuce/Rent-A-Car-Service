namespace RentACar.Domain.Common;

/// <summary>
/// Bir kiracıya (tenant) ait olan her entity bunu uygular.
/// <see cref="TenantId"/> hem EF Core global query filter'ın hem de Postgres RLS
/// policy'sinin dayanağıdır. Uygulama bu alanı elle set ETMEZ; insert sırasında
/// DbContext otomatik damgalar.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
