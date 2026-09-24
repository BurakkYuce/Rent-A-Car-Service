using RentACar.Domain.Enums;

namespace RentACar.Application.ServiceRecords;

public sealed class ServiceLineInput
{
    public string Aciklama { get; set; } = string.Empty;

    /// <summary>F9.1 — optional line id derived from the <c>Idempotency-Key</c> header (second add with the same key
    /// is a duplicate, ToplamIscilik does not grow twice). <c>null</c> → new id (Blazor path unchanged).</summary>
    public Guid? Id { get; set; }

    /// <summary>
    /// KDV HARİÇ net satır tutarı. <c>null</c> + <see cref="BirimFiyat"/> dolu ise
    /// <c>ServisKalemHesap.Net</c> ile TÜRETİLİR. null (=verilmedi) ile 0 (=bedelsiz kalem)
    /// bilerek ayrıştırıldı; <c>decimal</c> olsaydı "0" iki anlama gelirdi.
    /// </summary>
    public decimal? Tutar { get; set; }

    // FAZ-16 — canlı fiyatlandırma ızgarası bileşenleri (opsiyonel; serbest Tutar girişi korunur).
    public decimal? BirimFiyat { get; set; }
    public decimal? Miktar { get; set; }
    /// <summary>İndirim TUTARI (oran değil).</summary>
    public decimal? Indirim { get; set; }
    /// <summary>KDV ORANI 0..1.</summary>
    public decimal? KdvOran { get; set; }
}

/// <summary>
/// FAZ-16 — servis kaydının BİLGİ blokları (kaza/fatura/ödeme/yakıt/plan).
/// <para><b>Whitelist TİP DÜZEYİNDE:</b> <c>Durum</c>, <c>GirisKm</c>, <c>VehicleId</c>,
/// <c>ToplamIscilik</c>, <c>Yansitildi/YansitilanTutar/YansitilanCariId</c> bu tipte YOKTUR —
/// dolayısıyla "bilgi güncelle" yolu durum makinesini, işçilik toplamını ya da yansıtma
/// (defter yazan) alanları DEĞİŞTİREMEZ. RentalUpdateInput'takiyle aynı desen.</para>
/// </summary>
public class ServiceRecordBilgiInput
{
    public string? AtolyeAdi { get; set; }
    public string? Aciklama { get; set; }

    // Kaza / hasar
    public string? BeyanTuru { get; set; }
    public string? KarsiPlaka { get; set; }
    public string? KarsiTrafikSigortasi { get; set; }
    public DateTimeOffset? KazaTarihi { get; set; }
    public string? KazaSorumlusu { get; set; }
    public string? HasarDosyaNo { get; set; }
    public decimal? DegerKaybi { get; set; }

    // Fatura (BİLGİ — deftere yazmaz; FaturaGenelToplam servis tarafından TÜRETİLİR)
    public DateTimeOffset? FaturaTarihi { get; set; }
    public string? FaturaNo { get; set; }
    public decimal? FaturaTutar { get; set; }
    public decimal? FaturaKdv { get; set; }

    // Ödeme (BİLGİ — kasa/banka bakiyesini değiştirmez)
    public DateTimeOffset? OdemeTarihi { get; set; }
    public decimal? Odeme { get; set; }
    public string? OdemeDoviz { get; set; }
    public decimal? OdemeKur { get; set; }
    public OdemeYontemi? OdemeTuru { get; set; }
    public string? KasaKodu { get; set; }
    public string? HesapNo { get; set; }

    // Yakıt (0-12, kira sözleşmesiyle aynı ölçek)
    public int? CikisYakit { get; set; }
    public int? DonusYakit { get; set; }

    // Planlanan randevu penceresi (Rezerve akışı)
    public DateTimeOffset? PlanBasTarihi { get; set; }
    public DateTimeOffset? PlanBitTarihi { get; set; }
}

public sealed class ServiceRecordInput : ServiceRecordBilgiInput
{
    /// <summary>F9.1 — optional record id derived from the <c>Idempotency-Key</c> header. <c>null</c> → new id.</summary>
    public Guid? Id { get; set; }

    public Guid VehicleId { get; set; }
    public ServisTipi Tip { get; set; } = ServisTipi.Periyodik;
    public DateTimeOffset? GirisTarihi { get; set; }
    public int GirisKm { get; set; }
    public HasarSorumlu HasarSorumlu { get; set; } = HasarSorumlu.Yok;
    public decimal? KusurOrani { get; set; }

    /// <summary>
    /// FAZ-16: true → kayıt <see cref="ServisDurum.Rezerve"/> (planlanmış randevu) olarak açılır;
    /// araç servise GİRMEZ, "Servise Al" ile Açık'a döner. Varsayılan false → mevcut davranış.
    /// </summary>
    public bool Rezervasyon { get; set; }

    public List<ServiceLineInput> Lines { get; set; } = [];
}
