using System.Text.RegularExpressions;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.EInvoicing;

/// <summary>
/// e-Belge yaşam döngüsü kuralları — SAF (DB/ağ yok). Fatura kesimi (kuyruğa alma), gönderim işi ve kullanıcı
/// eylemleri aynı kuralları buradan okur.
/// </summary>
public static class EDocumentPolicy
{
    /// <summary>Geçici hatada en çok bu kadar deneme; sonra <see cref="EDocumentStatus.Hata"/> (operatör yeniden kuyruğa alır).</summary>
    public const int MaxAttempts = 5;

    /// <summary>Gönderim kilidinin süresi — işi alan çalışan çökerse bu süre sonunda satır yeniden alınabilir.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);

    /// <summary>Mesaj kolonunun uzunluğu (DB <c>varchar(512)</c>).</summary>
    public const int MessageMax = 512;

    public const string RefundNotSent =
        "İade faturası e-belge olarak otomatik gönderilmez: iade belgesini kural olarak hizmeti iade alan alıcı düzenler. " +
        "Kaynak belge e-Arşiv ise kaynak faturadan e-belge iptali yapılabilir.";

    /// <summary>
    /// Yeni kesilen fatura için e-belge kaydı. Fatura ile AYNI transaction'da yazılır (fatura var ↔ e-belge kaydı var).
    /// Ayarlar hazırsa <see cref="EDocumentStatus.Gonderilecek"/>; değilse <see cref="EDocumentStatus.Taslak"/> +
    /// "bağlı değil" sebebi. İade faturası hiçbir zaman otomatik kuyruğa girmez.
    /// </summary>
    public static EDocument ForNewInvoice(Invoice invoice, Domain.Entities.TenantSettings? settings, DateTimeOffset now)
    {
        var doc = new EDocument
        {
            InvoiceId = invoice.Id,
            TenantId = invoice.TenantId,
            Ettn = Guid.NewGuid(),
            CreatedAtUtc = now,
        };
        if (invoice.IadeMi)
        {
            doc.Durum = EDocumentStatus.Taslak;
            doc.SonMesaj = RefundNotSent;
        }
        else if (EInvoiceReadiness.Gap(settings) is { } gap)
        {
            doc.Durum = EDocumentStatus.Taslak;
            doc.SonMesaj = EInvoiceReadiness.NotConnected(gap);
        }
        else
        {
            doc.Durum = EDocumentStatus.Gonderilecek;
        }
        return doc;
    }

    /// <summary>Geçici hatadan sonra bekleme: 5 dk, 15 dk, 1 sa, sonra 4 sa.</summary>
    public static TimeSpan Backoff(int attempt) => attempt switch
    {
        <= 1 => TimeSpan.FromMinutes(5),
        2 => TimeSpan.FromMinutes(15),
        3 => TimeSpan.FromHours(1),
        _ => TimeSpan.FromHours(4),
    };

    private static readonly Regex ElevenDigits = new(@"(?<![0-9])[0-9]{11}(?![0-9])", RegexOptions.CultureInvariant);

    /// <summary>
    /// Kayda/loga gidecek metni temizler: bilinen kişisel değerler (ör. çözülmüş TCKN) ve HER 11 haneli rakam dizisi
    /// maskelenir (entegratör hata metni alıcının TCKN'sini geri yansıtabilir), uzunluk kırpılır.
    /// </summary>
    public static string? Sanitize(string? text, params string?[] secrets)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text;
        foreach (var secret in secrets ?? [])
            if (!string.IsNullOrWhiteSpace(secret) && secret.Length >= 4)
                s = s.Replace(secret, new string('*', secret.Length), StringComparison.Ordinal);
        s = ElevenDigits.Replace(s, "***********");
        s = string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return s.Length <= MessageMax ? s : s[..(MessageMax - 3)] + "...";
    }
}
