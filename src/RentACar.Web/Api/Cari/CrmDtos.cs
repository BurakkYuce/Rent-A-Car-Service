namespace RentACar.Web.Api.Cari;

// ------------------------------------------------------------------ anket

public sealed record SurveyAnswerDto(int SoruNo, string? Soru, string? Cevap, string? Aciklama);

/// <summary>Anket girdisi. Enum ADLA: <c>anketTuru</c> <c>Cikis</c>|<c>Donus</c>|boş; <c>durum</c> <c>Yapildi</c> (varsayılan)|<c>Yapilmadi</c>.
/// Çıkış ofisi boşsa bağlı kiradan kopyalanır. Cevaplar TAMAMEN değiştirilir (sorusu boş satır atlanır).</summary>
public record SurveyRequest
{
    public Guid? CariId { get; init; }
    public Guid? RentalId { get; init; }
    public int Puan { get; init; }
    public string? Yorum { get; init; }
    public DateTimeOffset? Tarih { get; init; }
    public string? Kaynak { get; init; }
    public string? AnketTuru { get; init; }
    public string? Durum { get; init; }
    public string? CikisOfisi { get; init; }
    public IReadOnlyList<SurveyAnswerDto>? Cevaplar { get; init; }
}

public sealed record SurveyUpdateRequest : SurveyRequest
{
    public string? Surum { get; init; }
}

public sealed record SurveyRow(
    Guid Id, DateTimeOffset Tarih, Guid? CariId, string? MusteriAd, Guid? RentalId, string? SozlesmeNo,
    string? AnketTuru, string Durum, int Puan, string? Kaynak, string? CikisOfisi, string? Yorum);

public sealed record SurveyCardDto(SurveyRow Anket, string? Surum, IReadOnlyList<SurveyAnswerDto> Cevaplar);

// ------------------------------------------------------------------ şikayet

/// <summary>Şikayet girdisi. <c>durum</c> <c>Acik</c> (varsayılan)|<c>Cozuldu</c>|<c>Kapali</c>; <c>sikayetYeri</c> <c>Kira</c>|<c>Rezervasyon</c>|boş;
/// <c>puan</c> 1–5 ya da boş.</summary>
public record ComplaintRequest
{
    public Guid? CariId { get; init; }
    public string? Konu { get; init; }
    public string? Detay { get; init; }
    public string? Durum { get; init; }
    public DateTimeOffset? Tarih { get; init; }
    public string? Cozum { get; init; }
    public Guid? RentalId { get; init; }
    public Guid? TeslimAlanPersonelId { get; init; }
    public Guid? TeslimEdenPersonelId { get; init; }
    public int? Puan { get; init; }
    public string? SikayetKanali { get; init; }
    public string? SikayetYeri { get; init; }
    public string? CikisOfisi { get; init; }
}

public sealed record ComplaintUpdateRequest : ComplaintRequest
{
    public string? Surum { get; init; }
}

public sealed record ComplaintRow(
    Guid Id, DateTimeOffset Tarih, string Konu, string? Detay, string Durum, string? Cozum, Guid? CariId,
    string? MusteriAd, string? MusteriTel, Guid? RentalId, string? SozlesmeNo, string? Plaka,
    Guid? TeslimAlanPersonelId, string? TeslimAlanAd, Guid? TeslimEdenPersonelId, string? TeslimEdenAd,
    int? Puan, string? SikayetKanali, string? SikayetYeri, string? CikisOfisi);

public sealed record ComplaintCardDto(ComplaintRow Sikayet, string? Surum);

// ------------------------------------------------------------------ assistans

/// <summary>Assistans (yol yardım) talebi. Kira seçiliyse boş plaka/ad/telefon kiradan doldurulur (kullanıcı değeri öncelikli).</summary>
public record AssistanceRequest
{
    public Guid? RentalId { get; init; }
    public string? Plaka { get; init; }
    public string? AdSoyad { get; init; }
    public string? CepTel { get; init; }
    public DateTimeOffset? Zaman { get; init; }
    public string? Mesaj { get; init; }
    public string? Sebep { get; init; }
    public bool YedekLastikMi { get; init; }
    public bool AracHareketMi { get; init; }
    public bool Kapandi { get; init; }
    public string? Cozum { get; init; }
}

