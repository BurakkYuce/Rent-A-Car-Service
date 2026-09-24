using RentACar.Domain.Enums;

namespace RentACar.Application.Customers;

/// <summary>Cari liste arama + filtre + sayfalama. Query: ad/soyad/ünvan/vergi no (içeren);
/// TC yalnız TAM 11 hane girilirse blind-index ile eşleşir (KVKK/F2 — şifreli, kısmi arama yok).</summary>
public sealed class CustomerFilter
{
    public string? Query { get; set; }
    /// <summary>Query 11 haneli TC ise CustomerService'in doldurduğu HMAC özeti; repo bununla arar.</summary>
    public string? TcHash { get; set; }
    public CariType? Tip { get; set; }
    /// <summary>true → İYS izinli; false → izinsiz; null → tümü.</summary>
    public bool? IysIzinli { get; set; }
    /// <summary>true → uyarı bayraklı; null → tümü.</summary>
    public bool? Uyari { get; set; }
    /// <summary>true → kara listede; null → tümü.</summary>
    public bool? KaraListe { get; set; }
    /// <summary>true → pasif; false → yalnız aktif; null → tümü. (Customer.Pasif alanı.)</summary>
    public bool? Pasif { get; set; }
    /// <summary>true → araç verilmez işaretli.</summary>
    public bool? AracVerilmez { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    /// <summary>F7.1 — sunucu tarafı sıralama (uç katmanının BEYAZ LİSTE <c>SiralamaHaritasi</c>'ından; eşitlik bozucu
    /// dahil). null → varsayılan (Tip, Ünvan, Ad).</summary>
    public Func<IQueryable<Domain.Entities.Customer>, IOrderedQueryable<Domain.Entities.Customer>>? Siralama { get; set; }
}
