namespace RentACar.Application.AracSiparisleri;

/// <summary>
/// Araç sipariş oluşturma/güncelleme girişi (roadmap L3 + FAZ-17 derinlik).
///
/// <para><b>PARA ÇİTİ:</b> <see cref="PiyasaFiyat"/>/<see cref="OpsFiyat"/>/<see cref="FiloFiyat"/>
/// SALT BİLGİdir; resmi birim tutar <see cref="BirimFiyat"/>'tır ve sipariş toplamı
/// (Adet × BirimFiyat) bu fazda DEĞİŞMEDİ. Sipariş kaydı hiçbir defter satırı yazmaz.</para>
/// </summary>
public sealed class AracSiparisInput
{
    public string? Tedarikci { get; set; }
    /// <summary>FAZ-17: tedarikçi cari bağı (opsiyonel). Cari bakiyesine/deftere HİÇBİR kayıt gitmez.</summary>
    public Guid? TedarikciCariId { get; set; }
    public DateTimeOffset? SiparisTarihi { get; set; }
    public DateTimeOffset? BeklenenTeslim { get; set; }
    /// <summary>FAZ-17: sipariş sözleşmesinin imza günü (canlıdaki Imza_Tarih).</summary>
    public DateTimeOffset? ImzaTarih { get; set; }
    /// <summary>FAZ-17: tedarikçi/bayi dosya referansı (canlıdaki Dosya_No).</summary>
    public string? DosyaNo { get; set; }
    public string? SatisTemsilci { get; set; }
    public string? OzelTemsilci { get; set; }
    public string? Marka { get; set; }
    public string? Tip { get; set; }
    public string? Grup { get; set; }
    public string? Versiyon { get; set; }
    public string? Opsiyon { get; set; }
    public string? Renk { get; set; }
    public string? IcRenk { get; set; }
    public string? KaynakTip { get; set; }
    public string? SatisTipi { get; set; }
    public string? TsbKayitNo { get; set; }
    /// <summary>FAZ-17: ilişkili araç kredisi (canlıdaki Kredi_No). Krediye taksit/gider yazmaz.</summary>
    public Guid? KrediId { get; set; }
    public int Adet { get; set; } = 1;
    /// <summary>RESMİ birim tutar — toplam bundan hesaplanır.</summary>
    public decimal BirimFiyat { get; set; }
    /// <summary>FAZ-17 fiyat katmanı — SALT BİLGİ, hiçbir toplama girmez. null = girilmemiş.</summary>
    public decimal? PiyasaFiyat { get; set; }
    /// <summary>FAZ-17 fiyat katmanı — SALT BİLGİ, hiçbir toplama girmez. null = girilmemiş.</summary>
    public decimal? OpsFiyat { get; set; }
    /// <summary>FAZ-17 fiyat katmanı — SALT BİLGİ, hiçbir toplama girmez. null = girilmemiş.</summary>
    public decimal? FiloFiyat { get; set; }
    public string Doviz { get; set; } = "TRY";
    public decimal Kur { get; set; } = 1m;
    public string? Aciklama { get; set; }
}
