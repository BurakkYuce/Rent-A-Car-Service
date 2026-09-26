using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Penalties;

public sealed class PenaltyInput
{
    public string CezaTuru { get; set; } = string.Empty;
    public DateTimeOffset? TebligTarihi { get; set; }
    /// <summary>Tebliğden itibaren ödeme süresi (gün). Varsayılan 15.</summary>
    public int VadeGun { get; set; } = 15;
    public Guid? VehicleId { get; set; }
    public Guid? CariId { get; set; }
    public Guid? RentalId { get; set; }
    /// <summary>
    /// Tek-satırlı (eski) kullanım için başlık tutarı. <see cref="Satirlar"/> DOLU ise bu alan
    /// YOK SAYILIR — toplam daima <c>Σ Satır.Tutar</c>'dır (çift kaynak yok).
    /// </summary>
    public decimal Tutar { get; set; }
    public string? Sebep { get; set; }

    /// <summary>FAZ-60 — çok satırlı ceza (canlı Ceza_Tutari1-3/Ceza_Sebebi1-3 karşılığı).</summary>
    public List<PenaltySatirInput> Satirlar { get; set; } = [];

    // ---- FAZ-60 bilgi alanları ----
    public string? Saat { get; set; }
    public string? Yer { get; set; }
    public string? CepTel { get; set; }
    public string? MakbuzNo { get; set; }
    public string? IslemSube { get; set; }
}

/// <summary>Ceza kalemi girdisi (tutar + sebep).</summary>
public sealed class PenaltySatirInput
{
    public decimal Tutar { get; set; }
    public string? Sebep { get; set; }
}

/// <summary>
/// FAZ-60 kısmi ödeme girdisi. <see cref="Tutar"/> null ise o KALEMİN kalanının tamamı ödenir.
/// Para birimi alanı BİLİNÇLİ olarak YOKTUR: trafik cezası devlete TRY ödenir; döviz kabul
/// etmek "1000 TL'lik ceza deftere 1000 EUR × kur" sessiz şişmesini açar (FAZ-14'te ampirik
/// olarak bulunmuş ve kapatılmış bir hata sınıfı).
/// </summary>
public sealed class CezaOdemeInput
{
    /// <summary>Ödenecek kalem (KARARLAR.md: satır bazında kalan).</summary>
    public Guid SatirId { get; set; }
    public decimal? Tutar { get; set; }
    public LedgerAccountType Hesap { get; set; } = LedgerAccountType.Kasa;
    public DateTimeOffset? Tarih { get; set; }
    public string? KasaKodu { get; set; }
    public string? HesapNo { get; set; }
    public string? MakbuzNo { get; set; }
    public string? IslemYapan { get; set; }
    public string? Aciklama { get; set; }
    /// <summary>Çift-submit koruması — form her render'da yeni GUID basar.</summary>
    public Guid? IslemAnahtari { get; set; }
}

/// <summary>Kısmi ödeme sonucu — satır ve başlık kalanları AYRI raporlanır (tutarlılık gözle görünür).</summary>
public sealed record CezaOdemeSonuc(
    Guid OdemeId, Guid SatirId, int Sira, decimal Tutar,
    decimal SatirKalan, decimal CezaKalan, PenaltyStatus Durum);

/// <summary>Ceza listesi filtresi (canlı ceza_listesi.aspx paritesi). Boş alanlar süzmez.</summary>
public sealed class PenaltyFilter
{
    /// <summary>Müşteri no / ad-soyad / e-posta içinde arar (case-insensitive).</summary>
    public string? Musteri { get; set; }
    public string? MakbuzNo { get; set; }
    public string? Plaka { get; set; }
    /// <summary>Tebliğ tarihi alt sınırı (gün dahil).</summary>
    public DateTimeOffset? Bas { get; set; }
    /// <summary>Tebliğ tarihi üst sınırı (GÜN DAHİL — gün sonuna kadar).</summary>
    public DateTimeOffset? Bit { get; set; }
    public PenaltyStatus? Durum { get; set; }
    /// <summary>Ödeme durumu süzgeci.</summary>
    public PenaltyPaymentStatus? OdemeDurum { get; set; }
    /// <summary>İşlem şubesi (tam eşleşme, case-insensitive).</summary>
    public string? IslemSube { get; set; }
}

/// <summary>Ödeme durumu süzgeci — tutarlardan TÜRETİLİR (ayrı bir kolon değil).</summary>
public enum PenaltyPaymentStatus
{
    Odenmemis = 0,
    Kismi = 1,
    Odendi = 2
}

/// <summary>
/// Liste satırı — ceza + ilişkili kayıtlardan çözülen bilgi kolonları (canlı ceza_listesi.aspx).
/// Tutar alanları <see cref="Penalty"/> üzerinden gelir; burada TEKRAR hesaplanmaz.
/// </summary>
public sealed record PenaltyRow(
    Penalty Ceza,
    string? Plaka,
    string? MusteriAd,
    string? MusteriEposta,
    string? SozlesmeNo,
    string? RezKaynak,
    string? FaturaNo,
    DateTimeOffset? FaturaTarihi,
    IReadOnlyList<PenaltySatir> Satirlar,
    IReadOnlyList<PenaltyOdeme> Odemeler);
