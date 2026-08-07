using RentACar.Domain.Entities;

namespace RentACar.Application.Jobs;

/// <summary>Otomatik servis koşu günlüğü liste filtresi. Boş filtre = son koşular.</summary>
public sealed class JobCalismaLogFilter
{
    public string? JobAdi { get; set; }
    public DateTimeOffset? Bas { get; set; }
    public DateTimeOffset? Bit { get; set; }
    /// <summary><c>true</c> = yalnız başarısızlar, <c>null</c> = hepsi.</summary>
    public bool? YalnizHatali { get; set; }
    /// <summary>Sayfa yükünü sınırlar (günlük hızla büyür).</summary>
    public int EnFazla { get; set; } = 500;
}

public interface IJobCalismaLogRepository
{
    Task<IReadOnlyList<JobCalismaLog>> ListAsync(JobCalismaLogFilter? filter = null, CancellationToken ct = default);

    /// <summary>İş adı başına son koşu (pano özeti: "hangi iş en son ne zaman koştu").</summary>
    Task<IReadOnlyList<JobCalismaLog>> SonKosularAsync(CancellationToken ct = default);
}
