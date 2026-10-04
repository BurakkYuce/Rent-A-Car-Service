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
/// <para><b>Öncelik:</b> önce Kod — TÜM gruplarda (pasif dahil). Kod bir gruba aitse karar odur: grup pasifse ve
/// <c>activeOnly</c> ise sonuç <c>null</c>dur, Ad yoluna DÜŞÜLMEZ (adversarial M1: pasif "EKO" kodlu grubun aracı, adı
/// "EKO" olan başka bir aktif grubun tarifesine kayıyordu — eski davranış ham koddur). Kod hiçbir gruba ait değilse
/// Ad (Türkçe-duyarsız). Böylece kodla fiyatlanan hiçbir aracın fiyatı değişmez. Ad/Kod çapraz çakışması
/// <see cref="VehicleGroupService"/> yazımında ayrıca reddedilir (M2a). Doluluk üyeliği ve grup sayacı da bu
/// kuraldan geçer (M2b: bir araç iki grupta sayılmaz).</para>
/// </summary>
public static class VehicleGroupMatch
{
    /// <summary>Değeri Kod sonra Ad ile eşleşen grup; boş değer ya da eşleşme yoksa null.</summary>
    /// <param name="groups">TÜM gruplar (pasif dahil) — Kod kararı pasif grubu da görmeli.</param>
    /// <param name="activeOnly">true → yalnız aktif grup döner (fiyat tarafı); false → pasif de (sayaç).</param>
    public static VehicleGroup? Find(IEnumerable<VehicleGroup> groups, string? value, bool activeOnly = true)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        var list = groups as IReadOnlyCollection<VehicleGroup> ?? groups.ToList();
        var byCode = list.FirstOrDefault(g => string.Equals(g.Kod?.Trim(), v, StringComparison.OrdinalIgnoreCase));
        if (byCode is not null) return !activeOnly || byCode.Aktif ? byCode : null;
        return list.FirstOrDefault(g => (!activeOnly || g.Aktif) && TurkishText.EqualsIgnoreTurkishCase(g.Ad?.Trim(), v));
    }
}
