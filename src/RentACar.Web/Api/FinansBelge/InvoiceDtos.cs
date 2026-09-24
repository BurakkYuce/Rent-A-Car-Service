namespace RentACar.Web.Api.FinansBelge;

/// <summary>Fatura listesi satırı. Tutarlar faturanın KENDİ dövizinde; iade faturası pozitif saklanır
/// (<c>iadeMi</c> ile işaretlenir). Cari adı KVKK tek kuralıyla (<c>MusteriGorunumu</c>); vergi no/TC DÖNMEZ.
/// <c>kiraId</c>: kira faturası ya da fark faturasının kirası.</summary>
public sealed record InvoiceListRow(
    Guid Id, string No, DateTimeOffset Tarih, DateTimeOffset? VadeTarihi, string Durum, bool IadeMi, bool ManuelMi,
    Guid CariId, string CariAd, Guid? KiraId, string? SozlesmeNo, string? Plaka, string? Ofis,
    decimal NetTutar, decimal KdvTutar, decimal GenelToplam, string Doviz, decimal Kur,
    bool EFaturaGonderildi, string? EFaturaEttn, Guid? KaynakFaturaId);

/// <summary>Fatura listesinin bir dövizdeki toplamı (faturanın KENDİ dövizinde; kurla çevrilmez). <c>genelToplam</c> iade
/// faturalarını da (pozitif saklanır) içerir; iadelerin payı <c>iadeToplam</c>.</summary>
public sealed record InvoiceCurrencyTotal(
    string Doviz, int Adet, decimal NetTutar, decimal KdvTutar, decimal GenelToplam, int IadeAdet, decimal IadeToplam);

/// <summary>Fatura listesi özeti: liste süzgeçleriyle aynı kümenin döviz kırılımı (<c>adet</c> = tüm dövizler).</summary>
public sealed record InvoiceSummary(int Adet, IReadOnlyList<InvoiceCurrencyTotal> Dovizler);

/// <summary>Fatura satırı (kesildiği andaki değerler; yeniden hesaplanmaz).</summary>
public sealed record InvoiceLineDto(
    Guid Id, string Aciklama, decimal Miktar, decimal BirimNetFiyat, decimal KdvOrani,
    decimal SatirNet, decimal SatirKdv, decimal SatirToplam);

/// <summary>Fatura detayı. <c>pdfAdresi</c>: tek kaynak QuestPDF çıktısı (<c>/faturalar/{id}/pdf</c>, SPA yönlendirmesi
/// dışında — tam sayfa açılır). <c>iadeFaturaId</c>: bu faturanın iadesi kesildiyse onun kimliği (kaynak başına tek iade).
/// e-Fatura: entegrasyon yapılandırılmadığında <c>eFaturaGonderildi=false</c> ve ETTN boştur (dürüst stub).</summary>
public sealed record InvoiceDetail(
    Guid Id, string No, DateTimeOffset Tarih, DateTimeOffset? VadeTarihi, string Durum, bool IadeMi, bool ManuelMi,
    Guid CariId, string CariAd, Guid? KiraId, Guid? KaynakFaturaId, Guid? IadeFaturaId,
    decimal NetTutar, decimal KdvTutar, decimal GenelToplam, string Doviz, decimal Kur,
    decimal? Otv, decimal? TevkifatOran, decimal? TevkifatTutar, decimal? DamgaVergisi,
    string? IslemSube, string? EvrakNo, string? FaturaOzelKod, string? OdemeTuru, string? GonderimSekli,
    string? KdvSifirSebep, bool EFaturaGonderildi, string? EFaturaEttn, string PdfAdresi,
    IReadOnlyList<InvoiceLineDto> Satirlar);

/// <summary>Fatura detay listesi satırı (fatura satırı seviyesinde). <c>isaretliToplamTl</c>: iade eksi, TL baz.</summary>
public sealed record InvoiceLineListRow(
    Guid FaturaId, string FaturaNo, DateTimeOffset Tarih, string Durum, bool IadeMi, bool ManuelMi,
    string Doviz, decimal Kur, Guid CariId, string CariAd,
    string Aciklama, decimal Miktar, decimal BirimNetFiyat, decimal KdvOrani,
    decimal SatirNet, decimal SatirKdv, decimal SatirToplam, decimal IsaretliToplamTl,
    Guid? KiraId, string? SozlesmeNo, string? Plaka, string? CikisOfisi);

/// <summary>Manuel (kiradan bağımsız) fatura. <c>kdvOrani</c> KESİR (0,20 = %20; boş → 0,20). Döviz yok: TRY.
/// Bilgi alanları ve vergi alanları deftere YANSIMAZ.</summary>
public sealed record ManualInvoiceRequest(
    Guid CariId, decimal NetTutar, decimal? KdvOrani = null, string? Aciklama = null,
    DateTimeOffset? Tarih = null, DateTimeOffset? VadeTarihi = null,
    string? IslemSube = null, string? EvrakNo = null, string? FaturaOzelKod = null, string? OdemeTuru = null,
    string? GonderimSekli = null, string? KdvSifirSebep = null,
    decimal? Otv = null, decimal? TevkifatOran = null, decimal? TevkifatTutar = null, decimal? DamgaVergisi = null);

/// <summary>Toplu kira faturası. <c>kdvOrani</c> boş → her kiranın kendi zinciri.</summary>
public sealed record BatchInvoiceRequest(IReadOnlyList<Guid> KiraIds, decimal? KdvOrani = null);

public sealed record DocumentResult(Guid Id, string No);

/// <summary>Toplu kesim sonucu: kesilen belgeler + atlanan kiralar (sebep metniyle; gizlenmez).</summary>
public sealed record BatchInvoiceResult(IReadOnlyList<DocumentResult> Kesilen, IReadOnlyList<string> Atlananlar);
