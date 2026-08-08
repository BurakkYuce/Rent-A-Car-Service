namespace RentACar.Application.ReservationSources;

/// <summary>Rezervasyon kaynağı oluştur/güncelle giriş modeli.</summary>
public sealed class ReservationSourceInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public bool Aktif { get; set; } = true;

    /// <summary>Kaynağın arkasındaki tedarikçi/acente adı.</summary>
    public string? Tedarikci { get; set; }

    // FAZ-24 — YÜZDE (12,5 = %12,5). Bu alanlar yalnız SAKLANIR; hiçbir fiyat/komisyon hesabı
    // okumaz. Bir tüketici eklenmeden önce ayrı para incelemesi gerekir.
    public decimal? KiraOrani { get; set; }
    public decimal? HizmetOrani { get; set; }
    public decimal? DropOrani { get; set; }
}
