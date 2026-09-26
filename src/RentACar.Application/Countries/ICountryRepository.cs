using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Countries;

/// <summary>Ülke repo sözleşmesi — üye seti <see cref="IMasterDefinitionRepository{T}"/>'den gelir (boş gövde; DI/tüketici yüzeyi değişmedi).</summary>
public interface ICountryRepository : IMasterDefinitionRepository<Country>;
