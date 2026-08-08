using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Filo plan hedefi (FAZ-19; canlı <c>arac_plan_yonetim.aspx</c>). Bir araç grubu ve/veya SIPP
/// kodu için HEDEF filo adedi. Kapasite planlama ekranıdır — gerçek satın alma siparişi
/// (<see cref="AracSiparis"/>) AYRI bir iştir ve bu kayıt ona dönüşmez.
///
/// <para><b>Hedef boyutu:</b> <see cref="AracGrupAdi"/> ve/veya <see cref="Sipp"/>. İkisi de
/// doluysa kural DAHA DARDIR (grup VE sipp birlikte eşleşmeli); en az biri zorunludur — ikisi de
/// boş bir hedef "tüm filo" anlamına gelirdi ve sessizce her aracı sayardı.</para>
///
/// <para><see cref="Donem"/> opsiyoneldir (boş = açık uçlu plan) ve doğal anahtarın PARÇASIDIR:
/// aynı grup için 2026-Q1 ve 2026-Q2 hedefleri birlikte yaşayabilsin.</para>
///
/// <para>Para/defter TAŞIMAZ.</para>
/// </summary>
public class FiloPlanHedefi : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string? AracGrupAdi { get; set; }
    public string? Sipp { get; set; }

    /// <summary>Dönem etiketi (ör. "2026-Q1"). Boş = açık uçlu.</summary>
    public string? Donem { get; set; }

    public int HedefAdet { get; set; }
    public string? Aciklama { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
