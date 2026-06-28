namespace RentACar.Application.Search;

/// <summary>Global arama sonucu (roadmap C4): tür + başlık + alt bilgi + hedef URL.</summary>
public sealed record SearchHit(string Tur, string Baslik, string? Alt, string Url);

public interface ISearchRepository
{
    /// <summary>Cross-module salt-okur arama (araç/cari/kira/rezervasyon/fatura). Tenant izolasyonu RLS+filter.</summary>
    Task<IReadOnlyList<SearchHit>> SearchAsync(string q, int perTypeLimit, CancellationToken ct = default);
}
