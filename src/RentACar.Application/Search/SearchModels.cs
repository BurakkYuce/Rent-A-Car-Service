using RentACar.Application.Authorization;

namespace RentACar.Application.Search;

/// <summary>Global arama sonucu (roadmap C4): tür + başlık + alt bilgi + hedef URL.</summary>
public sealed record SearchHit(string Tur, string Baslik, string? Alt, string Url);

public interface ISearchRepository
{
    /// <summary>Cross-module salt-okur arama (araç/cari/kira/rezervasyon/fatura). Tenant izolasyonu RLS+filter;
    /// F1.6: şube kapsamı <paramref name="kapsam"/> ile (liste ekranlarının C4/C5 şablonu — BranchScope.InScope ile birebir).</summary>
    Task<IReadOnlyList<SearchHit>> SearchAsync(string q, int perTypeLimit, BranchScope.BranchFilter kapsam, CancellationToken ct = default);
}
