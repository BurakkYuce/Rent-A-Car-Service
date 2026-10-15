using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Bir faturanın e-belge (e-Fatura / e-Arşiv) gönderim kaydı — fatura başına TEK satır.
///
/// <para><b>Neden faturadan ayrı tablo:</b> <c>Invoices</c> DB düzeyinde değişmezdir (<c>rc_prevent_mutation</c>);
/// gönderim durumu ise zamanla değişir (kuyruk → gönderildi / hata → yeniden kuyruk). Durumu faturaya yazmak
/// değişmezliği delmek olurdu. Fatura tutarları burada TUTULMAZ — XML her gönderimde değişmez faturadan üretilir.</para>
///
/// <para><b>ETTN:</b> belge oluşturulurken bir kez üretilir ve değişmez; gönderimin idempotency anahtarıdır
/// (<c>(TenantId, Ettn)</c> ve <c>(TenantId, InvoiceId)</c> benzersiz). ETTN'in VAR olması belgenin gönderildiği
/// anlamına GELMEZ — yalnız <see cref="EDocumentStatus.Gonderildi"/> durumu entegratör onayıdır (dürüst stub kuralı).</para>
///
/// <para><b>KVKK:</b> üretilen XML (alıcı TCKN'si içerebilir) SAKLANMAZ; yalnız SHA-256 özeti (<see cref="XmlOzet"/>).
/// <see cref="SonMesaj"/> TCKN maskelenerek yazılır.</para>
/// </summary>
public class EDocument : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid InvoiceId { get; set; }

    /// <summary>Evrensel tekil belge no (UUID) — belge oluşturulurken üretilir, değişmez.</summary>
    public Guid Ettn { get; set; } = Guid.NewGuid();

    public EDocumentStatus Durum { get; set; } = EDocumentStatus.Taslak;

    /// <summary>Gönderim anında belirlenir (alıcı e-Fatura kullanıcısı mı?); gönderilmeden önce null.</summary>
    public EDocumentKind? Tur { get; set; }
    /// <summary>UBL-TR senaryosu (TEMELFATURA / TICARIFATURA / EARSIVFATURA) — gönderim anında belirlenir.</summary>
    public string? Profil { get; set; }
    /// <summary>UBL-TR fatura tipi (SATIS / IADE …) — gönderim anında belirlenir.</summary>
    public string? FaturaTipi { get; set; }

    /// <summary>Gönderim denemesi sayısı (talep edilen her deneme +1). &gt;1 iken gönderimden ÖNCE entegratörde
    /// durum sorulur — önceki deneme ulaşmış olabilir, aynı ETTN ikinci kez gönderilmez.</summary>
    public int DenemeSayisi { get; set; }
    public DateTimeOffset? SonDenemeUtc { get; set; }
    /// <summary>Geçici hatadan sonra bir sonraki denemenin en erken anı (geri çekilme).</summary>
    public DateTimeOffset? SonrakiDenemeUtc { get; set; }
    /// <summary>Gönderim kilidi (kiralama süresi): işi alan çalışan bu ana kadar satırın sahibidir. Çökmede süre dolar,
    /// satır yeniden alınabilir.</summary>
    public DateTimeOffset? KilitBitisUtc { get; set; }

    public DateTimeOffset? GonderimUtc { get; set; }
    /// <summary>Entegratörün kendi belge referansı (varsa).</summary>
    public string? EntegratorRef { get; set; }
    /// <summary>Entegratörün son bildirdiği durum metni (bilgi).</summary>
    public string? EntegratorDurum { get; set; }
    /// <summary>Son durum açıklaması / hata (TCKN maskeli, kullanıcıya gösterilebilir).</summary>
    public string? SonMesaj { get; set; }
    /// <summary>Gönderilen XML'in SHA-256 özeti (hex). XML'in kendisi saklanmaz (KVKK).</summary>
    public string? XmlOzet { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
