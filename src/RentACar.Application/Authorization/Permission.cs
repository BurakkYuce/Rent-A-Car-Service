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
    /// <summary>Finansal yazma: tahsilat, fatura, gider, araç satış, ceza yansıtma.</summary>
    FinanceWrite,
    /// <summary>Finansal/operasyonel raporları görüntüleme.</summary>
    ViewReports,

    // ---- İnceltme (2026-08-17, kullanıcı kararı): "yazabilen her şeyi yok edebilir" varsayımı
    // kırıldı. Yeni değerler SONA eklenir — enum int olarak claim/DB'ye sızmıyor (adıyla yazılıyor)
    // ama sıra değişikliği yine de gereksiz risk.

    /// <summary>Operasyonel belge YOK ETME: araç/müşteri silme, rezervasyon-kira-servis-sipariş-ceza-BAF
    /// iptali. <see cref="OperationsWrite"/>'tan ayrıdır — kayıt AÇABİLEN herkes kayıt YOK EDEMEZ
    /// (operatör kaydeder, silemez; sahada yanlış tuşla sözleşme iptali gerçek vakadır).</summary>
    OperationsDelete,

    /// <summary>Defteri geri saran işlemler: kasa/banka ters kaydı, iade faturası, defterli dış-hizmet
    /// iptali. <see cref="FinanceWrite"/>'tan ayrıdır — tahsilat GİREBİLEN herkes defter GERİ SARAMAZ;
    /// varsayılan matris davranışı korur (FinanceWrite sahipleri aynen alır), değeri kullanıcı-bazlı
    /// kısıtlamada: tek kişiden yalnız ters-kayıt yetkisi alınabilir.</summary>
    FinanceReverse
}
