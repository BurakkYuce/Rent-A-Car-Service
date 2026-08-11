using RentACar.Domain.Enums;

namespace RentACar.Application.VehicleSales;

/// <summary>
/// Araç satış liste filtresi (FAZ-18 — canlı <c>arac_satis_ara.aspx</c> paritesi). Boş alan = kısıt yok.
/// Tarih aralığı satışın <see cref="Domain.Entities.VehicleSale.Tarih"/> alanına uygulanır.
/// <para>
/// <b>Ofis:</b> <c>VehicleSale</c> mali belgedir ve DB-immutable'dır; ona şube kolonu EKLEMEDİK
/// (mali belgeye sonradan anlam yükleyen kolon açmak, geçmiş satırlarda backfill tahmini gerektirirdi).
/// Ofis filtresi bu yüzden SATILAN ARACIN şubesi (<c>Vehicle.Sube</c>) üzerinden çalışır — canlıdaki
/// "hangi ofisin aracı satıldı" sorusunun karşılığı.
/// </para>
/// </summary>
public sealed class VehicleSaleFilter
{
    /// <summary>Plaka PARÇA eşleşmesi. Arama terimi VehicleService.PlakaAnahtar ile normalize edilir.</summary>
    public string? Plaka { get; set; }

    /// <summary>Alıcı cari (tekil seçim).</summary>
    public Guid? AliciCariId { get; set; }

    public SatisDurum? Durum { get; set; }

    /// <summary>Devir durumu bayrağı (canlı Satisi_Verildi). null = tümü.</summary>
    public bool? SatisiVerildi { get; set; }

    /// <summary>Satılan aracın şubesi (tam ad eşleşmesi — şube master'ından seçilir).</summary>
    public string? Ofis { get; set; }

    /// <summary>Satış tarihi ≥ (dahil).</summary>
    public DateTimeOffset? Bas { get; set; }

    /// <summary>Satış tarihi ≤ (dahil — çağıran gün sonunu geçirir).</summary>
    public DateTimeOffset? Bit { get; set; }

    /// <summary>Hiçbir alan dolu değilse filtre yok demektir (liste "Temizle" bağlantısı için).</summary>
    public bool Bos => string.IsNullOrWhiteSpace(Plaka) && AliciCariId is null && Durum is null
        && SatisiVerildi is null && string.IsNullOrWhiteSpace(Ofis) && Bas is null && Bit is null;
}

/// <summary>
/// Araç satışının SAF (DB'siz) yardımcı hesapları — test edilebilir olsun diye Razor'dan ayrıldı.
/// Hiçbiri para postlamaz; ekran gösterimi içindir.
/// </summary>
public static class SatisHesap
{
    /// <summary>
    /// Satış tarihinden bugüne GEÇEN GÜN (canlı listedeki "Geçen Süre" kolonu). İki taraf da UTC
    /// takvim gününe indirgenir — yerel/UTC karışımı CI ile lokalde farklı sonuç üretirdi.
    /// Gelecek tarihli satışta negatif döner (gizlenmez: veri hatası görünür kalsın).
    /// </summary>
    public static int GecenGun(DateTimeOffset tarih, DateTimeOffset simdi)
        => (int)(simdi.UtcDateTime.Date - tarih.UtcDateTime.Date).TotalDays;
}
