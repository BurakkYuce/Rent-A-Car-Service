using RentACar.Application.Common;
using RentACar.Application.TenantSettings;

namespace RentACar.Application.VehicleGroups;

/// <summary>
/// PR-10: grubu belirtilmeden açılan aracın düşeceği varsayılan grup adını çözer.
///
/// Zincir: <c>TenantSettings.VarsayilanGrupId</c> (dolu VE hedef grup AKTİF ise) → aktif gruplar
/// içinde Türkçe-duyarsız "Ekonomi" eşleşmesi → <b>null</b>.
///
/// "İlk aktif grup" (ör. WebSira'ya göre) fallback'i BİLİNÇLİ OLARAK YOKTUR: o grup pekâlâ "Lüks"
/// olabilir ve grubu unutulan bir aracı yanlış segmentte yayına sokardı. Grupsuz kalan araç yalnız
/// vitrine girmez (pending) — geri dönülebilir bir durumdur; yanlış fiyat segmenti değildir.
///
/// Ayar alanı HASSAS DEĞİLDİR → admin-gate'li <see cref="TenantSettingsService"/> yerine repo'dan
/// okunur (<see cref="Finance.VatDefault"/> ile aynı desen): araç açma yolu OperationsWrite ile
/// çalışır, ManageUsers istenirse operatörde patlardı.
/// </summary>
public sealed class DefaultGroupResolver(ITenantSettingsRepository settings, IVehicleGroupRepository groups)
{
    /// <summary>Ayar boşken aranan grup adı (Türkçe-duyarsız eşleşir: "EKONOMİ"/"ekonomi" de bulur).</summary>
    public const string EconomyName = "Ekonomi";

    /// <summary>Varsayılan grubun <c>Ad</c>'ı (Vehicle.Grup string eşleşmesiyle çalışır) — yoksa null.</summary>
    public async Task<string?> NameAsync(CancellationToken ct = default)
    {
        var activeItems = await groups.ListActiveAsync(ct);

        var configuredId = (await settings.GetAsync(ct))?.VarsayilanGrupId;
        if (configuredId is { } id)
        {
            // Yalnız AKTİF listede arıyoruz: pasifleştirilmiş bir gruba işaret eden ayar, araçları
            // sessizce görünmez bir gruba yazardı. Bulunamazsa zincir Ekonomi'ye devam eder.
            var configured = activeItems.FirstOrDefault(g => g.Id == id);
            if (configured is not null) return configured.Ad;
        }

        return activeItems.FirstOrDefault(g => TurkishText.EqualsIgnoreTurkishCase(g.Ad, EconomyName))?.Ad;
    }
}
