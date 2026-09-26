using RentACar.Application.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Common;

/// <summary>
/// F1.4 — <c>/api/ui</c> uçlarının (F1.2+) para işlemine vereceği <c>IslemAnahtari</c>'ni seçen TEK yer.
///
/// <para><b>Kullanım (uç gövdesinde):</b>
/// <c>input.IslemAnahtari = IdempotencyBasligi.Anahtar(ctx, deterministik: dto.TahsilatAnahtar);</c>
/// Deterministik anahtarı olmayan işlemde <c>deterministik: null</c> verilir.</para>
///
/// <para><b>Kurallar:</b>
/// <list type="number">
/// <item>Sunucunun deterministik anahtarı varsa O kazanır (<see cref="OperationKeyDeriver.Select"/>) —
/// başlık onu ezemez.</item>
/// <item>Yoksa <c>Idempotency-Key</c> başlığı → <c>UUIDv5(tenantId | userId | başlık)</c>. Ham değer
/// ASLA anahtar/PK olmaz. Kiracı ve kullanıcı oturum claim'lerinden okunur (istemci gövdesinden DEĞİL).</item>
/// <item>Başlık yoksa <c>null</c> — işlem bugünkü anahtarsız davranışıyla çalışır.</item>
/// <item>Başlık VAR ama biçimsizse (boş, kısa, ASCII-dışı, birden fazla değer) 400 — deterministik anahtar
/// olsa bile: bozuk istemci sessizce korumasız kalmasın.</item>
/// </list></para>
/// </summary>
public static class IdempotencyHeader
{
    /// <summary>Başlık adı (IETF draft-ietf-httpapi-idempotency-key-header).</summary>
    public const string Name = "Idempotency-Key";

    /// <summary>Öncelik kuralıyla seçilmiş anahtar (deterministik ▸ başlıktan türetilen ▸ null).</summary>
    public static Guid? Key(HttpContext ctx, Guid? deterministic = null)
        => OperationKeyDeriver.Select(deterministic, DeriveFromHeader(ctx));

    /// <summary>
    /// <see cref="Key"/> ile aynı seçim; ancak ne deterministik anahtar ne başlık varsa 400
    /// (<c>errors["Idempotency-Key"]</c>). Anahtarsız çağrı her seferinde YENİ işlem sayıldığı için
    /// (envanter E01/E21), SPA'nın para yaratan uçları başlıksız istekle çift yazıma açık kalmasın diye
    /// <c>/api/ui</c> para uçları bunu kullanır.
    /// </summary>
    public static Guid RequiredKey(HttpContext ctx, Guid? deterministic = null)
        => Key(ctx, deterministic)
           ?? throw new ValidationException("Idempotency-Key başlığı zorunlu (para işlemi çift gönderim koruması).", Name);

    /// <summary>
    /// Yalnız başlıktan türetilen anahtar; başlık yoksa <c>null</c>. Kimliksiz istekte başlık varsa
    /// <see cref="InvalidOperationException"/> (bu yardımcı yalnız oturumlu uçlarda kullanılır; anonim
    /// istekte türetmek tüm anonimleri tek ad alanında toplardı).
    /// </summary>
    public static Guid? DeriveFromHeader(HttpContext ctx)
    {
        if (!ctx.Request.Headers.TryGetValue(Name, out var values)) return null;
        if (values.Count != 1)
            throw new ValidationException("Idempotency-Key başlığı tek değer olmalı.", Name);
        var value = values[0];
        if (!OperationKeyDeriver.IsValid(value))
            throw new ValidationException(
                $"Idempotency-Key geçersiz: {OperationKeyDeriver.MinLength}-{OperationKeyDeriver.MaxLength} " +
                "karakter, yalnız görünür ASCII olmalı.", Name);

        var tenant = ClaimGuid(ctx, IdentityClaims.TenantId);
        var user = ClaimGuid(ctx, IdentityClaims.UserId);
        if (tenant is null || user is null)
            throw new InvalidOperationException("Idempotency-Key kimliksiz istekte türetilemez (tenant/kullanıcı claim'i yok).");
        return OperationKeyDeriver.Derive(tenant.Value, user.Value, value!);
    }

    private static Guid? ClaimGuid(HttpContext ctx, string tip)
        => Guid.TryParse(ctx.User.FindFirst(tip)?.Value, out var g) && g != Guid.Empty ? g : null;
}
