using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Tenant "kur sabitleme": firma günlük TCMB kurunu KENDİ sabit kuruyla ezer (ör. "biz EUR'yu hep 40 TL
/// sayarız" veya bir dönem için kilitle). Tenant-owned + RLS (her firmanın sabit kuru gizli/özel).
/// <c>KurService</c> çözümlemede ÖNCE buna bakar, aktif+geçerli yoksa TCMB'ye düşer. Geçerlilik penceresi
/// (BasTar/BitTar) null ise süresiz. Kur = 1 birim dövizin TL değeri.
/// </summary>
public class SabitKur : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>ISO kod (USD/EUR…). Tenant içinde benzersiz (servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;

    /// <summary>Sabit kur — 1 birim dövizin TL karşılığı.</summary>
    public decimal Kur { get; set; }

    /// <summary>Geçerlilik penceresi (null = süresiz).</summary>
    public DateTimeOffset? BasTar { get; set; }
    public DateTimeOffset? BitTar { get; set; }

    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
