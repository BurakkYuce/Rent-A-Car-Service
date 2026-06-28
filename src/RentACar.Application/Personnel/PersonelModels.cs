namespace RentACar.Application.Personnel;

/// <summary>Personel yaz modeli (DÜZ METİN PII). Güncellemede TcKimlik/Maas BOŞ ise mevcut korunur.</summary>
public sealed class PersonelInput
{
    public string? Kod { get; set; }
    public string? Ad { get; set; }
    public string? Soyad { get; set; }
    public string? TcKimlik { get; set; }
    public DateTimeOffset? IseGiris { get; set; }
    public DateTimeOffset? IseCikis { get; set; }
    public string? SurucuBelgeNo { get; set; }
    public decimal? Maas { get; set; }
    public string? Sube { get; set; }
    public bool Aktif { get; set; } = true;
}

/// <summary>Personel detay (düzenleme): PII çözülmüş düz metin.</summary>
public sealed record PersonelDetail(
    Guid Id, string Kod, string Ad, string Soyad, string? TcKimlik,
    DateTimeOffset? IseGiris, DateTimeOffset? IseCikis, string? SurucuBelgeNo,
    decimal? Maas, string? Sube, bool Aktif);
