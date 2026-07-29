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
/// okunur (<see cref="Finance.KdvVarsayilan"/> ile aynı desen): araç açma yolu OperationsWrite ile
/// çalışır, ManageUsers istenirse operatörde patlardı.
/// </summary>
public sealed class VarsayilanGrupCozucu(ITenantSettingsRepository ayarlar, IVehicleGroupRepository gruplar)
{
    /// <summary>Ayar boşken aranan grup adı (Türkçe-duyarsız eşleşir: "EKONOMİ"/"ekonomi" de bulur).</summary>
    public const string EkonomiAdi = "Ekonomi";

    /// <summary>Varsayılan grubun <c>Ad</c>'ı (Vehicle.Grup string eşleşmesiyle çalışır) — yoksa null.</summary>
    public async Task<string?> AdAsync(CancellationToken ct = default)
    {
        var aktifler = await gruplar.ListActiveAsync(ct);

        var ayarliId = (await ayarlar.GetAsync(ct))?.VarsayilanGrupId;
        if (ayarliId is { } id)
        {
            // Yalnız AKTİF listede arıyoruz: pasifleştirilmiş bir gruba işaret eden ayar, araçları
            // sessizce görünmez bir gruba yazardı. Bulunamazsa zincir Ekonomi'ye devam eder.
            var ayarli = aktifler.FirstOrDefault(g => g.Id == id);
            if (ayarli is not null) return ayarli.Ad;
        }

        return aktifler.FirstOrDefault(g => TurkishText.EqualsIgnoreTurkishCase(g.Ad, EkonomiAdi))?.Ad;
    }
}
