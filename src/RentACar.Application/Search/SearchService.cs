namespace RentACar.Application.Search;

/// <summary>
/// Global hızlı arama (roadmap C4): cross-module salt-okur. Boş/çok kısa sorgu → boş sonuç. Tenant
/// izolasyonu repo katmanında (RLS + query filter) otomatik. Yetki gerektirmez (oturum açmış kullanıcı;
/// sonuçlar zaten tenant-kapsamlı).
/// </summary>
public sealed class SearchService(ISearchRepository repository)
{
    private readonly ISearchRepository _repository = repository;

    public Task<IReadOnlyList<SearchHit>> SearchAsync(string? q, CancellationToken ct = default)
    {
        var term = (q ?? string.Empty).Trim();
        if (term.Length < 2) return Task.FromResult<IReadOnlyList<SearchHit>>([]); // gürültüyü önle
        return _repository.SearchAsync(term, perTypeLimit: 10, ct);
    }
}
