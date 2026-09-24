using RentACar.Domain.Entities;

namespace RentACar.Web.Api.Sistem;

// F11.1b — tanım ekranları (ikinci yarı) DTO'ları. Kurallar: tekil yanıtta `surum` dolu (listede null);
// PUT gövdesinde `surum` ZORUNLU (tam değiştirme, uyuşmazlık 409 `cakisma`). JSON alan adları Türkçe
// (domain alanları), tip adları İngilizce.

public sealed record InsuranceCompanyDto(Guid Id, string Kod, string Ad, string? Telefon, bool Aktif, string? Surum)
{
    public static InsuranceCompanyDto From(InsuranceCompany x, string? version) => new(x.Id, x.Kod, x.Ad, x.Telefon, x.Aktif, version);
}

public sealed record InsuranceCompanyRequest(string? Kod, string? Ad, string? Telefon, bool Aktif = true, string? Surum = null);

/// <summary>KDV oranı; <c>Oran</c> kesir (0.20 = %20).</summary>
public sealed record KdvRateDto(Guid Id, string Kod, string Ad, decimal Oran, bool Aktif, string? Surum)
{
    public static KdvRateDto From(KdvRate x, string? version) => new(x.Id, x.Kod, x.Ad, x.Oran, x.Aktif, version);
}

public sealed record KdvRateRequest(string? Kod, string? Ad, decimal? Oran, bool Aktif = true, string? Surum = null);

public sealed record PenaltyTypeDto(Guid Id, string Kod, string Ad, decimal? VarsayilanTutar, bool Aktif, string? Surum)
{
    public static PenaltyTypeDto From(PenaltyType x, string? version) => new(x.Id, x.Kod, x.Ad, x.VarsayilanTutar, x.Aktif, version);
}

public sealed record PenaltyTypeRequest(string? Kod, string? Ad, decimal? VarsayilanTutar, bool Aktif = true, string? Surum = null);

/// <summary>Haftalık çalışma saati satırı (1=Pazartesi … 7=Pazar). Saat "HH:mm".</summary>
public sealed record WorkingHoursDto(int Gun, string? Acilis, string? Kapanis, bool Kapali);

public sealed record LocationDto(
    Guid Id, string Kod, string Ad, string? Adres, string? Telefon, string? Eposta, string? CalismaSaatleri,
    decimal? TeslimUcreti, string? Sube, Guid? SubeId, string? IngilizceAd, string? BulusmaNoktasi, string? Iata,
    bool WebdeGizle, string? LokasyonTuru, string? BinaNo, string? Tarif, string? Ulke, string? PostaKodu,
    string? MapsKonumu, string? EkAciklama, int? WebSira, string? DropKarsilamaTuru, string? DropCalismaSekli,
    string? OzelMail, string? OzelTelefon, IReadOnlyList<WorkingHoursDto> HaftalikCalismaSaatleri, bool Aktif,
    string? Surum)
{
    public static LocationDto From(Location x, string? version) => new(
        x.Id, x.Kod, x.Ad, x.Adres, x.Telefon, x.Eposta, x.CalismaSaatleri, x.TeslimUcreti, x.Sube, x.SubeId,
        x.IngilizceAd, x.BulusmaNoktasi, x.Iata, x.WebdeGizle, x.LokasyonTuru, x.BinaNo, x.Tarif, x.Ulke,
        x.PostaKodu, x.MapsKonumu, x.EkAciklama, x.WebSira, x.DropKarsilamaTuru, x.DropCalismaSekli, x.OzelMail,
        x.OzelTelefon, (x.HaftalikCalismaSaatleri ?? []).Select(g => new WorkingHoursDto(g.Gun, g.Acilis, g.Kapanis, g.Kapali)).ToList(),
        x.Aktif, version);
}

public sealed record LocationRequest(
    string? Kod, string? Ad, string? Adres, string? Telefon, string? Eposta, string? CalismaSaatleri,
    decimal? TeslimUcreti, string? Sube, string? IngilizceAd, string? BulusmaNoktasi, string? Iata,
    bool WebdeGizle, string? LokasyonTuru, string? BinaNo, string? Tarif, string? Ulke, string? PostaKodu,
    string? MapsKonumu, string? EkAciklama, int? WebSira, string? DropKarsilamaTuru, string? DropCalismaSekli,
    string? OzelMail, string? OzelTelefon, IReadOnlyList<WorkingHoursDto>? HaftalikCalismaSaatleri,
    bool Aktif = true, string? Surum = null);

