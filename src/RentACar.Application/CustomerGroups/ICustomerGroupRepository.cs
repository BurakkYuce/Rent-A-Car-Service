using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.CustomerGroups;

/// <summary>Müşteri grubu repo sözleşmesi — üye seti <see cref="IMasterDefinitionRepository{T}"/>'den gelir (boş gövde; DI/tüketici yüzeyi değişmedi).</summary>
public interface ICustomerGroupRepository : IMasterDefinitionRepository<CustomerGroup>;
