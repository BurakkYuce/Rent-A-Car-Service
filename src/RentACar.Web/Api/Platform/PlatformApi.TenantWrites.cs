using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Common;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Platform;

namespace RentACar.Web.Api.Platform;

public static partial class PlatformApi
{
    // ================================================================== create / update

    private static async Task<Results<Created<PlatformTenantDetailDto>, ProblemHttpResult>> CreateTenant(
        PlatformTenantCreateRequest body, HttpContext http, PlatformAdminService svc, CancellationToken ct)
    {
        var code = (body.Kod ?? "").Trim();
        if (code.Length == 0) throw new ValidationException("Firma kodu zorunludur.", "kod");
        if (code.Length > 64) throw new ValidationException("Firma kodu en çok 64 karakter olabilir.", "kod");
        if (!CodeFormat.IsMatch(code))
            throw new ValidationException(
                "Firma kodu yalnız küçük harf (a-z), rakam ve aradaki tire içerebilir; en az 2 karakter.", "kod");
        if ((body.AdminSifre ?? "").Length > 256)
            throw new ValidationException("Admin parolası en çok 256 karakter olabilir.", "adminSifre");
        if (await svc.TenantCodeTakenAsync(code, ct))
            return UiError.Problem(UiError.ConflictCode, $"'{code}' kodlu firma zaten var.", alan: "kod");

        Guid id;
        try
        {
            id = await svc.CreateTenantAsync(code, body.Ad ?? "", body.AdminKullanici ?? "", body.AdminSifre ?? "",
                OperatorName(http), ct);
        }
        catch (ValidationException ex) when (ex.Message.EndsWith("kodlu firma zaten var.", StringComparison.Ordinal))
        {
            // Lost the race after the pre-check: the unique index decided — same 409 as the pre-check.
            return UiError.Problem(UiError.ConflictCode, ex.Message, alan: "kod");
        }
        return await LoadDetailAsync(svc, id, ct) is { } d
            ? TypedResults.Created($"{Root}/kiracilar/{id}", d)
            : TenantNotFound();
    }

    /// <summary>Full replace of the info fields; <c>surum</c> is mandatory (409 <c>cakisma</c> on mismatch,
    /// checked under the row lock in the service).</summary>
    private static async Task<Results<Ok<PlatformTenantDetailDto>, ProblemHttpResult>> UpdateTenant(
        Guid id, PlatformTenantUpdateRequest body, HttpContext http, PlatformAdminService svc, CancellationToken ct)
    {
        if (!await svc.TenantExistsAsync(id, ct)) return TenantNotFound();
        if (string.IsNullOrWhiteSpace(body.Surum))
            throw new ValidationException("Sürüm (surum) zorunludur; kaydı yeniden yükleyip tekrar deneyin.", "surum");
        if (!string.IsNullOrWhiteSpace(body.Eposta) && !EmailFormat.IsMatch(body.Eposta.Trim()))
            throw new ValidationException("E-posta biçimi geçersiz.", "eposta");
        await svc.UpdateTenantAsync(id, body.Ad ?? "", body.YetkiliAd, body.Eposta, body.Telefon, body.Notlar, body.Plan,
            OperatorName(http), ct, expectedVersion: body.Surum.Trim());
        return await DetailAfterWrite(svc, id, ct);
    }

    // ================================================================== status / switches

    /// <summary>
    /// Aktif / Pasif / Kapali (Blazor toggle + close + reopen). Transitions:
    /// Aktif↔Pasif = temporary suspension; →Kapali needs <c>onayKod</c> == tenant code (typed confirmation, checked
    /// on the server); Kapali→Aktif = reopen; Kapali→Pasif is refused (reopen first). Every change invalidates the
    /// status cache in the service → open sessions of the tenant drop on their next request (401 <c>kiraci_kapali</c>).
    /// Same-state requests are no-ops (idempotent, no audit row).
    /// </summary>
    private static async Task<Results<Ok<PlatformTenantDetailDto>, ProblemHttpResult>> ChangeStatus(
        Guid id, PlatformTenantStatusRequest body, HttpContext http, PlatformAdminService svc, CancellationToken ct)
    {
        var current = await svc.GetTenantAsync(id, ct);
        if (current is null) return TenantNotFound();
        var target = ParseStatus(body.Durum, "durum")
                     ?? throw new ValidationException("Durum zorunludur (Aktif, Pasif ya da Kapali).", "durum");
        var op = OperatorName(http);
        var closed = current.KapanisTarihiUtc is not null;

        switch (target)
        {
            case StatusClosed:
                if (!string.Equals((body.OnayKod ?? "").Trim(), current.Code, StringComparison.Ordinal))
                    throw new ValidationException(
                        "Onay kodu uyuşmadı — firma KAPATILMADI. Kapatmak için firma kodunu aynen yazın.", "onayKod");
                await svc.CloseAsync(id, op, ct);
                break;
            case StatusActive when closed:
                await svc.ReopenAsync(id, op, ct);
                break;
            case StatusActive:
                await svc.SetActiveAsync(id, true, op, ct);
                break;
            case StatusPassive when closed:
                throw new ValidationException("Kapalı firma pasife alınamaz — önce yeniden açın.", "durum");
            default:
                await svc.SetActiveAsync(id, false, op, ct);
                break;
        }
        return await DetailAfterWrite(svc, id, ct);
    }

