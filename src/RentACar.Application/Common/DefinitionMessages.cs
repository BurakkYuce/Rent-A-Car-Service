using System.Globalization;

namespace RentACar.Application.Common;

/// <summary>F11.1a — shared user-facing texts of definition (master) tables.</summary>
public static class DefinitionMessages
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Delete refused because the row is referenced. Deactivation keeps history intact.</summary>
    public static string InUse(string singularName)
        => $"{char.ToUpper(singularName[0], Tr)}{singularName[1..]} kullanımda olduğu için silinemez; pasife alabilirsiniz.";

    /// <summary>Same as <see cref="InUse(string)"/> with the referencing record kind and count.</summary>
    public static string InUse(string singularName, string usedBy, int count)
        => $"{char.ToUpper(singularName[0], Tr)}{singularName[1..]} {count} {usedBy} kaydında kullanılıyor; silinemez, pasife alabilirsiniz.";
}
