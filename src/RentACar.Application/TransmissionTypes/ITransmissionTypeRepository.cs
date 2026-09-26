using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.TransmissionTypes;

/// <summary>Vites türü repo sözleşmesi — üye seti <see cref="IMasterDefinitionRepository{T}"/>'den gelir (boş gövde; DI/tüketici yüzeyi değişmedi).</summary>
public interface ITransmissionTypeRepository : IMasterDefinitionRepository<TransmissionType>;
