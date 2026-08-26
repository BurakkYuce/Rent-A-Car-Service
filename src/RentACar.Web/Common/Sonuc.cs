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
public static class Sonuc
{
    /// <summary>İşlem başarılı — hedefe döner ve kullanıcıya <paramref name="mesaj"/> gösterilir.</summary>
    public static IResult Tamam(string yol, string mesaj, string? parca = null)
        => Results.Redirect(Url(yol, "bilgi", mesaj, parca));

    /// <summary>
    /// İşlem başarısız. Anahtar <c>hata</c> — 103 sayfa bunu zaten okuyup <c>&lt;p class="error"&gt;</c>
    /// basıyor, o davranış korunur.
    /// </summary>
    public static IResult Hata(string yol, string mesaj, string? parca = null)
        => Results.Redirect(Url(yol, "hata", mesaj, parca));

    /// <summary>
    /// Query parametresini doğru ayraçla ekler ve fragment'i DAİMA en sona koyar.
    ///
    /// <para>Tuzak: bugün uçlarda <c>$"/kiralar/{id}?ok=1{sekme}"</c> gibi elle birleştirme var.
    /// Yol zaten <c>?</c> içeriyorsa ikinci bir <c>?</c> parametreyi öldürür; fragment ortada
    /// kalırsa query'nin bir parçası sanılır. İkisi de burada tek yerde çözülür.</para>
    /// </summary>
    /// <remarks>SAF ve <c>public</c>: doğrudan test edilebilsin diye (repoda <c>InternalsVisibleTo</c>
    /// kullanılmıyor — TwilioWhatsAppService'teki aynı gerekçe).</remarks>
    public static string Url(string yol, string anahtar, string mesaj, string? parca)
    {
        // Çağıran fragment'i yolun içinde de vermiş olabilir ("/kiralar/5#sekme=finans").
        var diyez = yol.IndexOf('#');
        var govde = diyez >= 0 ? yol[..diyez] : yol;
        var kuyruk = diyez >= 0 ? yol[diyez..] : string.Empty;

        if (!string.IsNullOrEmpty(parca))
            kuyruk = parca.StartsWith('#') ? parca : "#" + parca;

        var ayrac = govde.Contains('?') ? '&' : '?';
        return $"{govde}{ayrac}{anahtar}={Uri.EscapeDataString(mesaj)}{kuyruk}";
    }
}
