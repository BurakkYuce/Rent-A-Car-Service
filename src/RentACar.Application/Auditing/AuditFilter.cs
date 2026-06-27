using RentACar.Domain.Enums;

namespace RentACar.Application.Auditing;

/// <summary>Denetim kaydı arama/filtre + sayfalama.</summary>
public sealed class AuditFilter
{
    public string? EntityName { get; set; }   // tablo adı (içeren)
    public string? UserName { get; set; }      // kullanıcı (içeren)
    public AuditAction? Action { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 30;
}
