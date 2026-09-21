using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Yeni arayüzün (Angular, F3.5) kişisel tablo düzeni: kullanıcı × tablo başına TEK satır — sütun
/// sırası/görünürlüğü/genişliği ve sıralama. Tenant-owned (EF filtre + FORCE RLS); kullanıcı boyutu
/// servis katmanında <c>ICurrentUser.UserId</c> ile uygulanır (uçta kullanıcı parametresi YOK).
///
/// <para><b>Bilinçli olarak <see cref="IAuditable"/> DEĞİL:</b> iş verisi değil, arayüz tercihidir;
/// sütun genişliği sürüklemek bile kayıt üretir — her biri AuditLog'a düşseydi denetim izi gürültüye
/// boğulurdu ve "kim neyi değiştirdi" sorusuna cevap aramak zorlaşırdı. Kişisel veri taşımaz.</para>
///
/// <para><see cref="Duzen"/> jsonb: servis DOĞRULANMIŞ DTO'yu serileştirir (ham istemci JSON'u
/// yazılmaz) — kolon biçimi sözleşmenin kendisidir, satır içeriği sınırlı ve şemalıdır.</para>
/// </summary>
public class TabloDuzeni : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Düzenin sahibi (Users FK, kullanıcı silinirse düzen de silinir).</summary>
    public Guid UserId { get; set; }

    /// <summary>Tablonun kararlı kodu (ör. <c>kiralar.liste</c>); küçük harf, rakam, nokta, tire.</summary>
    public string TabloKodu { get; set; } = string.Empty;

    /// <summary>Serileştirilmiş düzen (jsonb): <c>{ sutunlar: [...], siralama: [...] }</c>.</summary>
    public string Duzen { get; set; } = "{}";

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
