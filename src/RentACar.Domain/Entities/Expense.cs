using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Gider belgesi (araç/genel/personel/regülasyon masrafı). Tenant-owned + auditable +
/// DB-DEĞİŞMEZ (mali kayıt). Çift-taraflı defter yazar: Borç Gider(net) + Borç KDV(indirilecek)
/// / Alacak Kasa-Banka(gross) ya da Alacak tedarikçi Cari(gross, AçıkHesap'ta).
/// </summary>
public class Expense : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz no (GD-000001).</summary>
    public string No { get; set; } = string.Empty;

    public ExpenseType Tip { get; set; } = ExpenseType.Genel;
    public DateTimeOffset Tarih { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Araç gideriyse ilgili araç.</summary>
    public Guid? VehicleId { get; set; }
    /// <summary>Tedarikçi cari (AçıkHesap ödemede zorunlu).</summary>
    public Guid? CariId { get; set; }

    public string? Sube { get; set; }
    public Guid? SubeId { get; set; } // Branch FK (roadmap F1; metin korunur)
    public string? EvrakNo { get; set; }

    public decimal NetTutar { get; set; }
    public decimal KdvOrani { get; set; }   // ör. 0.20
    public decimal KdvTutar { get; set; }
    public decimal GenelToplam { get; set; }
    public string Currency { get; set; } = "TRY";
    public decimal Kur { get; set; } = 1m;

    public OdemeYontemi OdemeYontemi { get; set; } = OdemeYontemi.Nakit;
    /// <summary>Nakit/Banka ödemede karşı hesap (Kasa/Banka).</summary>
    public LedgerAccountType KasaBankaHesap { get; set; } = LedgerAccountType.Kasa;

    public string? Aciklama { get; set; }

    /// <summary>Toplu işlem idempotency anahtarı (parite #10). Dolu olduğunda tenant içinde benzersiz
    /// (kısmi unique index) → aynı toplu giderin çift-submit'i çakışır, atomik batch geri alınır.</summary>
    public Guid? IslemAnahtari { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