public sealed record AssistanceUpdateRequest : AssistanceRequest
{
    public string? Surum { get; init; }
}

/// <summary>Talep satırı. Ad/telefon bağlı kiranın müşterisi KVKK ile anonimleştirilmişse <c>null</c>.</summary>
public sealed record AssistanceRow(
    Guid Id, DateTimeOffset Zaman, Guid? RentalId, string? SozlesmeNo, string? Plaka, string? AdSoyad, string? CepTel,
    string Mesaj, string? Sebep, bool YedekLastikMi, bool AracHareketMi, bool Kapandi, string? Cozum);

public sealed record AssistanceCardDto(AssistanceRow Talep, string? Surum);

// ------------------------------------------------------------------ hukuk

/// <summary>Hukuk dosyası. <c>tur</c> <c>Dava</c>|<c>Icra</c>|<c>Diger</c>; <c>durum</c> <c>Acik</c>|<c>Beklemede</c>|<c>Kapali</c>.
/// Tutar ve tahsilat BİLGİ alanıdır — deftere/cari bakiyeye yazmaz.</summary>
public record LegalFileRequest
{
    public string? DosyaNo { get; init; }
    public Guid? CariId { get; init; }
    public string? Tur { get; init; }
    public string? Avukat { get; init; }
    public decimal Tutar { get; init; }
    public string? Durum { get; init; }
    public DateTimeOffset? Tarih { get; init; }
    public string? Aciklama { get; init; }
    public bool Aktif { get; init; } = true;
    public string? FaturaNoTemp { get; init; }
    public string? AvukatTel { get; init; }
    public string? AvukatMail { get; init; }
    public string? Avukat2Ad { get; init; }
    public string? Avukat2Tel { get; init; }
    public string? Avukat2Mail { get; init; }
    public decimal? Tahsilat { get; init; }
}

public sealed record LegalFileUpdateRequest : LegalFileRequest
{
    public string? Surum { get; init; }
}

public sealed record LegalFileRow(
    Guid Id, string DosyaNo, DateTimeOffset Tarih, string Tur, string Durum, bool Aktif, Guid? CariId, string? MusteriAd,
    string? MusteriTel, string? Avukat, string? AvukatTel, string? AvukatMail, string? Avukat2Ad, string? Avukat2Tel,
    string? Avukat2Mail, decimal Tutar, decimal? Tahsilat, decimal Kalan, string? FaturaNoTemp, string? Aciklama);

public sealed record LegalFileCardDto(LegalFileRow Dosya, string? Surum);

// ------------------------------------------------------------------ CRM analiz + seçim

public sealed record CrmSegmentRow(
    Guid CariId, string Ad, string? Mail, string? Tel, int KiraSayisi, decimal ToplamCiro, decimal OrtalamaKiraBedeli,
    decimal? OrtalamaKm, decimal HizmetBedeli, DateTimeOffset? DogumTarihi, DateTimeOffset? IlkKiraZamani,
    DateTimeOffset? SonIslem, string Segment);

public sealed record CrmStaffRow(Guid PersonelId, string Ad, int TahsisSayisi);

/// <summary>CRM analiz: özet (tüm süzülmüş küme) + sayfalı segment tablosu + personel BAF tahsisi.</summary>
public sealed record CrmAnalysisDto(
    int MusteriSayisi, decimal ToplamCiro, decimal ToplamHizmetBedeli, Application.Common.Sayfa<CrmSegmentRow> Segment,
    IReadOnlyList<CrmStaffRow> Personel);

public sealed record CrmFilterOptions(IReadOnlyList<string> Kaynaklar, IReadOnlyList<string> Ofisler);

/// <summary>Kira sözleşmesi seçim öğesi (anket/şikayet/assistans formları). TC/telefon YOK; ad KVKK kuralıyla.</summary>
public sealed record RentalPickItem(
    Guid Id, string SozlesmeNo, string? Plaka, string MusteriAd, Guid MusteriId, string? CikisOfisi, DateTimeOffset BasTar);
