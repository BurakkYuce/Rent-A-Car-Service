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
}

public sealed class SikayetInput
{
    public Guid? CariId { get; set; }
    public string? Konu { get; set; }
    public string? Detay { get; set; }
    public SikayetDurum Durum { get; set; } = SikayetDurum.Acik;
    public DateTimeOffset? Tarih { get; set; }
    public string? Cozum { get; set; }

    // ---- FAZ-43: teslim/dönüş bağı ----
    public Guid? RentalId { get; set; }
    public Guid? TeslimAlanPersonelId { get; set; }
    public Guid? TeslimEdenPersonelId { get; set; }
    public int? Puan { get; set; }
    public string? SikayetKanali { get; set; }
    public SikayetYeri? SikayetYeri { get; set; }
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
    public SikayetYeri? Yer { get; set; }
    /// <summary>Şikayet kanalı (tam eşleşme, harf duyarsız).</summary>
    public string? Kanal { get; set; }
    public SikayetDurum? Durum { get; set; }
    /// <summary>Konu / detay / sözleşme no / plaka içinde geçen metin.</summary>
    public string? Ara { get; set; }
    public int EnFazla { get; set; } = 1000;
}
