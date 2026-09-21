using RentACar.Application.Authorization;
using RentACar.Domain.Common;

namespace RentACar.Application.Search;

/// <summary>
/// Global hızlı arama (roadmap C4): cross-module salt-okur. Boş/çok kısa sorgu → boş sonuç. Tenant
/// izolasyonu repo katmanında (RLS + query filter) otomatik. İzin gerektirmez (oturum açmış kullanıcı).
/// <para><b>F1.6 güvenlik düzeltmesi — ŞUBE KAPSAMI:</b> 2026-09'a kadar arama şube kapsamı UYGULAMIYORDU;
/// başka şubenin operatörü liste ekranlarında göremediği aracı/kirayı/rezervasyonu/faturayı "Evrak No / Plaka ara"
/// kutusundan buluyordu. Artık <see cref="BranchScope.EffectiveFilter"/> repo'ya iner ve liste ekranlarıyla
/// AYNI kuralla (FK öncelikli, metin yedek) daraltır. Bilinçli davranış değişikliği: Blazor <c>/ara</c> da düzelir.
/// Cari (müşteri) kiracı geneli kalır — cari kaydında şube alanı yok, liste ekranı da kapsamsız.</para>
/// </summary>
public sealed class SearchService(ISearchRepository repository, ICurrentUser currentUser)
{
    private readonly ISearchRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<SearchHit>> SearchAsync(string? q, CancellationToken ct = default)
    {
        var term = (q ?? string.Empty).Trim();
        if (term.Length < 2) return Task.FromResult<IReadOnlyList<SearchHit>>([]); // gürültüyü önle
        return _repository.SearchAsync(term, perTypeLimit: 10, BranchScope.EffectiveFilter(_currentUser), ct);
    }
}
