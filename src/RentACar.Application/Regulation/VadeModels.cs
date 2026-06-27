using RentACar.Domain.Enums;

namespace RentACar.Application.Regulation;

/// <summary>Ham vade kaynağı (defter/poliçe ayrımı olmadan birleşik).</summary>
public sealed record VadeSource(Guid VehicleId, string Tur, DateTimeOffset Bitis);

/// <summary>Vade panosu satırı (kova + kalan gün hesaplanmış).</summary>
public sealed record VadeItem(Guid VehicleId, string Tur, DateTimeOffset Bitis, int KalanGun, VadeBucket Bucket);
