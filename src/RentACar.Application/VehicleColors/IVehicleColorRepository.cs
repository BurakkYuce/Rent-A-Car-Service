using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.VehicleColors;

/// <summary>Renk repo sözleşmesi — üye seti <see cref="IMasterTanimRepository{T}"/>'den gelir (boş gövde; DI/tüketici yüzeyi değişmedi).</summary>
public interface IVehicleColorRepository : IMasterTanimRepository<VehicleColor>;
