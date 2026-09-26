namespace RentACar.Web.Common;

/// <summary>
/// POST uçlarının kullanıcıya sonuç bildirerek geri dönmesi için tek yardımcı.
///
/// <para><b>Neden var (canlı şikayet):</b> "yeni kira oluşturulduğunda herhangi bir bildirim
/// kutusu gelmiyor". Ölçüm: 335 POST ucundan yalnız ~67'si başarı sinyali veriyordu; geri kalanı
/// sessizce listeye redirect ediyor, kullanıcı kaydın yazılıp yazılmadığını gözle arıyordu.</para>
///
/// <para><b>Neden anahtar <c>ok</c> değil <c>bilgi</c>:</b> 27 sayfa hâlâ <c>?ok=1</c> okuyup kendi
/// yerel <c>&lt;p class="ok"&gt;</c>'sini basıyor. Yeni anahtar farklı olduğu için bir uç
/// <c>bilgi=</c>'ye geçtiğinde o sayfanın yerel mesajı kendiliğinden susar ve global şerit devreye
/// girer — çift render YOK, 158 sayfada tek satır düzenleme YOK. Geçiş uç-uç yapılabilir.</para>
///
/// <para><b>Neden merkezî:</b> aynı URL kurma mantığı bugün dört modülde ayrı ayrı kopyalanmış
/// (AracKrediEndpoints.Durum/Geri, SiteIcerikEndpoints.Hata, FinanceEndpoints.SafeDonus/HataUrl,
/// BelgeSablonEndpoints). Hepsi buradan geçirilir.</para>
/// </summary>
public static class Result
{
    /// <summary>İşlem başarılı — hedefe döner ve kullanıcıya <paramref name="message"/> gösterilir.</summary>
    public static IResult Ok(string path, string message, string? part = null)
        => Results.Redirect(Url(path, "bilgi", message, part));

    /// <summary>
    /// İşlem başarısız. Anahtar <c>hata</c> — 103 sayfa bunu zaten okuyup <c>&lt;p class="error"&gt;</c>
    /// basıyor, o davranış korunur.
    /// </summary>
    public static IResult Error(string path, string message, string? part = null)
        => Results.Redirect(Url(path, "hata", message, part));

    /// <summary>
    /// Query parametresini doğru ayraçla ekler ve fragment'i DAİMA en sona koyar.
    ///
    /// <para>Tuzak: bugün uçlarda <c>$"/kiralar/{id}?ok=1{sekme}"</c> gibi elle birleştirme var.
    /// Yol zaten <c>?</c> içeriyorsa ikinci bir <c>?</c> parametreyi öldürür; fragment ortada
    /// kalırsa query'nin bir parçası sanılır. İkisi de burada tek yerde çözülür.</para>
    /// </summary>
    /// <remarks>SAF ve <c>public</c>: doğrudan test edilebilsin diye (repoda <c>InternalsVisibleTo</c>
    /// kullanılmıyor — TwilioWhatsAppService'teki aynı gerekçe).</remarks>
    public static string Url(string path, string key, string message, string? part)
    {
        // Çağıran fragment'i yolun içinde de vermiş olabilir ("/kiralar/5#sekme=finans").
        var hash = path.IndexOf('#');
        var body = hash >= 0 ? path[..hash] : path;
        var queue = hash >= 0 ? path[hash..] : string.Empty;

        if (!string.IsNullOrEmpty(part))
            queue = part.StartsWith('#') ? part : "#" + part;

        var separator = body.Contains('?') ? '&' : '?';
        return $"{body}{separator}{key}={Uri.EscapeDataString(message)}{queue}";
    }
}
