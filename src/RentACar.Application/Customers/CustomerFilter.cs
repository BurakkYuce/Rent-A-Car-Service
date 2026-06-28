using RentACar.Domain.Enums;

namespace RentACar.Application.Customers;

/// <summary>Cari liste arama + filtre + sayfalama. Query: ad/soyad/ünvan/TC/vergi no (içeren).</summary>
public sealed class CustomerFilter
{
    public string? Query { get; set; }
    public CariType? Tip { get; set; }
    /// <summary>true → İYS izinli; false → izinsiz; null → tümü.</summary>
    public bool? IysIzinli { get; set; }
    /// <summary>true → uyarı bayraklı; null → tümü.</summary>
    public bool? Uyari { get; set; }
    /// <summary>true → kara listede; null → tümü.</summary>
    public bool? KaraListe { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
