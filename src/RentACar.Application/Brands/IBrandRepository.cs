using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Brands;

/// <summary>Marka repo sözleşmesi — üye seti <see cref="IMasterTanimRepository{T}"/>'den gelir (boş gövde; DI/tüketici yüzeyi değişmedi).</summary>
public interface IBrandRepository : IMasterTanimRepository<Brand>;
