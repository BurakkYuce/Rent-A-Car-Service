using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Finance;

/// <summary>
/// Cari ekstre filtresi (FAZ-65 / canlı <c>hesap_extresi.aspx</c>). Tüm alanlar opsiyonel;
/// boş filtre = eski davranış (carinin TÜM hareketleri, tarihe göre artan).
/// </summary>
public sealed class CariEkstreFilter
{
    /// <summary>Hareket tarihi alt sınırı (dahil). Verilirse <see cref="CariEkstreSonuc.Devir"/> dolar.</summary>
    public DateTimeOffset? Bas { get; set; }

    /// <summary>Hareket tarihi üst sınırı (dahil — çağıran gün sonuna genişletir).</summary>
    public DateTimeOffset? Bit { get; set; }

    /// <summary>Hareketin ÖZGÜN dövizi (TRY/USD/EUR). Tutarlar defterde hem native hem base tutulur.</summary>
    public string? Doviz { get; set; }

    /// <summary>Kaynak türü (Fatura/Tahsilat/Ceza/…).</summary>
    public string? SourceType { get; set; }

    /// <summary>
    /// Bağlı kira sözleşmesinin durumu. YALNIZ tahsilat/ödeme satırlarına uygulanabilir:
    /// kira bağı defterde değil, <see cref="CashTransaction.RentalId"/>'de tutulur. Fatura, ceza,
    /// araç satışı gibi satırların kira bağı YOKTUR → bu filtre açıkken listeden düşerler.
    /// </summary>
    public RentalStatus? KiraDurum { get; set; }

    /// <summary>Tarih dışında bir daraltma var mı — yürüyen bakiyenin anlamını değiştirir.</summary>
    public bool TarihDisiDaraltmaVar
        => !string.IsNullOrWhiteSpace(Doviz) || !string.IsNullOrWhiteSpace(SourceType) || KiraDurum is not null;

    public bool BosMu => Bas is null && Bit is null && !TarihDisiDaraltmaVar;
}

/// <summary>
/// Ekstre sonucu: görünen satırlar + <see cref="Devir"/> (açılış bakiyesi).
///
/// <para><b>Devir neden şart:</b> ekstre yürüyen bakiye gösterir. Tarih filtresi uygulanınca
/// yürüyen bakiye sıfırdan başlasaydı ekranda YANLIŞ bir bakiye görünürdü. Devir, filtrenin
/// KAPSAM DIŞINDA bıraktığı önceki hareketlerin net toplamıdır (aynı tarih-dışı filtrelerle
/// hesaplanır) → yürüyen bakiye doğru devam eder.</para>
/// </summary>
public sealed record CariEkstreSonuc(decimal Devir, IReadOnlyList<AccountLedgerEntry> Satirlar)
{
    public static readonly CariEkstreSonuc Bos = new(0m, []);
}
