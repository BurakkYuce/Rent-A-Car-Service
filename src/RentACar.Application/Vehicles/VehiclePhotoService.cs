using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Vehicles;

public sealed record VehiclePhotoContent(byte[] Bytes, string ContentType);

/// <summary>
/// Araç fotoğraf galerisi iş mantığı (PR-3). Okuma guard'ı `VehicleService.GetAsync` ile BİREBİR aynı
/// (PermissionGuard YOK, yalnız BranchScope) — public site (PR-4) bu okuma yollarını doğrudan reuse
/// edebilir (PublicTenantContext.Role=null → BranchScope.EffectiveFilter Unrestricted döner, doğrulandı).
/// Yazma guard'ı `VehicleService.DeleteAsync` ile aynı (PermissionGuard.Require(OperationsWrite) + BranchScope).
/// </summary>
public sealed class VehiclePhotoService(
    IVehiclePhotoRepository repository, IVehicleRepository vehicles, ICurrentUser currentUser)
{
    private const long MaxBytes = 2 * 1024 * 1024; // 2 MB
    private const int MaxPhotos = 20;

    private async Task<Vehicle> RequireVehicleAsync(Guid vehicleId, CancellationToken ct)
    {
        var v = await vehicles.FindAsync(vehicleId, ct) ?? throw new ValidationException("Araç bulunamadı.");
        BranchScope.RequireInScope(currentUser, v.SubeId, v.Sube);
        return v;
    }

    public async Task<IReadOnlyList<VehiclePhotoMeta>> ListMetaAsync(Guid vehicleId, CancellationToken ct = default)
    {
        await RequireVehicleAsync(vehicleId, ct);
        return await repository.ListMetaAsync(vehicleId, ct);
    }

    public async Task<VehiclePhotoContent?> GetBytesAsync(Guid vehicleId, Guid photoId, CancellationToken ct = default)
    {
        await RequireVehicleAsync(vehicleId, ct);
        var p = await repository.FindAsync(vehicleId, photoId, ct);
        return p is null ? null : new VehiclePhotoContent(p.Bytes, p.ContentType);
    }

    /// <summary>ThumbBytes yoksa (üretim başarısız olmuştu) tam boya düşer — serve ucu her zaman bir şey döner.</summary>
    public async Task<VehiclePhotoContent?> GetThumbAsync(Guid vehicleId, Guid photoId, CancellationToken ct = default)
    {
        await RequireVehicleAsync(vehicleId, ct);
        var p = await repository.FindAsync(vehicleId, photoId, ct);
        if (p is null) return null;
        return p.ThumbBytes is { Length: > 0 }
            ? new VehiclePhotoContent(p.ThumbBytes, "image/jpeg")
            : new VehiclePhotoContent(p.Bytes, p.ContentType);
    }

    public async Task AddAsync(Guid vehicleId, byte[] bytes, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        await RequireVehicleAsync(vehicleId, ct);

        var kind = ImageValidation.Detect(bytes);
        if (kind == ImageKind.Unknown)
            throw new ValidationException("PNG, JPEG veya WebP yükleyin.");
        if (bytes.Length > MaxBytes)
            throw new ValidationException("Fotoğraf en fazla 2 MB olabilir.");
        if (await repository.CountAsync(vehicleId, ct) >= MaxPhotos)
            throw new ValidationException($"Araç başına en fazla {MaxPhotos} fotoğraf yüklenebilir.");

        var thumb = ImageProcessing.TryCreateThumbnail(bytes); // başarısızsa null — upload yine de tamamlanır

        await repository.AddAsync(new VehiclePhoto
        {
            VehicleId = vehicleId,
            Bytes = bytes,
            ContentType = ImageValidation.ContentType(kind),
            ThumbBytes = thumb
        }, ct);
    }

    public async Task DeleteAsync(Guid vehicleId, Guid photoId, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        await RequireVehicleAsync(vehicleId, ct);
        await repository.DeleteAsync(vehicleId, photoId, ct);
    }

    public async Task MoveAsync(Guid vehicleId, Guid photoId, int direction, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        await RequireVehicleAsync(vehicleId, ct);
        await repository.MoveAsync(vehicleId, photoId, direction, ct);
    }
}
