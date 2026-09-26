using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Müşteriye giden her mesajın kalıcı kaydı: ne gönderildi, kime, ne zaman, başarılı mı.
///
/// <para><b>Neden kayıt tutuluyor:</b> "müşteriye haber verildi mi" sorusunun tek dürüst cevabı
/// budur. Gönderim başarısızsa <see cref="Hata"/> saklanır — operatör "gitmedi" olduğunu ve
/// nedenini görür. Sessiz başarısızlık, bildirim altyapısının en sık görülen arızasıdır.</para>
///
/// <para><b>İdempotency:</b> <see cref="Anahtar"/> tenant içinde benzersizdir (kısmi unique index).
/// Anahtar deterministik üretilir; TEKRARLAYAN olaylarda (günlük hatırlatma) anahtara gün bileşeni
/// girer, tek seferlik olaylarda (rezervasyon onayı) kaynak id'si yeter. Böylece job iki kez koşsa
/// da müşteri aynı mesajı iki kez almaz — koruma uygulamada değil ŞEMADA.</para>
///
/// <para><b>IAuditable DEĞİL:</b> bu bir işlem kaydıdır, kullanıcı tarafından düzenlenen bir veri
/// değil; audit log'a kopyalamak aynı bilgiyi iki kez saklardı (bkz. bytea/blob entity kararı).</para>
/// </summary>
public class GidenMesaj : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Deterministik idempotency anahtarı (ör. <c>rez-onay:{rezervasyonId}</c>).</summary>
    public string Anahtar { get; set; } = string.Empty;

    /// <summary>Tür ADI olarak saklanır — enum sırası değişse de geçmiş kayıt anlamını korur.</summary>
    public string Tur { get; set; } = string.Empty;
    public MessageChannel Kanal { get; set; }

    /// <summary>E-posta adresi ya da telefon numarası.</summary>
    public string Alici { get; set; } = string.Empty;
    public string? Konu { get; set; }

    /// <summary>Gönderilen gövdenin kendisi (yer tutucular DOLDURULMUŞ hâli) — "ne yazdık" sorusu.</summary>
    public string Govde { get; set; } = string.Empty;

    /// <summary>
    /// Yer tutucu değerleri (JSON). Gövdeyi YENİDEN üretebilmek için saklanır.
    ///
    /// <para><b>Neden gerekli:</b> şablon olay anında tanımlı değilse gövde üretilemez. Değerler
    /// saklanmazsa o mesaj şablon sonradan yazılsa bile kalıcı ölür — ve bu, kurulumun ilk gününde
    /// (henüz hiçbir şablon yokken) gelen TÜM mesajların kaybı demektir. Değerlerle birlikte,
    /// yeniden deneme şablonu bulup gövdeyi o an üretir.</para>
    ///
    /// <para>Müşteri adı/plaka gibi operasyonel alanlar taşır; PII sınıfı bir alan (TC, ehliyet)
    /// yer tutucu listesinde YOKTUR ve buraya girmez.</para>
    /// </summary>
    public string? DegerlerJson { get; set; }

    public OutgoingMessageStatus Durum { get; set; } = OutgoingMessageStatus.Kuyrukta;
    public string? Hata { get; set; }
    public int DenemeSayisi { get; set; }

    /// <summary>İlgili kayıt türü ("Rezervasyon" / "Kira" / "Talep") — listede süzmek ve izlemek için.</summary>
    public string? KaynakTur { get; set; }
    public Guid? KaynakId { get; set; }

    public DateTimeOffset OlusturmaUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? GonderimUtc { get; set; }
}
