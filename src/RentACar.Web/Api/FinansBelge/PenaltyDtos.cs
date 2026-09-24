namespace RentACar.Web.Api.FinansBelge;

/// <summary>Ceza listesi satırı. Tutarlar TRY (trafik cezası). <c>odemeDurumu</c>: Odenmemis | Kismi | Odendi
/// (tutarlardan türetilir). Müşteri adı KVKK tek kuralıyla; e-posta ve ihbarname telefonu listede DÖNMEZ.</summary>
public sealed record PenaltyListRow(
    Guid Id, string No, string CezaTuru, DateTimeOffset TebligTarihi, DateTimeOffset VadeTarihi, string Durum,
    decimal Tutar, decimal OdenenTutar, decimal Kalan, string OdemeDurumu, string? Sebep,
    Guid? AracId, string? Plaka, Guid? CariId, string? CariAd, Guid? KiraId, string? SozlesmeNo,
    string? FaturaNo, string? MakbuzNo, string? IslemSube, string? Yer, string? Saat, DateTimeOffset? OdenmeTarihi);

/// <summary>Ceza kalemi (satır bazında kalan — ödeme daima tek kaleme yazılır).</summary>
public sealed record PenaltyLineDto(Guid Id, int Sira, decimal Tutar, decimal Odenen, decimal Kalan, string? Sebep);

/// <summary>Ceza kalem ödemesi (defterde Borç Gider / Alacak Kasa-Banka).</summary>
public sealed record PenaltyPaymentDto(
    Guid Id, Guid SatirId, int Sira, decimal Tutar, DateTimeOffset Tarih, string Hesap, decimal KalanSonrasi,
    string? MakbuzNo, string? KasaKodu, string? HesapNo, string? IslemYapan, string? Aciklama);

public sealed record PenaltyDetail(
    PenaltyListRow Ceza, string? CepTel, IReadOnlyList<PenaltyLineDto> Kalemler, IReadOnlyList<PenaltyPaymentDto> Odemeler);

/// <summary>Ceza kalemi girdisi.</summary>
public sealed record PenaltyLineRequest(decimal Tutar, string? Sebep = null);

/// <summary>Yeni ceza. <c>kalemler</c> 1–20; toplam = Σ kalem. Şubeye bağlı kullanıcı kira ya da araç vermek
/// zorundadır (cezanın kapsamı onun şubesidir). <c>vadeGun</c> boş → 15.</summary>
public sealed record PenaltyCreateRequest(
    string? CezaTuru, IReadOnlyList<PenaltyLineRequest>? Kalemler, DateTimeOffset? TebligTarihi = null,
    int? VadeGun = null, Guid? AracId = null, Guid? CariId = null, Guid? KiraId = null, string? Sebep = null,
    string? Saat = null, string? Yer = null, string? CepTel = null, string? MakbuzNo = null, string? IslemSube = null);

/// <summary>Kalem ödemesi. <c>tutar</c> boş → kalemin kalanının tamamı. <c>hesap</c>: "Kasa" | "Banka" (zorunlu).
/// Döviz yok (devlete TRY ödenir).</summary>
public sealed record PenaltyPaymentRequest(
    Guid SatirId, string? Hesap, decimal? Tutar = null, DateTimeOffset? Tarih = null, string? MakbuzNo = null,
    string? KasaKodu = null, string? HesapNo = null, string? IslemYapan = null, string? Aciklama = null);

/// <summary>Ödeme sonucu: kalem ve ceza kalanları AYRI (tutarlılık gözle görünür).</summary>
public sealed record PenaltyPaymentResult(
    Guid OdemeId, Guid SatirId, int Sira, decimal Tutar, decimal SatirKalan, decimal CezaKalan, string Durum);

public sealed record PenaltyStateResult(Guid Id, string Durum);
