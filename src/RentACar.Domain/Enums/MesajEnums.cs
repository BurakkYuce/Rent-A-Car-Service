namespace RentACar.Domain.Enums;

/// <summary>Müşteriye giden bildirimin kanalı.</summary>
public enum MesajKanal
{
    Eposta,
    Sms,
}

/// <summary>
/// Müşteriye giden bildirimin türü. Her tür için tenant başına kanal bazlı bir şablon tutulur.
///
/// <para>Yeni değerler SONA eklenir: şablon ve gönderim kayıtları enum ADIYLA değil sayısıyla
/// eşleşiyor olsaydı sıra değişikliği geçmiş kayıtların anlamını bozardı — bu yüzden hem sıra
/// korunur hem de kalıcı kayıtta ad saklanır (bkz. <c>GidenMesaj.Tur</c>).</para>
/// </summary>
public enum MesajTuru
{
    /// <summary>Halka açık siteden gelen rezervasyon talebi alındı.</summary>
    TalepAlindi,
    /// <summary>Rezervasyon oluşturuldu / onaylandı.</summary>
    RezervasyonOnay,
    /// <summary>Rezervasyon iptal edildi.</summary>
    RezervasyonIptal,
    /// <summary>Araç teslim (çıkış) gününden önce hatırlatma.</summary>
    TeslimHatirlatma,
    /// <summary>Araç iade (dönüş) gününde hatırlatma.</summary>
    IadeHatirlatma,
    /// <summary>Vadesi gelen/geçen ödeme hatırlatması.</summary>
    OdemeHatirlatma,
}

/// <summary>Giden mesajın yaşam döngüsü.</summary>
public enum GidenMesajDurum
{
    /// <summary>Kuyruğa alındı, henüz gönderilmedi (ya da yeniden denenecek).</summary>
    Kuyrukta,
    Gonderildi,
    /// <summary>Kalıcı olarak başarısız — yeniden denenmez (deneme hakkı bitti ya da kalıcı hata).</summary>
    Basarisiz,
    /// <summary>Müşteri o kanaldan iletişim izni vermediği için hiç gönderilmedi (KVKK/İYS).</summary>
    IzinYok,
}
