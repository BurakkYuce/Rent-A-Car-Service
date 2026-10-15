namespace RentACar.Web.Api.FinansBelge;

/// <summary>Sütun eşleme alanı: <paramref name="Baslik"/> dosyada bu alana eşlenen (önerilen ya da seçilen) sütun.</summary>
public sealed record HgsFieldDto(string Kod, string Etiket, bool Zorunlu, string? Baslik);

/// <summary>Önizleme/içe aktarma satırı. <paramref name="Durum"/>: Eslesti | KiraDisi | Belirsiz | Mukerrer | Hatali.</summary>
public sealed record HgsImportRowDto(
    int SatirNo, string? Plaka, DateTimeOffset? GecisZamani, string? GirisIstasyonu, string? CikisIstasyonu,
    decimal? Tutar, string Durum, Guid? KiraId, string? SozlesmeNo, bool AracBulunamadi, string? Mesaj);

/// <summary>
/// Önizleme (<c>Eklenen = 0</c>, hiçbir şey yazılmaz) ya da içe aktarma sonucu. Sayılar dosyanın TAMAMI içindir;
/// <see cref="Satirlar"/> en çok 500 satırdır (hatalılar önce).
/// </summary>
public sealed record HgsImportResultDto(
    IReadOnlyList<string> Basliklar, IReadOnlyList<HgsFieldDto> Alanlar,
    int Toplam, int Eslesen, int KiraDisi, int Belirsiz, int AracBulunamadi, int Mukerrer, int Hatali, int Eklenen,
    decimal EslesenTutar, decimal KiraDisiTutar, IReadOnlyList<string> Uyarilar, IReadOnlyList<HgsImportRowDto> Satirlar,
    Guid? IceAktarimId);

/// <summary>HGS geçiş listesi satırı. <paramref name="CariId"/>: yansıtılmışsa borçlanan cari, değilse kiranın müşterisi.</summary>
public sealed record HgsCrossingRow(
    Guid Id, string Plaka, Guid? VehicleId, DateTimeOffset GecisZamani, DateTimeOffset? GirisZamani,
    string? GirisIstasyonu, string? CikisIstasyonu, decimal Tutar, string Durum, Guid? KiraId, string? SozlesmeNo,
    Guid? CariId, string? CariAd, decimal? HizmetOrani, decimal? YansitilanTutar, DateTimeOffset? YansitmaZamani,
    string? EtiketNo, string? AracSinifi, string? DosyaAdi);

/// <summary>Seçili geçişleri yansıt. <paramref name="HizmetOrani"/> çarpan: 1,00 = bedelsiz, 1,03 = %3 hizmet bedeli.</summary>
public sealed record HgsReflectRequest(IReadOnlyList<Guid>? Ids, decimal? HizmetOrani);

/// <summary>Yansıtma sonucu. <paramref name="Uygunsuz"/>: kira dışı / belirsiz / kirası artık uymayan geçiş.</summary>
public sealed record HgsReflectResultDto(
    int Yansitilan, int ZatenYansitilmis, int Uygunsuz, decimal ToplamGecis, decimal YansitilanTutar);

public sealed record HgsRematchResult(int Degisen);
