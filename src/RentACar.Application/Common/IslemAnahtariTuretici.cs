using System.Security.Cryptography;
using System.Text;

namespace RentACar.Application.Common;

/// <summary>
/// F1.4 — istemcinin <c>Idempotency-Key</c> başlığından SUNUCUDA <c>IslemAnahtari</c> türetir.
///
/// <para><b>Neden türetme (ham değer DEĞİL):</b> <c>IslemAnahtari</c> bir <c>uuid</c> kolonudur ve manuel
/// faturada doğrudan <b>birincil anahtar</b> olur (<c>InvoiceService.CreateManualAsync</c>); depozito
/// irat, kasa/cari virman künyesi de onu <c>Id</c> yapar. İstemci değeri doğrudan kullanılsaydı:
/// (1) <c>{userId}:{key}</c> gibi metin kolona sığmaz; (2) iki kullanıcının/kiracının aynı değeri
/// birbirinin kaydına çarpar (PK'ler kiracı-GLOBAL); (3) id tahmin edilebilir olurdu. Bu yüzden anahtar
/// RFC 4122 <b>UUIDv5</b>(sabit ad alanı, <c>"{tenantId}|{userId}|{başlık}"</c>) olarak türetilir: aynı
/// kullanıcının aynı başlığı AYNI anahtarı verir (çift gönderim yakalanır), farklı kullanıcı/kiracı asla
/// aynı anahtarı üretemez.</para>
///
/// <para><b>Öncelik (<see cref="Sec"/>):</b> sunucunun deterministik anahtarı (ör. <c>TahsilatAnahtar</c>,
/// <c>CashService.RowKey</c>) varsa DAİMA o kullanılır; başlık yalnız deterministik anahtarı olmayan
/// işlemde devreye girer. Rastgele istemci anahtarı deterministik anahtarı ezseydi iki sekme/iki
/// kullanıcı aynı kiraya çift tahsilat yazabilirdi.</para>
///
/// <para>Saf fonksiyon: I/O yok, saat yok. Test vektörleri bağımsız olarak Python
/// <c>uuid.uuid5</c> ile hesaplanıp sabitlendi.</para>
/// </summary>
public static class IslemAnahtariTuretici
{
    /// <summary>Sabit UUIDv5 ad alanı. <b>ASLA DEĞİŞTİRİLMEZ</b> — değişirse uçuştaki yeniden denemeler
    /// farklı anahtar üretir ve çift gönderim korumasını bir sürüm boyunca kaybederiz.</summary>
    public static readonly Guid AdAlani = new("2adf1c10-4f5c-4c27-bad3-3294d841ee0c");

    /// <summary>Başlık değeri en az bu uzunlukta olmalı (düşük entropili sabit değer — ör. "1" — her
    /// gönderimi "mükerrer" yapardı; SPA <c>crypto.randomUUID()</c> 36 karakter üretir).</summary>
    public const int EnAzUzunluk = 16;

    /// <summary>Başlık değeri en fazla bu uzunlukta olabilir.</summary>
    public const int EnFazlaUzunluk = 128;

    /// <summary>Başlık değeri geçerli mi: <see cref="EnAzUzunluk"/>–<see cref="EnFazlaUzunluk"/> karakter,
    /// yalnız görünür ASCII (0x21–0x7E; boşluk/kontrol/ASCII-dışı yok). Kestrel zaten ASCII-dışı başlığı
    /// reddeder; kural burada da tutulur ki türetme girdisi ortamdan bağımsız tek anlamlı olsun.</summary>
    public static bool GecerliMi(string? deger)
    {
        if (deger is null || deger.Length < EnAzUzunluk || deger.Length > EnFazlaUzunluk) return false;
        foreach (var c in deger)
            if (c < '!' || c > '~') return false;
        return true;
    }

    /// <summary>
    /// <c>UUIDv5(AdAlani, "{tenantId:D}|{userId:D}|{istemciAnahtari}")</c>. Guid'ler küçük harf "D"
    /// biçiminde, ad UTF-8. Geçersiz girdi <see cref="ValidationException"/> (alan: <c>Idempotency-Key</c>);
    /// boş tenant/kullanıcı <see cref="ArgumentException"/> (programlama hatası — kimliksiz istekte
    /// türetme yapılmaz, yoksa tüm anonim istekler aynı ad alanını paylaşırdı).
    /// </summary>
    public static Guid Turet(Guid tenantId, Guid userId, string istemciAnahtari)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Kiracı boş olamaz.", nameof(tenantId));
        if (userId == Guid.Empty) throw new ArgumentException("Kullanıcı boş olamaz.", nameof(userId));
        if (!GecerliMi(istemciAnahtari))
            throw new ValidationException(
                $"Idempotency-Key geçersiz: {EnAzUzunluk}-{EnFazlaUzunluk} karakter, yalnız görünür ASCII olmalı.",
                "Idempotency-Key");
        return UuidV5(AdAlani, $"{tenantId:D}|{userId:D}|{istemciAnahtari}");
    }

    /// <summary>
    /// Öncelik kuralı: deterministik (sunucu) anahtar doluysa O; değilse başlıktan türetilen; ikisi de
    /// yoksa <c>null</c> (işlem bugünkü anahtarsız davranışıyla çalışır). <see cref="Guid.Empty"/> "yok"
    /// sayılır — servisler de boş Guid'i anahtarsız kabul ediyor.
    /// </summary>
    public static Guid? Sec(Guid? deterministik, Guid? basliktan)
    {
        if (deterministik is { } d && d != Guid.Empty) return d;
        if (basliktan is { } b && b != Guid.Empty) return b;
        return null;
    }

    /// <summary>RFC 4122 §4.3 UUIDv5: SHA-1(ad alanı [ağ sırası] ‖ ad), ilk 16 bayt, sürüm 5 + RFC varyantı.
    /// SAF ve <c>public</c>: RFC/Python örnek vektörüyle doğrudan test edilebilsin diye (repoda
    /// <c>InternalsVisibleTo</c> yok).</summary>
    public static Guid UuidV5(Guid adAlani, string ad)
    {
        var nsBytes = adAlani.ToByteArray(bigEndian: true);
        var adBytes = Encoding.UTF8.GetBytes(ad);
        var girdi = new byte[nsBytes.Length + adBytes.Length];
        nsBytes.CopyTo(girdi, 0);
        adBytes.CopyTo(girdi, nsBytes.Length);

        var hash = SHA1.HashData(girdi); // RFC 4122 v5 tanımı gereği SHA-1 (güvenlik değil, ad→uuid eşlemesi)
        var b = hash.AsSpan(0, 16).ToArray();
        b[6] = (byte)((b[6] & 0x0F) | 0x50); // sürüm 5
        b[8] = (byte)((b[8] & 0x3F) | 0x80); // RFC 4122 varyantı
        return new Guid(b, bigEndian: true);
    }
}
