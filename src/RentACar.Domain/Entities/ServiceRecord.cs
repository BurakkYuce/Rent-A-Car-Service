using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Servis/bakım kaydı. Tenant-owned + auditable. Operasyonel kayıt (mali belge DEĞİL —
/// maliyet bilgilendirme; gerçek gider Gider dilimine bağlanır, follow-up). Servis süresince
/// araç Serviste durumuna geçer, tamamlanınca Musait'e döner. İşçilik kalemleri alt-tabloda.
/// </summary>
public class ServiceRecord : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz no (SRV-000001).</summary>
    public string No { get; set; } = string.Empty;

    public Guid VehicleId { get; set; }
    public ServisTipi Tip { get; set; } = ServisTipi.Periyodik;
    public ServisDurum Durum { get; set; } = ServisDurum.Acik;

    public string? AtolyeAdi { get; set; }
    public DateTimeOffset GirisTarihi { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CikisTarihi { get; set; }
    public int GirisKm { get; set; }
    public int? CikisKm { get; set; }

    /// <summary>Hasarlı serviste sorumluluk; KusurOrani 0..1 (kusur yüzdesi).</summary>
    public HasarSorumlu HasarSorumlu { get; set; } = HasarSorumlu.Yok;
    public decimal? KusurOrani { get; set; }

    /// <summary>Periyodik bakım için sonraki bakım KM hedefi.</summary>
    public int? SonrakiBakimKm { get; set; }

    public string? Aciklama { get; set; }

    /// <summary>İşçilik kalemleri toplamı (Lines'tan türetilir, kalıcılaştırılır).</summary>
    public decimal ToplamIscilik { get; set; }

    public List<ServiceLine> Lines { get; set; } = [];

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}

/// <summary>Servis işçilik/parça kalemi. Tenant-owned (RLS); servis kaydına bağlı.</summary>
public class ServiceLine : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ServiceRecordId { get; set; }

    public string Aciklama { get; set; } = string.Empty;
    public decimal Tutar { get; set; }
}
