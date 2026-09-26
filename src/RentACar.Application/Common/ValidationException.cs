namespace RentACar.Application.Common;

/// <summary>
/// İş kuralı / doğrulama ihlali. Web katmanı bunu kullanıcıya gösterilebilir
/// hata olarak ele alır (500 değil, form hatası).
///
/// <para><b>Alt tipler (F1.1, Angular geçişi):</b> yeni SPA hatayı TÜRÜNE göre işler — alan hatası
/// formda kalır, çakışma formu silmez, mükerrer kaydı yeniden yükler, yetki reddi uyarı bandı olur.
/// Bu ayrım için <see cref="NoPermissionException"/>, <see cref="DuplicateOperationException"/> ve çakışma
/// istisnaları (<c>AvailabilityConflictException</c>, <c>DuplicateCariException</c>,
/// <c>DuplicatePlakaException</c>) buradan TÜRER: mevcut <c>catch (ValidationException)</c> blokları
/// (Blazor uçları, <c>DogrulamaHatasiMiddleware</c>) hepsini aynen yakalamaya devam eder — Blazor
/// davranışı değişmez.</para>
/// </summary>
public class ValidationException : Exception
{
    /// <param name="message">Kullanıcıya gösterilecek Türkçe mesaj.</param>
    /// <param name="alan">Hatanın ait olduğu form alanı (ör. <c>"plaka"</c>). Doluysa SPA mesajı o alanın
    /// altında gösterir; boşsa genel hata olarak. Blazor bunu kullanmaz.</param>
    public ValidationException(string message, string? alan = null) : base(message)
    {
        Alan = alan;
    }

    /// <summary>Hatanın ait olduğu form alanı; bilinmiyorsa null.</summary>
    public string? Alan { get; }
}
