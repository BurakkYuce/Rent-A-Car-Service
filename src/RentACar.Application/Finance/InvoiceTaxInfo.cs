namespace RentACar.Application.Finance;

/// <summary>Fatura kesiminde opsiyonel vergi/belge metadata (parite #8; bilgi amaçlı — defter
/// postlamasına yansımaz). Tümü opsiyonel; verilmezse fatura eski davranışla kesilir.</summary>
public sealed record InvoiceTaxInfo(
    decimal? Otv = null,
    decimal? TevkifatOran = null,
    decimal? TevkifatTutar = null,
    decimal? DamgaVergisi = null,
    bool IadeMi = false,
    bool ManuelMi = false);
