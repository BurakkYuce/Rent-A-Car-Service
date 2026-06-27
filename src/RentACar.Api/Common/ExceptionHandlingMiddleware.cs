using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;

namespace RentACar.Api.Common;

/// <summary>Tutarlı JSON hata zarfı (error code + message).</summary>
public sealed record ApiError(string Error, string Message);

/// <summary>
/// Domain/uygulama istisnalarını tutarlı HTTP durum + JSON zarfına çevirir:
///   ValidationException → 400, çakışma (Availability/Duplicate) → 409, diğer → 500.
/// İş kuralı hataları log'a WARNING, beklenmeyenler ERROR olarak yazılır.
/// </summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        // NOT: çakışma istisnaları ValidationException'dan TÜRER → önce onları yakala (sıra önemli).
        catch (AvailabilityConflictException ex)
        {
            await WriteAsync(ctx, StatusCodes.Status409Conflict, "conflict", ex.Message);
        }
        catch (DuplicateCariException ex)
        {
            await WriteAsync(ctx, StatusCodes.Status409Conflict, "duplicate", ex.Message);
        }
        catch (ValidationException ex)
        {
            await WriteAsync(ctx, StatusCodes.Status400BadRequest, "validation", ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Beklenmeyen hata: {Path}", ctx.Request.Path);
            await WriteAsync(ctx, StatusCodes.Status500InternalServerError, "server_error", "Beklenmeyen bir hata oluştu.");
        }
    }

    private static async Task WriteAsync(HttpContext ctx, int status, string error, string message)
    {
        if (ctx.Response.HasStarted) return;
        ctx.Response.Clear();
        ctx.Response.StatusCode = status;
        // WriteAsJsonAsync → uygulamanın yapılandırılmış JSON seçenekleri (camelCase) ile tutarlı zarf.
        await ctx.Response.WriteAsJsonAsync(new ApiError(error, message));
    }
}
