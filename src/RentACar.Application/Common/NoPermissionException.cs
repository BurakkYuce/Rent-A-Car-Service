namespace RentACar.Application.Common;

/// <summary>
/// Yetki reddi: rol/izin matrisi (<c>PermissionGuard</c>), şube kapsamı (<c>BranchScope</c>) ya da ekran
/// yetkisi (<c>ScreenPermissionService</c>) işlemi reddetti.
///
/// <para><b>Neden ayrı tip (F1.1):</b> bugün bu üç guard düz <see cref="ValidationException"/> fırlatıyordu;
/// JSON API'de yetki reddi "doğrulama hatası" (400) gibi görünüyordu. Yeni SPA 403 <c>yetki_yok</c>'u
/// FORMU SİLMEDEN uyarı bandı olarak gösterir; 400'ü ise alan hatası sanıp formun içine basardı.
/// <see cref="ValidationException"/>'dan türediği için mevcut Blazor yakalayıcıları aynen çalışır.</para>
/// </summary>
public sealed class NoPermissionException(string message) : ValidationException(message);
