using RentACar.Domain.Enums;

namespace RentACar.Application.Expenses;

/// <summary>
/// Gider listesi filtresi (FAZ-63 / canlı <c>gider_ara.aspx</c>). Tüm alanlar opsiyonel; boş filtre
/// = eski davranış (şube kapsamındaki tüm giderler). Şube KAPSAMI bundan bağımsızdır ve her zaman
/// uygulanır — <see cref="Sube"/> yalnız kapsam İÇİNDE daraltır, kapsamı genişletemez.
/// </summary>
public sealed class ExpenseFilter
{
    /// <summary>Evrak no / açıklama / gider no içinde geçen metin (küçük-büyük harf duyarsız).</summary>
    public string? Ara { get; set; }

    /// <summary>Tedarikçi cari.</summary>
    public Guid? CariId { get; set; }

    /// <summary>Plaka (kısmi). Araç tablosundan çözülür.</summary>
    public string? Plaka { get; set; }

    /// <summary>Gider türü ("Gider Adı").</summary>
    public ExpenseType? Tip { get; set; }

    /// <summary>Ofis/şube adı (kapsam içinde daraltma).</summary>
    public string? Sube { get; set; }

    /// <summary>Tarih alt sınırı (dahil).</summary>
    public DateTimeOffset? Bas { get; set; }

    /// <summary>Tarih üst sınırı (dahil — çağıran gün sonuna genişletir).</summary>
    public DateTimeOffset? Bit { get; set; }
}
