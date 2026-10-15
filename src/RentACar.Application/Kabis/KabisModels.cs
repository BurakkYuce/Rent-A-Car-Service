using RentACar.Domain.Enums;

namespace RentACar.Application.Kabis;

/// <summary>Tenant'ın KABİS kapısı: otomatik bildirim açık mı, teslimi engelliyor mu, hangi kurum.</summary>
public readonly record struct KabisGate(bool Etkin, bool TeslimiEngelle, KabisAuthority Kurum)
{
    public static readonly KabisGate Kapali = new(false, false, KabisAuthority.Emniyet);
}

/// <summary>
/// Kira işleminin AYNI transaction'ında yazılacak kuyruk kaydı tarifi (outbox). Teslim/dönüş commit olduysa
/// kuyruk kaydı da vardır — ikisinin arasında çökme "teslim edildi ama kuyrukta yok" bırakamaz.
/// </summary>
/// <param name="RequiresReportedRental">true (İade): kirada "gerekli değil" olmayan bir Kiralama kaydı yoksa
/// (ör. KABİS açılmadan önce teslim edilmiş kira) İade kaydı yazılmaz — karşı sistemin tanımadığı kiralamanın
/// iadesi bildirilmez.</param>
public sealed record KabisOutbox(
    KabisNoticeType Tur, KabisAuthority Kurum, DateTimeOffset OlayUtc, bool RequiresReportedRental = false);

/// <summary>Bildirim yükünün kurulduğu kaynak (repository projeksiyonu). Kimlik alanları CIPHER'dır.</summary>
public sealed record KabisRentalSource(
    Guid KiraId, string SozlesmeNo, RentalStatus Durum, Guid? CikisSubeId, string? CikisOfisi, string? DonusOfisi,
    DateTimeOffset BasTar, DateTimeOffset BitTar, DateTimeOffset? GercekDonusTar, int? CikisKm, int? DonusKm,
    string Plaka, string? AracMarkaModel,
    KabisPartySource Kiralayan, KabisPartySource? IkinciSurucu, string? MisafirSurucuAd, string? MisafirSurucuSoyad);

/// <summary>Cari projeksiyonu — <c>*Enc</c> alanları şifreli, <c>*Eski</c> alanları backfill öncesi düz kolon.</summary>
public sealed record KabisPartySource(
    CustomerType Tip, string? Ad, string? Soyad, string? Unvan, string? VergiNo,
    string? TcKimlikEnc, string? TcKimlikEski, string? PasaportNoEnc, string? PasaportNoEski,
    string? EhliyetNoEnc, string? EhliyetNoEski, string? EhliyetSinifi, DateTimeOffset? EhliyetTarihi,
    DateTimeOffset? DogumTarihi, string? Ulke, string? CepTel, string? Adres, string? Il, string? Ilce);

/// <summary>Kira ekranındaki bildirim satırı (PII yok).</summary>
public sealed record KabisNoticeView(
    KabisNoticeType Tur, KabisNoticeStatus Durum, KabisAuthority Kurum, DateTimeOffset OlayUtc, int DenemeSayisi,
    string? SonHata, string? DisReferans, bool Elle, string? Gerekce, DateTimeOffset? SonDenemeUtc,
    DateTimeOffset? SonrakiDenemeUtc, DateTimeOffset? BildirimUtc);

/// <summary>Kira ekranı KABİS bloğu: ayar kapısı + kayıtlar + tek cümlelik uyarı (yoksa null).</summary>
public sealed record KabisRentalStatus(
    bool Etkin, bool TeslimiEngelle, KabisAuthority Kurum, IReadOnlyList<KabisNoticeView> Bildirimler, string? Uyari);

/// <summary>"Bildirilemeyen kiralar" listesinin satırı (şube kapsamlı; PII yok).</summary>
public sealed record KabisProblemRow(
    Guid KiraId, string SozlesmeNo, string? Plaka, KabisNoticeType Tur, KabisNoticeStatus Durum, int DenemeSayisi,
    string? SonHata, DateTimeOffset OlayUtc, DateTimeOffset? SonDenemeUtc);

/// <summary>KABİS ayar görünümü — şifre DÖNMEZ, yalnız tanımlı mı.</summary>
public sealed record KabisSettingsView(
    bool Etkin, KabisAuthority Kurum, string? Kullanici, bool SifreTanimli, bool TeslimiEngelle);

/// <summary>KABİS ayar yazımı. <see cref="Sifre"/> boşsa mevcut korunur; <see cref="SifreTemizle"/> siler.</summary>
public sealed class KabisSettingsInput
{
    public bool Etkin { get; set; }
    public KabisAuthority Kurum { get; set; } = KabisAuthority.Emniyet;
    public string? Kullanici { get; set; }
    public string? Sifre { get; set; }
    public bool SifreTemizle { get; set; }
    public bool TeslimiEngelle { get; set; }

    /// <summary>Şifre kayıt yazdırımlarında görünmez.</summary>
    public override string ToString() => $"KabisSettingsInput {{ Etkin = {Etkin}, Kurum = {Kurum}, Kullanici = {Kullanici}, Sifre = *** }}";
}

/// <summary>Bağlantı sınaması sonucu (dürüst: yapılandırma yoksa Ok=false).</summary>
public sealed record KabisConnectionTest(bool Ok, string Mesaj);
