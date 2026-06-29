namespace RentACar.Application.FiloKiralamalar;

/// <summary>Filo kiralama oluşturma girişi (roadmap L1). KdvOrani kesir (0.20 = %20).</summary>
public sealed class FiloKiralamaInput
{
    public Guid MusteriId { get; set; }
    public Guid VehicleId { get; set; }
    public DateTimeOffset? BasTar { get; set; }
    public int SureAy { get; set; }
    public decimal AylikUcret { get; set; }
    public decimal KdvOrani { get; set; } = 0.20m;
    public string Doviz { get; set; } = "TRY";
    public decimal Kur { get; set; } = 1m;
    public int? ToplamKmLimiti { get; set; }
    public decimal? DamgaVergisi { get; set; }
    public string? Aciklama { get; set; }
}

/// <summary>Taksit planı tek satırı.</summary>
public sealed record FiloKiraTaksit(int Sira, DateTimeOffset Vade, decimal Net, decimal Kdv, decimal Toplam);

/// <summary>Sözleşme mali özeti + taksit planı (salt-hesap; deftere yansımaz).</summary>
public sealed record FiloKiraOzet(
    decimal ToplamNet, decimal ToplamKdv, decimal Damga, decimal GenelToplam,
    IReadOnlyList<FiloKiraTaksit> Taksitler);
