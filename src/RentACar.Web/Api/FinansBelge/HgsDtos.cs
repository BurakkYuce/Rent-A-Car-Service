namespace RentACar.Web.Api.FinansBelge;

/// <summary>Sütun eşleme alanı: <paramref name="Baslik"/> dosyada bu alana eşlenen (önerilen ya da seçilen) sütun.</summary>
public sealed record HgsFieldDto(string Kod, string Etiket, bool Zorunlu, string? Baslik);

/// <summary>Önizleme/içe aktarma satırı. <paramref name="Durum"/>: Eslesti | KiraDisi | Belirsiz | Mukerrer | Hatali.</summary>
public sealed record HgsImportRowDto(
    int SatirNo, string? Plaka, DateTimeOffset? GecisZamani, string? GirisIstasyonu, string? CikisIstasyonu,
    decimal? Tutar, string Durum, Guid? KiraId, string? SozlesmeNo, bool AracBulunamadi, string? Mesaj);

/// <summary>
/// Önizleme (<c>Eklenen = 0</c>, hiçbir şey yazılmaz) ya da içe aktarma sonucu. Sayılar dosyanın TAMAMI içindir;
/// <see cref="Satirlar"/> en çok 500 satırdır (hatalılar önce). <paramref name="OlasiMukerrer"/>
/// <paramref name="Belirsiz"/>'in alt kümesidir (aynı plaka + tutar + 60 sn içinde başka geçiş ya da başka dökümde
/// aynı istasyon + kirayla 6 sa içinde eşi).
/// </summary>
public sealed record HgsImportResultDto(
    IReadOnlyList<string> Basliklar, IReadOnlyList<HgsFieldDto> Alanlar,
    int Toplam, int Eslesen, int KiraDisi, int Belirsiz, int OlasiMukerrer, int AracBulunamadi, int Mukerrer, int Hatali,
    int Eklenen, decimal EslesenTutar, decimal KiraDisiTutar, IReadOnlyList<string> Uyarilar,
    IReadOnlyList<HgsImportRowDto> Satirlar, Guid? IceAktarimId);

/// <summary>HGS geçiş listesi satırı. <paramref name="CariId"/>: yansıtılmışsa borçlanan cari, değilse kiranın müşterisi.
/// <paramref name="EslesmeNotu"/>: Belirsiz/KiraDisi gerekçesi (olası mükerrer, birden çok kira, teslim edilmemiş…).
/// <paramref name="OnayZamani"/>: olası mükerrer elle "ayrı geçiş" onaylandıysa onay anı.</summary>
public sealed record HgsCrossingRow(
    Guid Id, string Plaka, Guid? VehicleId, DateTimeOffset GecisZamani, DateTimeOffset? GirisZamani,
    string? GirisIstasyonu, string? CikisIstasyonu, decimal Tutar, string Durum, Guid? KiraId, string? SozlesmeNo,
    Guid? CariId, string? CariAd, decimal? HizmetOrani, decimal? YansitilanTutar, DateTimeOffset? YansitmaZamani,
    string? EtiketNo, string? AracSinifi, string? DosyaAdi, string? EslesmeNotu, DateTimeOffset? OnayZamani);

/// <summary>Seçili olası mükerrer geçişleri "ayrı geçiş" olarak onayla (para yazmaz).</summary>
public sealed record HgsConfirmRequest(IReadOnlyList<Guid>? Ids);

/// <summary>Onay sonucu. <paramref name="Uygunsuz"/>: olası mükerrer olmayan / kirası artık uymayan geçiş.</summary>
public sealed record HgsConfirmResultDto(int Onaylanan, int Uygunsuz);

/// <summary>Seçili geçişleri yansıt. <paramref name="HizmetOrani"/> çarpan: 1,00 = bedelsiz, 1,03 = %3 hizmet bedeli.</summary>
public sealed record HgsReflectRequest(IReadOnlyList<Guid>? Ids, decimal? HizmetOrani);

/// <summary>Yansıtma sonucu. <paramref name="Uygunsuz"/>: kira dışı / belirsiz / kirası artık uymayan geçiş.</summary>
public sealed record HgsReflectResultDto(
    int Yansitilan, int ZatenYansitilmis, int Uygunsuz, decimal ToplamGecis, decimal YansitilanTutar);

public sealed record HgsRematchResult(int Degisen);
