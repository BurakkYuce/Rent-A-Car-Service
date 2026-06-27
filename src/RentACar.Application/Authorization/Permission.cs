namespace RentACar.Application.Authorization;

/// <summary>
/// Yetki (izin) türleri. Sabit roller bu izinlere eşlenir (<see cref="RolePermissions"/>).
/// Servisler hassas işlemlerden önce ilgili izni doğrular (test edilebilir guard).
/// </summary>
public enum Permission
{
    /// <summary>Kullanıcı yönetimi (oluştur/pasifleştir/parola).</summary>
    ManageUsers,
    /// <summary>Operasyonel yazma: araç, cari, rezervasyon, kira, teslim/dönüş, servis, hasar.</summary>
    OperationsWrite,
    /// <summary>Finansal yazma: tahsilat, ters kayıt, fatura, gider, araç satış, ceza yansıtma.</summary>
    FinanceWrite,
    /// <summary>Finansal/operasyonel raporları görüntüleme.</summary>
    ViewReports
}
