namespace RentACar.Web.Api.FinansBelge;

/// <summary>Gider listesi satırı. Tutarlar giderin KENDİ dövizinde (<c>doviz</c>, <c>kur</c>). <c>odenen/kalan</c>:
/// açık hesap giderinde ödeme takibi (<c>takipEdilir=true</c>); nakit/banka gider kayıt anında ödenmiştir.
/// <c>sozlesmeNo</c>: bağlı kira sözleşmesinin numarası (kira silinmiş ya da bağ yoksa boş).</summary>
public sealed record ExpenseListRow(
    Guid Id, string No, string Tip, DateTimeOffset Tarih, Guid? AracId, string? Plaka, Guid? CariId, string? CariAd,
    string? Sube, string? EvrakNo, decimal NetTutar, decimal KdvOrani, decimal KdvTutar, decimal GenelToplam,
    string Doviz, decimal Kur, string OdemeYontemi, string KasaBankaHesap, string? Aciklama, Guid? KiraId,
    DateTimeOffset? Vade, DateTimeOffset? OdemeTarihi, decimal Odenen, decimal Kalan, bool TakipEdilir,
    string? SozlesmeNo);

/// <summary>Gider ödeme takibi kaydı (deftere YAZMAZ).</summary>
public sealed record ExpensePaymentDto(
    Guid Id, int Sira, decimal Tutar, decimal KalanSonrasi, DateTimeOffset Tarih, string? MakbuzNo, string? Aciklama,
    string? IslemYapan);

public sealed record ExpenseDetail(ExpenseListRow Gider, string? HazirAciklama, Guid? HesapId,
    IReadOnlyList<ExpensePaymentDto> Odemeler);

/// <summary>Yeni gider. <c>tip</c>: Genel | Arac | Personel | Sigorta | Mtv | Muayene | Diger …; <c>odemeYontemi</c>:
/// Nakit (Kasa) | Banka | AcikHesap (tedarikçi cari zorunlu). <c>kdvOrani</c> KESİR (0,20 = %20). <c>doviz</c> boş →
/// TRY ("TL" → TRY); <c>kur</c> boş → otomatik (TRY=1; döviz sabit kur/TCMB; yoksa red). Şubeye bağlı kullanıcı
/// kendi şubesini <c>sube</c> olarak vermek zorundadır.</summary>
public sealed record ExpenseCreateRequest(
    string? Tip, decimal NetTutar, decimal KdvOrani, string? OdemeYontemi,
    DateTimeOffset? Tarih = null, Guid? AracId = null, Guid? CariId = null, string? Sube = null, string? EvrakNo = null,
    string? Doviz = null, decimal? Kur = null, string? Aciklama = null, Guid? HesapId = null,
    DateTimeOffset? OdemeTarihi = null, string? HazirAciklama = null, Guid? KiraId = null, DateTimeOffset? Vade = null);

/// <summary>Açık hesap giderine ödeme takibi. <c>tutar</c> boş → kalanın tamamı.</summary>
public sealed record ExpensePaymentRequest(
    decimal? Tutar = null, DateTimeOffset? Tarih = null, string? MakbuzNo = null, string? Aciklama = null);
