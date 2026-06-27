using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Interceptors;

/// <summary>
/// SaveChanges anında iki iş yapar:
///   1) Eklenen tenant-owned entity'lere TenantId damgalar (uygulama elle set etmez).
///   2) IAuditable entity'lerin create/update/delete'ini AuditLog'a yazar
///      (kim/ne zaman/eski-yeni; eski-yeni ChangeTracker'dan).
/// Kimlik/tenant bilgisini AppDbContext örneğinden okur → bağımlılıksız, singleton olabilir.
/// Recursion guard: AuditLog IAuditable değildir, kendini denetlemez. Audit satırları
/// aynı SaveChanges/transaction içinde yazılır → atomik.
/// </summary>
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        StampAndAudit(eventData.Context as AppDbContext);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        StampAndAudit(eventData.Context as AppDbContext);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void StampAndAudit(AppDbContext? db)
    {
        if (db is null) return;
        db.ChangeTracker.DetectChanges();

        // 1) Tenant damgalama — audit'ten ÖNCE (audit, entity.TenantId'yi okur).
        foreach (var entry in db.ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = db.TenantId;
        }

        // 2) Audit — yalnız IAuditable (AuditLog hariç; o IAuditable değil).
        var audits = new List<AuditLog>();
        foreach (var entry in db.ChangeTracker.Entries())
        {
            if (entry.Entity is not IAuditable) continue;

            var action = entry.State switch
            {
                EntityState.Added => (AuditAction?)AuditAction.Create,
                EntityState.Modified => AuditAction.Update,
                EntityState.Deleted => AuditAction.Delete,
                _ => null
            };
            if (action is null) continue;

            audits.Add(BuildAudit(db, entry, action.Value));
        }

        if (audits.Count > 0)
            db.AuditLogs.AddRange(audits);
    }

    private static AuditLog BuildAudit(AppDbContext db, EntityEntry entry, AuditAction action)
    {
        var pk = entry.Metadata.FindPrimaryKey()!.Properties[0];
        var entityId = entry.Property(pk.Name).CurrentValue?.ToString() ?? string.Empty;
        var tenantId = entry.Entity is ITenantOwned owned ? owned.TenantId : db.TenantId;

        Dictionary<string, object?>? oldValues = null;
        Dictionary<string, object?>? newValues = null;

        switch (action)
        {
            case AuditAction.Create:
                newValues = Snapshot(entry, original: false);
                break;
            case AuditAction.Delete:
                oldValues = Snapshot(entry, original: true);
                break;
            default: // Update — yalnız değişen alanlar
                oldValues = new Dictionary<string, object?>();
                newValues = new Dictionary<string, object?>();
                foreach (var p in entry.Properties)
                {
                    if (!p.IsModified) continue;
                    oldValues[p.Metadata.Name] = p.OriginalValue;
                    newValues[p.Metadata.Name] = p.CurrentValue;
                }
                break;
        }

        return new AuditLog
        {
            TenantId = tenantId,
            EntityName = entry.Metadata.GetTableName() ?? entry.Metadata.ClrType.Name,
            EntityId = entityId,
            Action = action,
            UserId = db.CurrentUserId,
            UserName = db.CurrentUserName,
            TimestampUtc = DateTimeOffset.UtcNow,
            OldValues = Serialize(oldValues),
            NewValues = Serialize(newValues)
        };
    }

    private static Dictionary<string, object?> Snapshot(EntityEntry entry, bool original)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var p in entry.Properties)
            dict[p.Metadata.Name] = original ? p.OriginalValue : p.CurrentValue;
        return dict;
    }

    private static string? Serialize(Dictionary<string, object?>? values)
        => values is null ? null : JsonSerializer.Serialize(values, JsonOpts);
}
