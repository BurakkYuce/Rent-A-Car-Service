namespace RentACar.Web.Platform;

/// <summary>Platform operatörü claim adları + policy adı (tek doğruluk kaynağı).</summary>
public static class PlatformClaims
{
    /// <summary>Platform süper-admin işareti — YALNIZ /platform/auth/login yazar; tenant login'i ASLA.</summary>
    public const string PlatformAdmin = "platform_admin";

    /// <summary>Authorization policy adı.</summary>
    public const string Policy = "PlatformAdmin";
}
