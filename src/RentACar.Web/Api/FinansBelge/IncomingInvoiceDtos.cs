namespace RentACar.Web.Api.FinansBelge;

/// <summary>Gelen e-fatura satırı (tedarikçi belgesi; VKN kurumsal vergi no'dur). <c>giderlestirildi</c>: deftere
/// yansıdı (kırılım/bağlama artık değişmez).</summary>
public sealed record IncomingInvoiceRow(
    Guid Id, string Ettn, string GonderenVkn, string GonderenUnvan, DateTimeOffset Tarih,
    decimal NetTutar, decimal KdvTutar, decimal GenelToplam, string Doviz, string Durum, string? RedNedeni,
    string? Aciklama, decimal? Kdv20Matrah, decimal? Kdv20, decimal? Kdv10Matrah, decimal? Kdv10,
    decimal? Kdv1Matrah, decimal? Kdv1, decimal? Kdv0Matrah, Guid? AracId, string? Plaka,
    Guid? GiderKategoriId, Guid? CariId, string? CariAd, string? GiderTipi, bool Giderlestirildi,
    DateTimeOffset? GiderlestirilmeTarihi);

/// <summary>Elle gelen fatura girişi (entegrasyon yokken). net + KDV = genel toplam olmalı.</summary>
public sealed record IncomingInvoiceCreateRequest(
    string? Ettn, string? GonderenVkn, string? GonderenUnvan, decimal NetTutar, decimal KdvTutar, decimal GenelToplam,
    DateTimeOffset? Tarih = null, string? Doviz = null, string? Aciklama = null);

/// <summary>KDV oran kırılımı + araç/kategori/tedarikçi bağı (deftere YAZMAZ; giderleştirmenin girdisi).</summary>
public sealed record IncomingInvoiceLinkRequest(
    decimal? Kdv20Matrah = null, decimal? Kdv20 = null, decimal? Kdv10Matrah = null, decimal? Kdv10 = null,
    decimal? Kdv1Matrah = null, decimal? Kdv1 = null, decimal? Kdv0Matrah = null,
    Guid? AracId = null, Guid? GiderKategoriId = null, Guid? CariId = null, string? GiderTipi = null);

/// <summary>Giderleştirme: <c>odemeYontemi</c> Nakit | Banka | AcikHesap (varsayılan AcikHesap; cari boşsa
/// faturaya bağlı cari).</summary>
public sealed record IncomingInvoiceExpenseRequest(string? OdemeYontemi = null, Guid? CariId = null, string? Sube = null);

public sealed record IncomingInvoiceRejectRequest(string? Neden = null);

/// <summary>GİB gelen kutusu çekme aralığı (gün, İstanbul).</summary>
public sealed record IncomingInvoiceSyncRequest(DateOnly Bas, DateOnly Bit);

public sealed record IncomingInvoiceSyncResult(int Eklenen);

public sealed record IncomingInvoiceExpenseResult(Guid Id, int GiderSatiri);

public sealed record IncomingInvoiceStateResult(Guid Id, string Durum);