/// <summary>Personel liste satırı — PII YOK (TC/maaş taşımaz).</summary>
public sealed record PersonnelListItem(
    Guid Id, string Kod, string Ad, string Soyad, string? Sube, string? GorevTanimi, string? CepTel,
    string? MailAdresi, DateTimeOffset? IseGiris, DateTimeOffset? IseCikis, bool Aktif);

/// <summary>
/// Personel detayı (ManageUsers). TC KİMLİK HİÇBİR YANITTA DÖNMEZ — yalnız <c>TcKimlikTanimli</c> bayrağı; yeni
/// değer yazmak için gövdede <c>tcKimlik</c> gönderilir (boş → mevcut korunur). Maaş yalnız bu uçta (ManageUsers).
/// </summary>
public sealed record PersonnelDto(
    Guid Id, string Kod, string Ad, string Soyad, bool TcKimlikTanimli, DateTimeOffset? IseGiris,
    DateTimeOffset? IseCikis, string? SurucuBelgeNo, decimal? Maas, string? Sube, Guid? SubeId, string? GorevTanimi,
    string? Adres, string? EvTelefonu, string? IsTelefonu, string? CepTel, string? MailAdresi, string? Referans,
    string? Aciklama, string? SSinifi, DateTimeOffset? SVerilisTarihi, string? SVerilisYeri,
    DateTimeOffset? DogumTarihi, string? DogumYeri, string? BabaAdi, string? AnaAdi, string? Il, string? Ilce,
    string? Mahalle, string? CiltNo, string? AileSiraNo, string? SiraNo, string? KanGrubu, string? RacTabletNo,
    bool Aktif, string? Surum);

/// <summary>Personel yazma gövdesi. <c>TcKimlik</c>/<c>Maas</c> boş → mevcut değer korunur.</summary>
public sealed record PersonnelRequest(
    string? Kod, string? Ad, string? Soyad, string? TcKimlik, DateTimeOffset? IseGiris, DateTimeOffset? IseCikis,
    string? SurucuBelgeNo, decimal? Maas, string? Sube, string? GorevTanimi, string? Adres, string? EvTelefonu,
    string? IsTelefonu, string? CepTel, string? MailAdresi, string? Referans, string? Aciklama, string? SSinifi,
    DateTimeOffset? SVerilisTarihi, string? SVerilisYeri, DateTimeOffset? DogumTarihi, string? DogumYeri,
    string? BabaAdi, string? AnaAdi, string? Il, string? Ilce, string? Mahalle, string? CiltNo, string? AileSiraNo,
    string? SiraNo, string? KanGrubu, string? RacTabletNo, bool Aktif = true, string? Surum = null);

public sealed record VehicleGroupDto(
    Guid Id, string Kod, string Ad, string? Aciklama, string? Sipp, string? Segment, string? KasaTuru, string? Marka,
    string? Tipi, int? KoltukSayisi, int? KapiSayisi, int? BagajSayisi, int? KucukBagaj, int? BuyukBagaj,
    int? SurucuMinYas, int? GencSurucuYas, decimal? GencSurucuUcretGunluk, decimal? EkSurucuUcretGunluk,
    int? EhliyetMinYil, int? GencEhliyetMinYil, decimal? Provizyon, decimal? Provizyon2, decimal? MuafiyetTutari,
    decimal? Muafiyet2, int? GunlukKmLimiti, int? AylikMaxKm, decimal? AsimKmUcreti, decimal? YakitFiyati,
    decimal? SonraOdeOran, bool? KrediKartiSart, int? WebSira, int? UpgradeSira, string? ProvizyonDoviz,
    string? Provizyon2Doviz, string? YakitTuru, string? Vites, string? EntegrasyonKod1, string? WebId,
    string? ServisId, bool Aktif, int? AracSayisi, string? Surum)
{
    public static VehicleGroupDto From(VehicleGroup x, int? vehicleCount, string? version) => new(
        x.Id, x.Kod, x.Ad, x.Aciklama, x.Sipp, x.Segment, x.KasaTuru, x.Marka, x.Tipi, x.KoltukSayisi, x.KapiSayisi,
        x.BagajSayisi, x.KucukBagaj, x.BuyukBagaj, x.SurucuMinYas, x.GencSurucuYas, x.GencSurucuUcretGunluk,
        x.EkSurucuUcretGunluk, x.EhliyetMinYil, x.GencEhliyetMinYil, x.Provizyon, x.Provizyon2, x.MuafiyetTutari,
        x.Muafiyet2, x.GunlukKmLimiti, x.AylikMaxKm, x.AsimKmUcreti, x.YakitFiyati, x.SonraOdeOran, x.KrediKartiSart,
        x.WebSira, x.UpgradeSira, x.ProvizyonDoviz, x.Provizyon2Doviz, x.YakitTuru?.ToString(), x.Vites?.ToString(), x.EntegrasyonKod1,
        x.WebId, x.ServisId, x.Aktif, vehicleCount, version);
}

