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

    // ---- FAZ-21 sözleşme meta-alanları (para hesabına GİRMEZ) ----
    public string? SatisTemsilcisi { get; set; }
    public string? FaturaTuru { get; set; }
    public DateTimeOffset? SozlesmeTarihi { get; set; }
    public DateTimeOffset? ImzaTarih { get; set; }
    public string? MakbuzNo { get; set; }
    public string? DosyaNo { get; set; }
    public string? SozlesmeNo { get; set; }
    public int? VadeGun { get; set; }
    public string? FiyatTuru { get; set; }
    public string? Kaynak { get; set; }
    public int? CikisKm { get; set; }
    public int? ToplamKm { get; set; }
}

/// <summary>
/// FAZ-21 — sözleşme KÜNYE güncelleme girişi. Whitelist TİP DÜZEYİNDE: para ve süre alanları
/// (<c>AylikUcret</c>, <c>SureAy</c>, <c>KdvOrani</c>, <c>Kur</c>, <c>DamgaVergisi</c>,
/// <c>BasTar</c>) bu tipte BULUNMAZ — taksit planını sessizce değiştiren bir düzenleme yolu
/// açılmasın diye. (<c>RentalUpdateInput</c> ile aynı desen.) Planı değiştirmek gerekiyorsa
/// sözleşme iptal edilip yenisi açılır.
/// </summary>
public sealed class FiloKiralamaMetaInput
{
    public string? SatisTemsilcisi { get; set; }
    public string? FaturaTuru { get; set; }
    public DateTimeOffset? SozlesmeTarihi { get; set; }
    public DateTimeOffset? ImzaTarih { get; set; }
    public string? MakbuzNo { get; set; }
    public string? DosyaNo { get; set; }
    public string? SozlesmeNo { get; set; }
    public int? VadeGun { get; set; }
    public string? FiyatTuru { get; set; }
    public string? Kaynak { get; set; }
    public int? CikisKm { get; set; }
    public int? ToplamKm { get; set; }
    public int? ToplamKmLimiti { get; set; }
    public string? Aciklama { get; set; }
}

/// <summary>Filo kiralama listesi filtresi (FAZ-21). Boş filtre = tüm kayıtlar (eski davranış).</summary>
public sealed class FiloKiralamaFilter
{
    public Guid? MusteriId { get; set; }
    /// <summary>Plaka (kısmi). Araç tablosundan çözülür; DB'de normalize saklandığı için
    /// arama terimi de normalize edilir.</summary>
    public string? Plaka { get; set; }
    /// <summary>Sözleşme no / makbuz / dosya / açıklama içinde geçen metin.</summary>
    public string? Ara { get; set; }
    public DateTimeOffset? Bas { get; set; }
    public DateTimeOffset? Bit { get; set; }
    public RentACar.Domain.Enums.FleetRentalStatus? Durum { get; set; }
    /// <summary>F5.1 adversarial M3 — aracın şubesine göre kapsam (varsayılan: sınırsız → eski davranış).
    /// <see cref="FleetRentalService.ListScopedAsync"/> çağıranın kapsamıyla doldurur.</summary>
    public RentACar.Application.Authorization.BranchScope.BranchFilter Kapsam { get; set; }
}

/// <summary>Taksit planı tek satırı.</summary>
public sealed record FiloKiraTaksit(int Sira, DateTimeOffset Vade, decimal Net, decimal Kdv, decimal Toplam);

/// <summary>Sözleşme mali özeti + taksit planı (salt-hesap; deftere yansımaz).</summary>
public sealed record FiloKiraOzet(
    decimal ToplamNet, decimal ToplamKdv, decimal Damga, decimal GenelToplam,
    IReadOnlyList<FiloKiraTaksit> Taksitler);
