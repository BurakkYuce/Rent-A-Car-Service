namespace RentACar.Application.AracKredileri;

/// <summary>Araç kredisi oluşturma girişi (roadmap L4). FaizOran kesir (0.20 = %20 yıllık basit).</summary>
public sealed class AracKrediInput
{
    public string? BankaAdi { get; set; }
    public Guid? VehicleId { get; set; }
    public decimal KrediTutari { get; set; }
    public decimal FaizOran { get; set; }
    public int TaksitSayisi { get; set; }
    public DateTimeOffset? BaslangicTarihi { get; set; }
    public string Doviz { get; set; } = "TRY";
    public decimal Kur { get; set; } = 1m;
    public string? Aciklama { get; set; }
}

/// <summary>Kredi taksit planı satırı.</summary>
public sealed record AracKrediTaksit(int Sira, DateTimeOffset Vade, decimal Tutar, bool Odendi);

/// <summary>Kredi mali özeti + taksit planı (salt-hesap).</summary>
public sealed record AracKrediOzet(
    decimal ToplamFaiz, decimal ToplamGeriOdeme, decimal AylikTaksit,
    decimal OdenenTutar, decimal KalanBakiye, IReadOnlyList<AracKrediTaksit> Taksitler);
