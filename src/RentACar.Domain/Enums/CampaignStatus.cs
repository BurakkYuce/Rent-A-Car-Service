namespace RentACar.Domain.Enums;

/// <summary>
/// Kiralama kuralı / kampanya yaşam döngüsü (FAZ-73, canlı kampanya_ara.aspx karşılığı).
///
/// <para><b>Fiyat motoru YALNIZ <see cref="Aktif"/> durumunu okur</b> — diğer dört durum kuralı
/// otomatik seçimden çıkarır. Bu, göç öncesindeki <c>RentalRule.Aktif == true</c> koşuluyla
/// BİREBİR aynı davranıştır (migration backfill'i <c>Aktif=true → Aktif</c>,
/// <c>Aktif=false → Pasif</c> eşler), yani mevcut hiçbir kuralın fiyatı değişmez.</para>
///
/// <para><b>Neden <c>Aktif bool</c> silinmedi:</b> tek yazma noktasından (RentalRuleService.Apply)
/// iki alan BİRLİKTE set edilir; <c>Aktif == (KampanyaDurum == Aktif)</c> değişmezi korunur.
/// Kolonun kaldırılması ayrı bir temizlik işidir (geriye uyum).</para>
///
/// <para>Sayısal değerler KALICIDIR (DB'de int saklanır) — yeniden numaralandırılamaz.</para>
/// </summary>
public enum CampaignStatus
{
    /// <summary>Hazırlık aşamasında; hiçbir yerde uygulanmaz.</summary>
    Taslak = 0,

    /// <summary>Onaylı ama henüz yürürlükte değil (ileri tarihli kampanya) — motor SEÇMEZ.</summary>
    Planlandi = 1,

    /// <summary>Yürürlükte — fiyat motorunun otomatik seçimine giren TEK durum.</summary>
    Aktif = 2,

    /// <summary>Yürürlükten kaldırıldı (geçici); motor seçmez.</summary>
    Pasif = 3,

    /// <summary>İptal edildi (kalıcı, iz olarak durur); motor seçmez.</summary>
    Iptal = 4
}
