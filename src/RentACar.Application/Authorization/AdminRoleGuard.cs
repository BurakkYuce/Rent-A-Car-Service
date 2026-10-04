using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Application.Authorization;

/// <summary>
/// Güvenlik tur 2 M1 (koordinatör kararı) — kullanıcı ve yetki yönetimi YAZMALARI (kullanıcı oluştur/güncelle/aktiflik/
/// parola sıfırla, izin istisnası ekle/kaldır, ekran yetkisi, yetki grubu) yalnız Admin ROLÜNE. ManageUsers kullanıcı
/// bazlı istisnayla devredilebildiği için (Operatör + ManageUsers) kıdem/kapsam/kendinde-olmayan-izin denetimleri yerine
/// en basit güvenli kural seçildi: istisna bu yazmaları AÇMAZ; okuma ve firma ayarları ManageUsers ile kalır.
/// Web uçları ayrıca rol kapısı taşır (çift savunma).
/// </summary>
public static class AdminRoleGuard
{
    public const string Message = "Kullanıcı ve yetki yönetimi değişikliklerini yalnız Admin rolü yapabilir.";

    public static void Require(ICurrentUser user)
    {
        if (user.Role != UserRole.Admin) throw new NoPermissionException(Message);
    }
}
