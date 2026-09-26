namespace RentACar.Application.TenantSettings;

/// <summary>
/// FAZ-82 — form ÖN-DOLDURMA varsayılanları çözücüsü (<c>Finance.KdvVarsayilan</c> ile AYNI desen).
///
/// <para>Neden <c>TenantSettingsService</c> değil de doğrudan repo: ayar servisi hassastır
/// (entegrasyon sırları) ve <c>Permission.ManageUsers</c> kapısı arkasındadır. Buradaki iki alan
/// hassas değildir ve okundukları ekranlar (rezervasyon/teklif/kira formu) OPERATÖR yetkisiyle
/// çalışır — servis üzerinden okumak operatörde 403 üretirdi.</para>
///
/// <para>SÖZLEŞME: bu sınıf yalnız FORMUN ÖN-SEÇİLİ/ÖN-DOLU değerini üretir. Kaydedilen değer daima
/// kullanıcının gönderdiğidir; hiçbir servis burayı okuyup bir kaydın alanını sessizce türetmez.
/// AYAR YOKKEN dönen değerler bugünkü sabitlerin BİREBİR aynısıdır (yakıt 8, fiyat türü seçilmemiş)
/// → ayar boşken davranış değişmez.</para>
/// </summary>
public sealed class FormDefaultResolver(ITenantSettingsRepository settings)
{
    /// <summary>Kira teslim formundaki "Çıkış Yakıt" ön-değeri. Ayar yoksa/aralık dışıysa <c>8</c>
    /// (bugüne kadar sayfaya gömülü olan sabit). Skala 0-12.</summary>
    public const int DefaultFuel = 8;

    /// <summary>Tenant'ın çıkış yakıt varsayılanı; yoksa <see cref="DefaultFuel"/>.
    /// Aralık dışı (eski/bozuk) kayıt da sabite düşer — teslim formunun HTML min/max'ıyla çakışmasın.</summary>
    public async Task<int> PickupFuelAsync(CancellationToken ct = default)
    {
        var v = (await settings.GetAsync(ct))?.VarsayilanYakitSeviyesi;
        return v is >= 0 and <= 12 ? v.Value : DefaultFuel;
    }

    /// <summary>
    /// Yeni rezervasyon/teklif/kira formundaki "Fiyat Türü" dropdown'ının ön-seçili değeri;
    /// ayar yoksa <c>null</c> = "seçilmemiş" (bugünkü davranış).
    ///
    /// <para>Tanınmayan bir metin saklanmışsa (elle DB düzenlemesi / eski veri) <c>null</c> döner:
    /// motorun tanımadığı bir değeri ön-seçili göstermek, operatöre "bu tarife modunda kaydediyorum"
    /// dedirtip aslında BAŞKA bir fiyat/KDV davranışı çalıştırırdı.</para>
    /// </summary>
    public async Task<string?> PriceTypeAsync(CancellationToken ct = default)
        => Pricing.PriceTypeOption.Normalize((await settings.GetAsync(ct))?.VarsayilanFiyatTuru);
}
