namespace RentACar.Domain.Enums;

/// <summary>
/// Kesilmiş bir faturanın e-belge (e-Fatura / e-Arşiv) gönderim durumu. Fatura kaydının KENDİSİ değişmez
/// (<c>rc_prevent_mutation</c>); bu durum ayrı <c>EBelgeler</c> satırında yaşar.
/// <list type="bullet">
/// <item><b>Taslak</b> — fatura kesildi ama gönderim kuyruğunda DEĞİL (entegratör bağlı değil ya da iade
/// faturası gibi otomatik gönderilmeyen bir belge). Sebep <c>SonMesaj</c>'da yazılıdır.</item>
/// <item><b>Gonderilecek</b> — kuyrukta; gönderim işi alıp entegratöre iletecek (geçici hatada yeniden dener).</item>
/// <item><b>Gonderildi</b> — entegratör belgeyi KABUL ETTİ (ETTN ancak bu durumda "gönderildi" sayılır).</item>
/// <item><b>Hata</b> — kalıcı hata (veri eksik/geçersiz ya da deneme sınırı doldu); düzeltilip yeniden kuyruğa alınabilir.</item>
/// <item><b>Iptal</b> — e-belge iptal edildi (gönderilmişse entegratörde, gönderilmemişse yerel olarak).</item>
/// </list>
/// </summary>
public enum EDocumentStatus
{
    Taslak = 0,
    Gonderilecek = 1,
    Gonderildi = 2,
    Hata = 3,
    Iptal = 4
}

/// <summary>e-Belge türü: alıcı GİB'de kayıtlı e-Fatura kullanıcısıysa e-Fatura, değilse e-Arşiv.</summary>
public enum EDocumentKind
{
    EFatura = 0,
    EArsiv = 1
}
