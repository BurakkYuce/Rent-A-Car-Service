namespace RentACar.Application.Common;

/// <summary>Sayfalı sonuç: o sayfanın öğeleri + toplam adet + sayfa bilgisi.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling(Total / (double)PageSize);
    public bool HasPrev => Page > 1;
    public bool HasNext => Page < TotalPages;
}
