using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Countries;

/// <summary>Ülke repo sözleşmesi — üye seti <see cref="IMasterTanimRepository{T}"/>'den gelir (boş gövde; DI/tüketici yüzeyi değişmedi).</summary>
public interface ICountryRepository : IMasterTanimRepository<Country>;
