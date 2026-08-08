using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Gider belgesi (araç/genel/personel/regülasyon masrafı). Tenant-owned + auditable +
/// DB-DEĞİŞMEZ (mali kayıt). Çift-taraflı defter yazar: Borç Gider(net) + Borç KDV(indirilecek)
/// / Alacak Kasa-Banka(gross) ya da Alacak tedarikçi Cari(gross, AçıkHesap'ta).
/// </summary>
public class Expense : ITenantOwned, IAuditable, IBranchScoped
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

    // Şube-FK marker: Sube metnini SubeId'ye çözer (BranchFkInterceptor).
    string? IBranchScoped.SubeAdi => Sube;
    Guid? IBranchScoped.SubeFk { get => SubeId; set => SubeId = value; }
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

    // ---- FAZ-29 toplu gider derinliği (canlı toplu_gider.aspx) ----

    /// <summary>
    /// Ödeme vadesi (bilgi). <b>Deftere GİRMEZ:</b> gider kaydı zaten <see cref="Tarih"/> ile
    /// postlanır; vadeyi bir tahakkuk/ödeme planına bağlamak ayrı bir modeldir. Açık-hesap
    /// giderlerinde "ne zaman ödenecek" notunu tutar.
    /// </summary>
    public DateTimeOffset? Vade { get; set; }

    /// <summary>
    /// Hangi kasa/banka hesabından ödendiği (<see cref="FinancialAccount"/>). <b>BELGE BİLGİSİDİR:</b>
    /// defter karşı hesabı hâlâ <see cref="KasaBankaHesap"/> (Kasa/Banka) üzerinden yazılır —
    /// hesap-bazlı defter kırılımı ayrı bir model değişikliğidir ve bu fazda AÇILMAMIŞTIR.
    /// Seçilen hesap, giderin hangi IBAN'dan çıktığını belgelemeye yarar.
    /// </summary>
    public Guid? FinansalHesapId { get; set; }

    /// <summary>Toplu işlem idempotency anahtarı (parite #10). Dolu olduğunda tenant içinde benzersiz
    /// (kısmi unique index) → aynı toplu giderin çift-submit'i çakışır, atomik batch geri alınır.</summary>
    public Guid? IslemAnahtari { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
