using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Integrations;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Notifications;

/// <summary>
/// Müşteriye giden bildirimin TEK giriş noktası: şablonu bulur, yer tutucuları doldurur, kalıcı
/// gönderim kaydını yazar, kanaldan gönderir ve sonucu kaydeder.
///
/// <para><b>Sıralama bilinçli:</b> önce KAYIT, sonra GÖNDERİM. Ters sırada, gönderim başarılı olup
/// kayıt yazılamadığında (yarış/çökme) aynı mesaj bir daha gönderilirdi. Kayıt önce yazılınca
/// benzersiz index ikinci denemeyi zaten reddeder; en kötü senaryo "kayıt var, gönderim yeniden
/// denenecek"tir — müşteriye çift mesaj değil.</para>
///
/// <para><b>Yetki:</b> gönderim bir OPERASYON yan etkisidir, ayrı bir izin kapısı YOKTUR — çağıran
/// akış (rezervasyon oluşturma, job) zaten kendi guard'ından geçmiştir. Şablon YÖNETİMİ ise ayarlar
/// sınıfıdır ve <see cref="Permission.ManageUsers"/> ister.</para>
/// </summary>
public sealed class CustomerNotificationService(
    IMessageRepository repository, NotificationChannelService channel, ICurrentUser currentUser,
    IMessageTemplateVersionStore? versionStore = null)
{
    /// <summary>F11.1b — şablon sürümleri (kimlik → xmin). ManageUsers.</summary>
    public async Task<IReadOnlyDictionary<Guid, string>> TemplateVersionsAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        return await Versions.VersionsAsync(ct);
    }

    /// <summary>
    /// F11.1b — şablonu sürüm karşılaştırmasıyla kaydeder (tam değiştirme PUT'u). Doğrulama
    /// <see cref="SaveTemplateAsync(MesajSablonInput, CancellationToken)"/> ile AYNI.
    /// </summary>
    public async Task SaveTemplateAsync(MesajSablonInput input, string? expectedVersion, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        ValidateTemplate(input);
        await Versions.UpsertAsync(input, expectedVersion, ct);
    }

    private IMessageTemplateVersionStore Versions => versionStore
        ?? throw new InvalidOperationException("IMessageTemplateVersionStore kayıtlı değil.");

    private static void ValidateTemplate(MesajSablonInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Govde))
            throw new ValidationException("Mesaj gövdesi boş olamaz.");
        if (input.Kanal == MessageChannel.Eposta && string.IsNullOrWhiteSpace(input.Konu))
            throw new ValidationException("E-posta şablonunda konu zorunludur.");
        if (input.Kanal == MessageChannel.Sms && input.Govde.Length > 600)
            throw new ValidationException("SMS gövdesi 600 karakteri aşamaz.");
    }

    /// <summary>Kalıcı başarısız sayılmadan önceki deneme hakkı.</summary>
    public const int MaxAttempts = 5;

    // ---- Şablon yönetimi (ayarlar sınıfı → ManageUsers) ----

    public async Task<IReadOnlyList<MesajSablonRow>> ListTemplatesAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        return await repository.ListTemplatesAsync(ct);
    }

    public async Task SaveTemplateAsync(MesajSablonInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        ValidateTemplate(input);
        await repository.UpsertTemplateAsync(input, ct);
    }

    public async Task<IReadOnlyList<GidenMesajRow>> OutgoingListAsync(
        GidenMesajFilter? filter = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ViewReports);
        return await repository.ListMessagesAsync(filter, ct);
    }

    // ---- Gönderim ----

    /// <summary>
    /// Bildirimi gönderir (idempotent). <paramref name="hasPermission"/> müşterinin o kanaldan iletişim
    /// izni — KVKK/İYS gereği ÇAĞIRAN tarafından çözülür (müşteri kaydından okunur) ve izin yoksa
    /// mesaj hiç oluşturulmaz.
    /// </summary>
    public async Task<MesajSonuc> GonderAsync(MesajIstegi request, bool hasPermission, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Alici))
            return new MesajSonuc(OutgoingMessageStatus.Basarisiz, "Alıcı adresi/numarası boş.");
        if (string.IsNullOrWhiteSpace(request.Anahtar))
            throw new ValidationException("Bildirim idempotency anahtarı boş olamaz.");

        // Zaten bu olay için kayıt var mı? Varsa ve gönderildiyse tekrar GÖNDERİLMEZ.
        var existing = await repository.FindMessageAsync(request.Anahtar, ct);
        if (existing is not null)
        {
            if (existing.Durum is OutgoingMessageStatus.Gonderildi or OutgoingMessageStatus.IzinYok
                || existing.DenemeSayisi >= MaxAttempts)
                return new MesajSonuc(existing.Durum, existing.Hata);
            return await TryAsync(existing, ct);
        }

        if (!hasPermission)
        {
            // İzinsiz gönderim YAPILMAZ ama kayıt YAZILIR: "neden gitmedi" sorusunun cevabı ve
            // aynı olay için tekrar tekrar denenmemesi için (terminal durum).
            var withoutConsent = Record(request, subject: null, body: "(izin yok — gönderilmedi)");
            withoutConsent.Durum = OutgoingMessageStatus.IzinYok;
            withoutConsent.Hata = "Müşteri bu kanaldan iletişim izni vermemiş (KVKK/İYS).";
            await repository.AddMessageAsync(withoutConsent, ct);
            return new MesajSonuc(OutgoingMessageStatus.IzinYok, withoutConsent.Hata);
        }

        var template = await repository.FindTemplateAsync(request.Tur, request.Kanal, ct);
        var htmlEscape = request.Kanal == MessageChannel.Eposta;
        var newItem = Record(request,
            subject: template is null ? null : TemplateFiller.Fill(template.Konu, request.Degerler, htmlEscape: false),
            body: template is null ? string.Empty : TemplateFiller.Fill(template.Govde, request.Degerler, htmlEscape));

        if (template is null || !template.Aktif)
        {
            // Şablon eksik/kapalı: KUYRUKTA bırakılır, kalıcı başarısız SAYILMAZ. Firma şablonu
            // tanımlayınca yeniden deneme işi mesajı gönderir — aksi halde kayıp mesaj olurdu.
            newItem.Durum = OutgoingMessageStatus.Kuyrukta;
            newItem.Hata = template is null
                ? $"'{request.Tur}' türü için {request.Kanal} şablonu tanımlı değil."
                : $"'{request.Tur}' türü için {request.Kanal} şablonu kapalı.";
            // Gövde boş kalır — şablon yazıldığında yeniden deneme onu DegerlerJson'dan üretir.
            newItem.Govde = newItem.Hata;
            await repository.AddMessageAsync(newItem, ct);
            return new MesajSonuc(OutgoingMessageStatus.Kuyrukta, newItem.Hata);
        }

        if (!await repository.AddMessageAsync(newItem, ct))
        {
            // Yarış: başka bir çağrı aynı anahtarı yazdı → onun sonucunu döndür, ikinci mesaj GÖNDERME.
            var racing = await repository.FindMessageAsync(request.Anahtar, ct);
            return new MesajSonuc(racing?.Durum ?? OutgoingMessageStatus.Kuyrukta, racing?.Hata);
        }

        return await TryAsync(newItem, ct);
    }

    /// <summary>
    /// Kuyruktaki bir kaydı (yeniden) göndermeyi dener ve sonucu kalıcılaştırır.
    /// Şablon eksikse gövde de eksiktir → önce şablon çözülür.
    /// </summary>
    public async Task<MesajSonuc> TryAsync(GidenMesaj message, CancellationToken ct = default)
    {
        var type = Enum.TryParse<MessageType>(message.Tur, out var t) ? t : (MessageType?)null;
        if (type is null)
        {
            await repository.UpdateMessageAsync(message.Id, m =>
            {
                m.Durum = OutgoingMessageStatus.Basarisiz;
                m.Hata = $"Bilinmeyen mesaj türü: {message.Tur}";
            }, ct);
            return new MesajSonuc(OutgoingMessageStatus.Basarisiz, $"Bilinmeyen mesaj türü: {message.Tur}");
        }

        var template = await repository.FindTemplateAsync(type.Value, message.Kanal, ct);
        if (template is null || !template.Aktif)
        {
            var error = $"'{message.Tur}' türü için {message.Kanal} şablonu " + (template is null ? "tanımlı değil." : "kapalı.");
            await repository.UpdateMessageAsync(message.Id, m => m.Hata = error, ct);
            return new MesajSonuc(OutgoingMessageStatus.Kuyrukta, error);
        }

        // Gövde ve konu HER denemede şablondan yeniden üretilir. Şablon olay anında yoktu ve
        // sonradan yazıldıysa mesaj burada doğru içeriğe kavuşur — değerler kayıtta saklandığı için
        // (DegerlerJson) yeniden doldurma mümkündür. Şablon değiştiyse gönderilen metin de güncel
        // olur; "kuyrukta bekleyen mesaj eski metinle gitti" durumu oluşmaz.
        var htmlEscape = message.Kanal == MessageChannel.Eposta;
        var values = ResolveValues(message.DegerlerJson);
        var subject = TemplateFiller.Fill(template.Konu, values, htmlEscape: false);
        var body = TemplateFiller.Fill(template.Govde, values, htmlEscape);
        if (string.IsNullOrWhiteSpace(body))
        {
            const string info = "Şablon gövdesi boş — gönderilecek içerik üretilemedi.";
            await repository.UpdateMessageAsync(message.Id, m =>
            {
                m.Durum = OutgoingMessageStatus.Basarisiz;
                m.Hata = info;
            }, ct);
            return new MesajSonuc(OutgoingMessageStatus.Basarisiz, info);
        }
        message.Konu = subject;
        message.Govde = body;

        var (ok, errorText) = message.Kanal switch
        {
            MessageChannel.Eposta => await TryEmailAsync(message, ct),
            MessageChannel.Sms => (await channel.SmsGonderAsync(message.Alici, message.Govde, ct), (string?)"SMS gönderilemedi."),
            _ => (false, "Bilinmeyen kanal."),
        };

        var status = ok ? OutgoingMessageStatus.Gonderildi : OutgoingMessageStatus.Kuyrukta;
        var attempt = message.DenemeSayisi + 1;
        if (!ok && attempt >= MaxAttempts) status = OutgoingMessageStatus.Basarisiz;

        await repository.UpdateMessageAsync(message.Id, m =>
        {
            m.Durum = status;
            m.DenemeSayisi = attempt;
            m.Konu = subject;
            m.Govde = body; // "ne gönderdik" sorusunun cevabı GERÇEKTEN gönderilen metin olsun
            m.Hata = ok ? null : errorText;
            if (ok) m.GonderimUtc = DateTimeOffset.UtcNow;
        }, ct);

        return new MesajSonuc(status, ok ? null : errorText);
    }

    private static IReadOnlyDictionary<string, string?> ResolveValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string?>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(json)
                   ?? new Dictionary<string, string?>();
        }
        catch (System.Text.Json.JsonException)
        {
            // Bozuk JSON gönderimi ÇÖKERTMEZ: yer tutucular dolmaz, şablon ham hâliyle gider ve
            // operatör bunu giden mesaj ekranında görür. Kayıp mesajdan iyidir.
            return new Dictionary<string, string?>();
        }
    }

    private async Task<(bool, string?)> TryEmailAsync(GidenMesaj message, CancellationToken ct)
    {
        var result = await channel.SendEmailAsync(
            message.Alici, message.Konu ?? string.Empty, message.Govde, TemplateFiller.PlainText(message.Govde), ct);
        return (result.Ok, result.Hata);
    }

    private static GidenMesaj Record(MesajIstegi request, string? subject, string body) => new()
    {
        DegerlerJson = System.Text.Json.JsonSerializer.Serialize(request.Degerler),
        Anahtar = request.Anahtar,
        Tur = request.Tur.ToString(),
        Kanal = request.Kanal,
        Alici = request.Alici.Trim(),
        Konu = subject,
        Govde = body,
        KaynakTur = request.KaynakTur,
        KaynakId = request.KaynakId,
    };
}