    /// <summary>New-UI pilot switch (<c>TenantSettings.YeniArayuzPilot</c>); read uncached by the pilot gate, so it
    /// takes effect on the tenant's next request.</summary>
    private static async Task<Results<Ok<PlatformTenantDetailDto>, ProblemHttpResult>> SetPilot(
        Guid id, PlatformSwitchRequest body, HttpContext http, PlatformAdminService svc, CancellationToken ct)
    {
        if (!await svc.TenantExistsAsync(id, ct)) return TenantNotFound();
        var on = body.Aktif ?? throw new ValidationException("Aktif (true/false) zorunludur.", "aktif");
        await svc.SetNewUiPilotAsync(id, on, OperatorName(http), ct);
        return await DetailAfterWrite(svc, id, ct);
    }

    /// <summary>"Web Sitesi" module licence (purchase decision — only the platform writes it).</summary>
    private static async Task<Results<Ok<PlatformTenantDetailDto>, ProblemHttpResult>> SetWebSiteModule(
        Guid id, PlatformSwitchRequest body, HttpContext http, PlatformAdminService svc, CancellationToken ct)
    {
        if (!await svc.TenantExistsAsync(id, ct)) return TenantNotFound();
        var on = body.Aktif ?? throw new ValidationException("Aktif (true/false) zorunludur.", "aktif");
        await svc.SetWebsiteModuleAsync(id, on, OperatorName(http), ct);
        return await DetailAfterWrite(svc, id, ct);
    }

    // ================================================================== logo

    /// <summary>Logo preview — inline (no file name → no Content-Disposition), PNG only (validated on upload).</summary>
    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> LogoContent(
        Guid id, PlatformAdminService svc, CancellationToken ct)
    {
        if (!await svc.TenantExistsAsync(id, ct)) return TenantNotFound();
        var (bytes, _) = await svc.GetTenantLogoAsync(id, ct);
        return bytes is { Length: > 0 }
            ? TypedResults.File(bytes, "image/png")
            : F5Shared.NotFound("Logo yüklü değil.");
    }

    private static async Task<Results<Ok<PlatformTenantDetailDto>, ProblemHttpResult>> UploadLogo(
        Guid id, IFormFile? logo, HttpContext http, PlatformAdminService svc, CancellationToken ct)
    {
        if (!await svc.TenantExistsAsync(id, ct)) return TenantNotFound();
        if (logo is null || logo.Length == 0) throw new ValidationException("Logo dosyası seçilmedi.", "logo");
        if (logo.Length > LogoValidationRules.MaxBytes) throw new ValidationException("Logo en fazla 1 MB olabilir.", "logo");
        using var ms = new MemoryStream();
        await logo.CopyToAsync(ms, ct);
        await svc.SetTenantLogoAsync(id, ms.ToArray(), OperatorName(http), ct); // type/size/width: LogoKurallari
        return await DetailAfterWrite(svc, id, ct);
    }

    private static async Task<Results<Ok<PlatformTenantDetailDto>, ProblemHttpResult>> DeleteLogo(
        Guid id, HttpContext http, PlatformAdminService svc, CancellationToken ct)
    {
        if (!await svc.TenantExistsAsync(id, ct)) return TenantNotFound();
        await svc.SetTenantLogoAsync(id, null, OperatorName(http), ct);
        return await DetailAfterWrite(svc, id, ct);
    }
}
