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

    // ---- FAZ-40 derinlik ----
    public string? GorevTanimi { get; set; }
    public string? Adres { get; set; }
    public string? EvTelefonu { get; set; }
    public string? IsTelefonu { get; set; }
    public string? CepTel { get; set; }
    public string? MailAdresi { get; set; }
    public string? Referans { get; set; }
    public string? Aciklama { get; set; }
    public string? SSinifi { get; set; }
    public DateTimeOffset? SVerilisTarihi { get; set; }
    public string? SVerilisYeri { get; set; }
    public DateTimeOffset? DogumTarihi { get; set; }
    public string? DogumYeri { get; set; }
    public string? BabaAdi { get; set; }
    public string? AnaAdi { get; set; }
    public string? Il { get; set; }
    public string? Ilce { get; set; }
    public string? Mahalle { get; set; }
    public string? CiltNo { get; set; }
    public string? AileSiraNo { get; set; }
    public string? SiraNo { get; set; }
    public string? KanGrubu { get; set; }
    public string? RacTabletNo { get; set; }

    public bool Aktif { get; set; } = true;
}

/// <summary>
/// Personel detay (düzenleme): PII çözülmüş düz metin. FAZ-40 derinlik alanları PII DEĞİLDİR ve
/// entity'den olduğu gibi taşınır — ham entity referansı da veriliyor ki form 23 yeni alanı tek
/// tek parametreye çevirmek zorunda kalmasın (kayıt yolu yine servis üzerinden).
/// </summary>
public sealed record PersonelDetail(
    Guid Id, string Kod, string Ad, string Soyad, string? TcKimlik,
    DateTimeOffset? IseGiris, DateTimeOffset? IseCikis, string? SurucuBelgeNo,
    decimal? Maas, string? Sube, bool Aktif,
    RentACar.Domain.Entities.Personel? Ham = null);

/// <summary>Personel seçim satırı (dropdown projeksiyonu) — PII İÇERMEZ (operasyon ekranları için).</summary>
public sealed record PersonelSecim(Guid Id, string Ad, string Soyad, string? Sube);

/// <summary>Personel liste filtresi (FAZ-40). Boş alan = kısıt yok.</summary>
public sealed class PersonelFilter
{
    /// <summary>Sicil / ad / soyad / cep tel / mail içinde geçen metin.</summary>
    public string? Ara { get; set; }
    public string? Sube { get; set; }
    public string? GorevTanimi { get; set; }
    /// <summary>null = hepsi; true/false = yalnız aktif/pasif.</summary>
    public bool? Aktif { get; set; }
}
