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

    /// <summary>İadesi kesilmiş faturanın e-belgesi gönderilmez (defterde satış geri alındı; GİB'e karşılıksız satış gitmesin).</summary>
    public const string RefundedSourceNotSent =
        "Bu fatura iade edildi; e-belgesi gönderilmez (defterde satış geri alındı).";

    /// <summary>İade, kaynağın henüz gönderilmemiş e-belgesini aynı transaction'da bu mesajla iptal eder.</summary>
    public const string RefundedSourceCancelled =
        "Fatura iade edildi; e-belge gönderilmeden iptal edildi.";

    /// <summary>Test ortamı kabulü (GİB'e ulaşmadı).</summary>
    public const string TestEnvironmentSent =
        "Entegratörün TEST ortamına gönderildi; GİB'e ulaşmadı. Canlı ortama geçince yeniden gönderin.";

    /// <summary>Gönderilmiş e-Arşiv iptali iadesiz yapılmaz (adversarial M3).</summary>
    public const string SentArchiveNeedsRefund =
        "Gönderilmiş e-Arşiv yalnız fatura iade edildikten sonra iptal edilebilir: önce faturayı iade edin (defterdeki " +
        "satış ve KDV geri alınır), sonra e-Arşiv belgesini iptal edin.";

    /// <summary>Kilit süresinin sonundan bırakılan pay: adaptör çağrıları kilit dolmadan kesilir (adversarial L1).</summary>
    public static readonly TimeSpan LeaseMargin = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gönderim işinin entegratör çağrılarına ayrılan süre: kilit bitişinden <see cref="LeaseMargin"/> önce. Kilit
    /// dolduktan sonra süren bir çağrı, bu arada yapılan bir yerel iptalin arkasından belgeyi teslim edebilirdi.
    /// </summary>
    public static TimeSpan CallBudget(DateTimeOffset leaseUntil, DateTimeOffset clock)
    {
        var budget = leaseUntil - clock - LeaseMargin;
        return budget > TimeSpan.FromSeconds(1) ? budget : TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// Sonucu belirsiz bir CANLI denemenin "entegratörde yok" sayılabilmesi için geçmesi gereken süre (adversarial N1).
    /// İstemci zaman aşımında (HttpClient iptali) istek sunucuya ulaşmış olabilir ve sunucu onu bizden sonra işler: o
    /// arada gelen tek bir "bulunamadı" yanıtı kesin değildir. Bizim taraftaki çağrı <see cref="CallBudget"/> ile kilit
    /// süresinin (10 dk) altında kesilir; sunucu tarafının işleme süresi bilinmediğinden kilidin üç katı (30 dk)
    /// beklenir. Bu süre dolmadan yerel iptal reddedilir, iade edilmiş belgenin iptali ve tür/ortam değişikliği ertelenir.
    /// Test ortamı denemesi GİB'e ulaşmadığından bekletilmez.
    /// </summary>
    public static readonly TimeSpan AttemptSettle = TimeSpan.FromMinutes(30);

    /// <summary>Son deneme canlıda (ya da ortamı bilinmiyor) ve üzerinden <see cref="AttemptSettle"/> geçmedi.</summary>
    public static bool LiveAttemptMayStillLand(EDocument doc, DateTimeOffset now)
        => doc.SonDenemeCanli != false && doc.SonDenemeUtc is { } at && now - at < AttemptSettle;

    public static string AttemptNotSettled(EDocument doc, DateTimeOffset now)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((doc.SonDenemeUtc!.Value + AttemptSettle - now).TotalMinutes));
        return $"Önceki gönderim denemesinin sonucu henüz kesinleşmedi (entegratör isteği geç işleyebilir); {minutes} dakika sonra yeniden deneyin.";
    }

    /// <summary>İade edilmiş fatura, canlı denemesi kesinleşene kadar bekletilir (sonra iptal edilir).</summary>
    public const string RefundedAwaitingSettle =
        "Fatura iade edildi; önceki gönderim denemesinin sonucu kesinleşince e-belge iptal edilecek.";

    /// <summary>Varsayılan GİB e-belge serileri (ayar boşsa). İç fatura serisinden (RNT) ve birbirinden farklı.</summary>
    public const string EInvoiceSeriesDefault = "EFA";
    public const string EArchiveSeriesDefault = "EAR";

    /// <summary>
    /// Belge türünün GİB seri kodu. GİB e-Arşiv kılavuzu v1.18 §6: "e-Arşiv kapsamında düzenledikleri faturalarda
    /// e-fatura uygulamasında kullandıkları birim kodlardan farklı birim kodları belirleyerek kullanacaklardır."
    /// </summary>
    public static string Series(Domain.Entities.TenantSettings? s, EDocumentKind kind) => kind == EDocumentKind.EFatura
        ? s?.EFaturaSeriKodu ?? EInvoiceSeriesDefault
        : s?.EArsivSeriKodu ?? EArchiveSeriesDefault;

    /// <summary>Belge numarası sayacı: seri + yıl + ORTAM. Test ortamı ayrı sayaç kullanır — test gönderimleri canlı
    /// serisinde boşluk bırakmasın. Önek iç fatura sayacından (<c>F:</c>) ayrıdır.</summary>
    public static string DocumentCounterKey(string series, int year, bool live)
        => $"EB:{series}:{year:D4}" + (live ? "" : ":test");

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

    /// <summary>
    /// Hane arası en çok 3 karakterlik ayraç: boşluk, nokta, eğik çizgi, alt çizgi, tire, en/em tire ("123 456 789 01",
    /// "123 – 456", "123_456", "123/456"). Önce NFKC (tam genişlikli "１２３" → "123"), görünmez biçim karakterleri (U+200B
    /// vb.) silinir, boşluklar tek ' 'e indirilir (adversarial N4, #410 L3/L5/L6).
    /// <para>Bilinçli DIŞARIDA kalanlar (yanlış pozitif dengesi, #410 L5/L6):</para>
    /// <list type="bullet">
    /// <item><b>Virgül, artı, parantez:</b> Türkçe tutar biçimi ("200,00 / 1.250,00", "1.000,00 + 2.500,00") ve listeler
    /// 11 haneye birleşip maskelenirdi; virgüllü TCKN gruplaması ("123,456,789,01") yakalanmaz.</item>
    /// <item><b>İki nokta:</b> saat ("15.10.2026 08:30:15") tarihle birleşip 11 hane sayılırdı.</item>
    /// <item><b>4+ karakterlik ayraç</b> (" -- "): üst sınır 3; genişletmek tarih/tutar dizilerini birleştirirdi.</item>
    /// </list>
    /// Bilinen sır (çözülmüş TCKN) verilirse aynı ayraç kuralıyla ayrıca maskelenir; asıl savunma entegratör mesajına
    /// kişisel veri koymamaktır (port sözleşmesi).
    /// </summary>
    private const string DigitSeparator = @"[\s./_\-–—]{0,3}";

    private static readonly Regex FormatChars = new(@"\p{Cf}", RegexOptions.CultureInvariant);

    private static readonly Regex ElevenDigits = new(
        @"(?<![0-9])[0-9](?:" + DigitSeparator + @"[0-9]){10}(?![0-9])", RegexOptions.CultureInvariant);

    /// <summary>
    /// Kayda/loga gidecek metni temizler: bilinen kişisel değerler (ör. çözülmüş TCKN) ve HER 11 haneli rakam dizisi
    /// maskelenir (entegratör hata metni alıcının TCKN'sini geri yansıtabilir), uzunluk kırpılır. Haneler arasında
    /// boşluk/nokta/tire olan gruplu yazım da yakalanır (adversarial L3).
    /// </summary>
    public static string? Sanitize(string? text, params string?[] secrets)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // Önce boşluk normalizasyonu: NBSP / dar boşluk / sekme / satır sonu → tek ' ' (char.IsWhiteSpace).
        var s = string.Join(' ', FormatChars.Replace(text.Normalize(System.Text.NormalizationForm.FormKC), string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        foreach (var secret in secrets ?? [])
        {
            if (string.IsNullOrWhiteSpace(secret) || secret.Length < 4) continue;
            s = s.Replace(secret, new string('*', secret.Length), StringComparison.Ordinal);
            if (secret.All(char.IsAsciiDigit))
                s = Regex.Replace(s, string.Join(DigitSeparator, secret.Select(c => c.ToString())),
                    new string('*', secret.Length), RegexOptions.CultureInvariant);
        }
        s = ElevenDigits.Replace(s, "***********");
        return s.Length <= MessageMax ? s : s[..(MessageMax - 3)] + "...";
    }
}
