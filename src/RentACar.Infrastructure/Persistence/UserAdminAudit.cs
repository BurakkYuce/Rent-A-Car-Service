using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Users;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// F11.1b güvenlik M2 — kullanıcı yönetimi eylemlerinin AÇIK denetim kaydı. <see cref="User"/> ve
/// <see cref="KullaniciIzinIstisna"/> platform desenindedir ve IAuditable DEĞİLDİR (interceptor onları denetlemez);
/// oluşturma, durum, parola ve izin istisnası değişiklikleri buradan AuditLog'a yazılır. Parola ya da hash ASLA yazılmaz.
/// Aktör kimliği context'in oturum kullanıcısıdır. Aynı SaveChanges/işlem içinde eklenir → atomik.
/// </summary>
internal static class UserAdminAudit
{
    public const string UsersEntity = "Users";
    public const string ExceptionsEntity = "KullaniciIzinIstisnalari";

    public static void Add(AppDbContext db, string entityName, Guid entityId, AuditAction action, UserAuditEntry entry,
        IReadOnlyDictionary<string, object?>? extra = null)
    {
        var payload = new Dictionary<string, object?> { ["Islem"] = entry.Operation };
        if (entry.Detail is not null) payload["Detay"] = entry.Detail;
        if (extra is not null)
            foreach (var (k, v) in extra) payload[k] = v;
        db.Set<AuditLog>().Add(new AuditLog
        {
            TenantId = db.TenantId,
            EntityName = entityName,
            EntityId = entityId.ToString(),
            Action = action,
            UserId = db.CurrentUserId,
            UserName = db.CurrentUserName,
            TimestampUtc = DateTimeOffset.UtcNow,
            NewValues = JsonSerializer.Serialize(payload),
        });
    }

    /// <summary>Kiracı başına kullanıcı yönetimi kilidi (işlem sonuna kadar).</summary>
    public static Task LockTenantAsync(AppDbContext db, CancellationToken ct)
        => db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended({0}, 0))", ["users-admin:" + db.TenantId], ct);
}
