using RentACar.Application.Common;

namespace RentACar.Web.Api.Tanim;

/// <summary>Drop matrix row: return location (<c>lokasyon</c>) + branch (+ optional pick-up location) → one-off NET fee.</summary>
public sealed record DropDto(Guid Id, string Lokasyon, string Sube, string? CikisLokasyon, string? KarsilamaSekli,
    string? CalismaSekli, string? OzelIletisim, decimal? Ucret, int? MinGun, int? ManSuresi, decimal? Drop2, bool Aktif,
    string? Surum) : IDefinitionRow
{
    internal static readonly SortFieldMap<DropDto> Sort = SortFieldMap<DropDto>
        .Create(x => x.Id).Alan("lokasyon", x => x.Lokasyon).Alan("sube", x => x.Sube)
        .Alan("cikisLokasyon", x => x.CikisLokasyon).Alan("ucret", x => x.Ucret).Alan("aktif", x => x.Aktif);
}

/// <summary>POST/PUT body; <c>aktif</c> is REQUIRED (a missing value must not silently stop the fee); <c>surum</c>
/// REQUIRED on PUT. Amounts are JSON numbers (invariant).</summary>
public sealed record DropRequest(string? Lokasyon, string? Sube, string? CikisLokasyon, string? KarsilamaSekli,
    string? CalismaSekli, string? OzelIletisim, decimal? Ucret, int? MinGun, int? ManSuresi, decimal? Drop2, bool? Aktif,
    string? Surum = null) : IDefinitionRequest;

/// <summary>Occupancy surcharge rule: at <c>esikYuzde</c>% group occupancy the price rises by <c>carpanYuzde</c>%
/// (0–50). Validity days are Istanbul calendar days.</summary>
public sealed record OccupancyRuleDto(Guid Id, string Kod, string Ad, string? AracGrupKod, int EsikYuzde,
    decimal CarpanYuzde, string? Sube, bool SadeceKendiSubeleri, DateOnly? GecerlilikBas, DateOnly? GecerlilikBit,
    bool Aktif, string? Surum) : IDefinitionRow
{
    internal static readonly SortFieldMap<OccupancyRuleDto> Sort = SortFieldMap<OccupancyRuleDto>
        .Create(x => x.Id).Alan("kod", x => x.Kod).Alan("ad", x => x.Ad).Alan("aracGrupKod", x => x.AracGrupKod)
        .Alan("esikYuzde", x => x.EsikYuzde).Alan("carpanYuzde", x => x.CarpanYuzde).Alan("aktif", x => x.Aktif);
}

public sealed record OccupancyRuleRequest(string? Kod, string? Ad, string? AracGrupKod, int EsikYuzde, decimal CarpanYuzde,
    string? Sube, bool SadeceKendiSubeleri = false, DateOnly? GecerlilikBas = null, DateOnly? GecerlilikBit = null,
    bool Aktif = true, string? Surum = null) : IDefinitionRequest;

/// <summary>Bulk ladder: common scope once + (threshold, multiplier) steps → rules <c>{kodOnEk}-{esik}</c>.</summary>
public sealed record OccupancyBulkRequest(string? KodOnEk, string? AdOnEk, string? AracGrupKod, string? Sube,
    bool SadeceKendiSubeleri, DateOnly? GecerlilikBas, DateOnly? GecerlilikBit, IReadOnlyList<OccupancyStep>? Kademeler,
    bool Aktif = true);

public sealed record OccupancyStep(int EsikYuzde, decimal CarpanYuzde);

public sealed record OccupancyBulkResult(IReadOnlyList<Guid> Kimlikler);
