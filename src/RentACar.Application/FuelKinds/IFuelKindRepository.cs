using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.FuelKinds;

/// <summary>Yakıt türü repo sözleşmesi — üye seti <see cref="IMasterTanimRepository{T}"/>'den gelir (boş gövde; DI/tüketici yüzeyi değişmedi).</summary>
public interface IFuelKindRepository : IMasterTanimRepository<FuelKind>;
