using RentACar.Domain.Enums;

namespace RentACar.Application.Bookings;

/// <summary>Kira listesi filtreleri. Sube, servis tarafından rol bazlı şube kapsamına ayarlanır.</summary>
public sealed class RentalFilter
{
    /// <summary>Sözleşme no / müşteri adı / plaka içeren arama (case-insensitive).</summary>
    public string? Query { get; set; }
    public RentalStatus? Durum { get; set; }
    /// <summary>true → faturalı; false → faturasız; null → tümü.</summary>
    public bool? Faturali { get; set; }
    public DateTimeOffset? BaslangicMin { get; set; }
    public DateTimeOffset? BaslangicMax { get; set; }
    /// <summary>Çıkış veya dönüş ofisi eşleşmesi.</summary>
    public string? Ofis { get; set; }
    public string? Sube { get; set; }         // UI ofis filtresi (kullanıcı seçimi)
    /// <summary>Rol bazlı şube KAPSAMI (C4; servis ayarlar) — türetilmiş CikisSubeId + ofis metni.</summary>
    public Authorization.BranchScope.BranchFilter Kapsam { get; set; }

    // ---- FAZ-46: canlı kira_listesi.aspx süzgeçleri ----
    /// <summary><see cref="BaslangicMin"/>/<see cref="BaslangicMax"/> aralığının HANGİ tarihe
    /// uygulanacağı. null → <see cref="Domain.Enums.TarihListesiTuru.Baslangic"/> (eski davranış).</summary>
    public TarihListesiTuru? TarihTuru { get; set; }
    /// <summary><see cref="Ofis"/> hangi ofise uygulansın. null → eski davranış (çıkış VEYA dönüş).</summary>
    public OfisDurumu? OfisDurum { get; set; }
    /// <summary>Araç sahibi (Vehicle.AracSahibi) — araç üzerinden süzer.</summary>
    public string? SahipGrup { get; set; }
    /// <summary>Araç grubu (Vehicle.Grup) — araç üzerinden süzer.</summary>
    public string? AracGrubu { get; set; }
    /// <summary>Rezervasyon kaynağı — sözleşmenin KENDİ Kaynak alanı (rezervasyondan taşınmış).</summary>
    public string? RezKaynak { get; set; }
    /// <summary>Dönüşü teslim alan personel (RentalContract.TeslimAlanPersonelId).</summary>
    public Guid? PersonelId { get; set; }
}
