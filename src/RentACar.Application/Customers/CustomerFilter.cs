namespace RentACar.Application.Customers;

/// <summary>Cari liste arama + sayfalama. Query: ad/soyad/ünvan/TC/vergi no (içeren).</summary>
public sealed class CustomerFilter
{
    public string? Query { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
