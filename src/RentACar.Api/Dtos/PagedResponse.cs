using RentACar.Application.Common;

namespace RentACar.Api.Dtos;

/// <summary>Sayfalı API yanıtı: öğeler + toplam + sayfa bilgisi.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize, int TotalPages);

/// <summary>PagedResult&lt;TSrc&gt; (Application) → PagedResponse&lt;TDto&gt; eşleme yardımcısı.</summary>
public static class PagedResponse
{
    public static PagedResponse<TDto> From<TSrc, TDto>(PagedResult<TSrc> r, Func<TSrc, TDto> map)
        => new(r.Items.Select(map).ToList(), r.Total, r.Page, r.PageSize, r.TotalPages);
}
