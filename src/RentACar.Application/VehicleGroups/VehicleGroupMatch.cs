using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.VehicleGroups;

/// <summary>
/// Aracın serbest-metin <c>Vehicle.Grup</c> değerini tanımlı bir <see cref="VehicleGroup"/>'a çözer — fiyat tarafının
/// TEK kuralı (kabul bulgusu B-A2).
///
/// <para><b>Neden iki anahtar:</b> araç ekranı, grup sayacı, eşleşmeyen-değer paneli, "Ata" aracı, ad değişikliği
/// zinciri ve vitrin <c>Vehicle.Grup</c>'u grubun <b>ADI</b> olarak taşır (bilinçli tasarım — bkz.
/// <see cref="VehicleGroupService.UpdateAsync(Guid, VehicleGroupInput, string?, CancellationToken)"/>); API/içe
/// aktarımla açılan araçlarda ise değer çoğunlukla grubun <b>KODU</b>dur. Tarife matrisi, kiralama kuralı, doluluk
/// kuralı ve sürücü ücretleri ise <c>AracGrupKod</c> ile eşleşir. Eskiden fiyat tarafı değeri doğrudan kod sanıyordu
/// → ekrandan açılan her araçta "Otomatik tarife bulunamadı" ve grup ücretleri uygulanmıyordu.</para>
///
/// <para><b>Öncelik:</b> önce Kod (büyük/küçük harf duyarsız), sonra Ad (Türkçe-duyarsız). Kod önce: bugün kodla
/// fiyatlanan hiçbir aracın fiyatı değişmez (bir grubun adı başka bir grubun koduyla aynıysa bile).</para>
/// </summary>
public static class VehicleGroupMatch
{
    /// <summary>Değeri Kod sonra Ad ile eşleşen grup; boş değer ya da eşleşme yoksa null.</summary>
    public static VehicleGroup? Find(IEnumerable<VehicleGroup> groups, string? value)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        var list = groups as IReadOnlyCollection<VehicleGroup> ?? groups.ToList();
        return list.FirstOrDefault(g => string.Equals(g.Kod?.Trim(), v, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(g => TurkishText.EqualsIgnoreTurkishCase(g.Ad?.Trim(), v));
    }
}
