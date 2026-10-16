namespace RentACar.Domain.Enums;

/// <summary>
/// KABİS (Kiralık Araç Bildirim Sistemi) bildiriminin türü. Kalıcı kayıtta enum ADIYLA saklanır
/// (<c>KabisBildirimleri.Tur</c>) — yeni değerler SONA eklenir, mevcutların adı değişmez.
/// </summary>
public enum KabisNoticeType
{
    /// <summary>Teslim anında kiralayan + araç bildirimi (1774 s. Kanun ek md. 3: "araç teslimi esnasında").</summary>
    Kiralama,
    /// <summary>Araç iade (dönüş) bildirimi.</summary>
    Iade,
    /// <summary>Bildirilmiş kiralamanın iptali (teslim edilmiş sözleşme iptal edildiğinde).</summary>
    Iptal,
}

/// <summary>Kira başına KABİS bildiriminin yaşam döngüsü. Kalıcı kayıtta ADIYLA saklanır.</summary>
public enum KabisNoticeStatus
{
    /// <summary>Kuyrukta: henüz bildirilemedi (gönderilmedi, geçici hata ya da bağlantı yapılandırılmadı).
    /// Yeniden deneme işi alır.</summary>
    Bekliyor,
    /// <summary>Karşı sistem bildirimi KABUL ETTİ (dış referans dolu) ya da operatör portaldan elle
    /// bildirdiğini kayda geçirdi (<c>Elle</c>).</summary>
    Bildirildi,
    /// <summary>Kalıcı hata: deneme hakkı bitti ya da veri eksik/geçersiz. Operatör düzeltip yeniden dener.</summary>
    Hata,
    /// <summary>Operatör gerekçeyle "gerekli değil" işaretledi ya da olay bildirimi anlamsızlaştı (kira iptal).</summary>
    GerekliDegil,
}

/// <summary>Bildirimin gittiği genel kolluk: Emniyet (EGM KABİS) ya da Jandarma (JGK KABİS).</summary>
public enum KabisAuthority
{
    Emniyet,
    Jandarma,
}
