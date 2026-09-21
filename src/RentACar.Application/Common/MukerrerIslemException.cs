namespace RentACar.Application.Common;

/// <summary>
/// Mükerrer gönderim: AYNI işlem (aynı <c>IslemAnahtari</c> / deterministik anahtar / idempotency kısıtı)
/// ikinci kez yazılmak istendi ve veritabanının idempotency kısıtı reddetti.
///
/// <para><b>Neden ayrı tip (F1.1):</b> bu bir form hatası DEĞİL — ilk gönderim zaten kaydedildi. Yeni SPA
/// 409 <c>mukerrer</c>'i görünce form hatası basmak yerine kaydı YENİDEN YÜKLER. İş benzersizliği ihlali
/// (belge no, TC, plaka, müsaitlik) ise <c>cakisma</c>'dır ve formu korur — ikisi karıştırılmamalı.
/// Ayrım <c>PostgresException.ConstraintName</c> ile yapılır (<c>IdempotencyKisiti</c>).
/// <see cref="ValidationException"/>'dan türediği için mevcut Blazor yakalayıcıları aynen çalışır.</para>
/// </summary>
public sealed class MukerrerIslemException(string mesaj) : ValidationException(mesaj)
{
    /// <summary>
    /// F1.4 — AYNI anahtar FARKLI içerikle geldi (başka hedef/tutar). Sessiz idempotent başarı yalnız
    /// kayıtlı satırın hedefi ve tutarı gelen istekle BİREBİR eşleşirse verilir; eşleşmezse bu metinle
    /// 409 — aksi halde ikinci isteğin parası yazılmadığı hâlde kullanıcı "başarılı" görürdü.
    /// </summary>
    public const string FarkliIcerikMesaji =
        "Bu işlem anahtarı farklı içerikle zaten kullanılmış; sayfayı yenileyip yeniden deneyin.";

    /// <summary>Anahtar farklı içerikle kullanılmış → 409.</summary>
    public static MukerrerIslemException FarkliIcerik() => new(FarkliIcerikMesaji);
}
