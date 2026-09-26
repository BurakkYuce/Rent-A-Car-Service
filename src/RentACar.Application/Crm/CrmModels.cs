using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Crm;

public sealed class AnketInput
{
    public Guid? CariId { get; set; }
    public int Puan { get; set; }
    public string? Yorum { get; set; }
    public DateTimeOffset? Tarih { get; set; }
    public string? Kaynak { get; set; }

    // ---- FAZ-42 ----
    public Guid? RentalId { get; set; }
    public RentACar.Domain.Enums.SurveyType? AnketTuru { get; set; }
    public RentACar.Domain.Enums.SurveyStatus Durum { get; set; } = RentACar.Domain.Enums.SurveyStatus.Yapildi;
    /// <summary>Boş bırakılırsa sözleşmeden doldurulur (snapshot).</summary>
    public string? CikisOfisi { get; set; }
    /// <summary>Soru-cevap satırları. Boş liste = cevapsız anket (ör. "Yapılmadı").</summary>
    public List<AnketCevapInput> Cevaplar { get; set; } = [];
}

/// <summary>Anket soru-cevap satırı (FAZ-42). Soru metni cevapla birlikte SAKLANIR (snapshot).</summary>
public sealed class AnketCevapInput
{
    public int SoruNo { get; set; }
    public string? Soru { get; set; }
    public string? Cevap { get; set; }
    public string? Aciklama { get; set; }
}

/// <summary>Anket liste filtresi (FAZ-42). Boş alan = kısıt yok.</summary>
public sealed class AnketFilter
{
    public Guid? CariId { get; set; }
    public RentACar.Domain.Enums.SurveyType? AnketTuru { get; set; }
    public RentACar.Domain.Enums.SurveyStatus? Durum { get; set; }
    public DateTimeOffset? TarihMin { get; set; }
    public DateTimeOffset? TarihMax { get; set; }
    public string? CikisOfisi { get; set; }
}

/// <summary>Anket + cevapları (detay okuması).</summary>
public sealed record AnketDetay(Anket Anket, IReadOnlyList<AnketCevap> Cevaplar);

public sealed class SikayetInput
{
    public Guid? CariId { get; set; }
    public string? Konu { get; set; }
    public string? Detay { get; set; }
    public ComplaintStatus Durum { get; set; } = ComplaintStatus.Acik;
    public DateTimeOffset? Tarih { get; set; }
    public string? Cozum { get; set; }

    // ---- FAZ-43: teslim/dönüş bağı ----
    public Guid? RentalId { get; set; }
    public Guid? TeslimAlanPersonelId { get; set; }
    public Guid? TeslimEdenPersonelId { get; set; }
    public int? Puan { get; set; }
    public string? SikayetKanali { get; set; }
    public ComplaintLocation? SikayetYeri { get; set; }
    public string? CikisOfisi { get; set; }
}

/// <summary>
/// FAZ-43 — şikayet listesi satırı: şikayet + sözleşmeden ÇÖZÜLEN plaka/sözleşme no + müşteri.
/// Plaka snapshot DEĞİL: her istekte sözleşme→araç bağından okunur.
/// </summary>
public sealed record SikayetSatirDto(
    Sikayet Sikayet, string? MusteriAd, string? MusteriTel,
    string? SozlesmeNo, string? Plaka, string? TeslimAlanAd, string? TeslimEdenAd);

/// <summary>FAZ-43 — şikayet listesi filtresi. Boş filtre = tüm kayıtlar (eski davranış).</summary>
public sealed class SikayetFilter
{
    public Guid? CariId { get; set; }
    /// <summary>Çıkış ofisi (tam eşleşme).</summary>
    public string? Ofis { get; set; }
    public ComplaintLocation? Yer { get; set; }
    /// <summary>Şikayet kanalı (tam eşleşme, harf duyarsız).</summary>
    public string? Kanal { get; set; }
    public ComplaintStatus? Durum { get; set; }
    /// <summary>Konu / detay / sözleşme no / plaka içinde geçen metin.</summary>
    public string? Ara { get; set; }
    public int EnFazla { get; set; } = 1000;
}
