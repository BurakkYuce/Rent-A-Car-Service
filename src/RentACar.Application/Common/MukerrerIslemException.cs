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
public sealed class MukerrerIslemException(string mesaj) : ValidationException(mesaj);
