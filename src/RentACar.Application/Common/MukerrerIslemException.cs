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
public sealed class MukerrerIslemException(string mesaj, MevcutIslem? mevcut = null) : ValidationException(mesaj)
{
    /// <summary>
    /// F4.4 adversarial HIGH-1: aynı anahtarla yazılmış KAYDIN kendisi (varsa). Doluysa istemci "zaten kaydedildi"
    /// der ve formu temizler — yeniden gönderime YÖNLENDİRMEZ (kaybolan yanıttan sonraki doğru tekrar ikinci
    /// tahsilata dönüşmesin). ProblemDetails'e <c>mevcut</c> uzantısı olarak yazılır (<c>UiHata</c>).
    /// </summary>
    public MevcutIslem? Mevcut { get; } = mevcut;

    /// <summary>
    /// F1.4 — AYNI anahtar FARKLI içerikle geldi (başka hedef/tutar). Sessiz idempotent başarı yalnız
    /// kayıtlı satırın hedefi ve tutarı gelen istekle BİREBİR eşleşirse verilir; eşleşmezse bu metinle
    /// 409 — aksi halde ikinci isteğin parası yazılmadığı hâlde kullanıcı "başarılı" görürdü.
    /// </summary>
    // "Sayfayı yenileyip yeniden deneyin" DENMEZ (adversarial LOW-A, 2026-09-21): kur boş bırakılıp çağrı anında
    // çözüldüğünde, ilk yazım BAŞARILI olmuşken kur değişince birebir tekrar da "farklı içerik" sayılır. Yenile +
    // yeni form anahtarıyla gönderen kullanıcı ÇİFT kayıt (ör. çift virman) üretirdi. Mesaj önce kontrole yönlendirir;
    // SPA da bu kodda yeni anahtarla OTOMATİK yeniden gönderim yapmaz.
    public const string FarkliIcerikMesaji =
        "Bu işlem anahtarı farklı içerikle zaten kullanılmış; işlem daha önce kaydedilmiş olabilir. Yeniden göndermeden önce kayıtları kontrol edin.";

    /// <summary>Anahtar farklı içerikle kullanılmış → 409.</summary>
    public static MukerrerIslemException FarkliIcerik() => new(FarkliIcerikMesaji);
}

/// <summary>409 <c>mukerrer</c>'de istemciye bildirilen, aynı anahtarla ZATEN yazılmış işlem (belge no + tutar).</summary>
public sealed record MevcutIslem(Guid Id, string BelgeNo, decimal Tutar, string Doviz);
