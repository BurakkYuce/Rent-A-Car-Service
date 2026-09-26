using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Common;
using RentACar.Web.Api.Rezervasyon;

namespace RentACar.Web.Api.Sistem;

/// <summary>F11.1b uçlarının ortak uç-katmanı yardımcıları (iş mantığı YOK).</summary>
internal static class SystemApiCommon
{
    public const string Tag = "Sistem";
    public const string WebsiteTag = "Web Sitesi";
    public const string DefinitionsTag = "Tanımlar";

    public static ProblemHttpResult NotFound(string detail = "Kayıt bulunamadı.") => F5Shared.NotFound(detail);

    /// <summary>Tam değiştirme PUT'unda <c>surum</c> zorunlu (DEVIR §5).</summary>
    public static void RequireVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ValidationException("Kayıt sürümü (surum) zorunludur; kaydı yeniden açın.", "surum");
    }

    /// <summary>Boş/boşluk → null, aksi Trim.</summary>
    public static string? Clean(string? s) => F5Shared.Nz(s);
}