public sealed record VehicleGroupRequest(
    string? Kod, string? Ad, string? Aciklama, string? Sipp, string? Segment, string? KasaTuru, string? Marka,
    string? Tipi, int? KoltukSayisi, int? KapiSayisi, int? BagajSayisi, int? KucukBagaj, int? BuyukBagaj,
    int? SurucuMinYas, int? GencSurucuYas, decimal? GencSurucuUcretGunluk, decimal? EkSurucuUcretGunluk,
    int? EhliyetMinYil, int? GencEhliyetMinYil, decimal? Provizyon, decimal? Provizyon2, decimal? MuafiyetTutari,
    decimal? Muafiyet2, int? GunlukKmLimiti, int? AylikMaxKm, decimal? AsimKmUcreti, decimal? YakitFiyati,
    decimal? SonraOdeOran, bool? KrediKartiSart, int? WebSira, int? UpgradeSira, string? ProvizyonDoviz,
    string? Provizyon2Doviz, string? YakitTuru, string? Vites, string? EntegrasyonKod1, string? WebId,
    string? ServisId, bool Aktif = true, string? Surum = null);

/// <summary>Tanımlı hiçbir aktif gruba eşleşmeyen filo <c>Grup</c> değeri (<c>Bos</c> = grubu girilmemiş araçlar).</summary>
public sealed record UnmatchedGroupValueDto(string Grup, int AracSayisi, bool Bos);

/// <summary>Eşleşmeyen değeri tanımlı gruba taşıma. <c>Bos=true</c> → grubu BOŞ araçlar (kaynak yok sayılır).</summary>
public sealed record GroupAssignRequest(Guid HedefGrupId, string? Kaynak, bool Bos = false);

public sealed record GroupAssignResult(int Tasinan);

public sealed record DocumentTemplateDto(
    Guid Id, string BelgeTuru, string Ad, bool VarsayilanMi, bool Aktif, string? BelgeBasligi,
    string? HukukiMetinSol, string? HukukiMetinSag, string? EkKosullarVarsayilan, string? AltBilgi,
    bool ImzaAlaniGoster, string? Surum)
{
    public static DocumentTemplateDto From(RentACar.Domain.Entities.BelgeSablon x, string? version) => new(
        x.Id, x.BelgeTuru.ToString(), x.Ad, x.VarsayilanMi, x.Aktif, x.BelgeBasligi, x.HukukiMetinSol, x.HukukiMetinSag,
        x.EkKosullarVarsayilan, x.AltBilgi, x.ImzaAlaniGoster, version);
}

public sealed record DocumentTemplateRequest(
    string? BelgeTuru, string? Ad, bool VarsayilanMi, bool Aktif = true, string? BelgeBasligi = null,
    string? HukukiMetinSol = null, string? HukukiMetinSag = null, string? EkKosullarVarsayilan = null,
    string? AltBilgi = null, bool ImzaAlaniGoster = true, string? Surum = null);

/// <summary>"Aşağıya Yansıt" sonucu: oranları güncellenen diğer AKTİF kaynak sayısı.</summary>
public sealed record ReflectRatesResult(int Guncellenen);
