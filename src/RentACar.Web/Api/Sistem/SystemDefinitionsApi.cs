using RentACar.Application.Common;
using RentACar.Web.Api.Rezervasyon;

namespace RentACar.Web.Api.Sistem;

/// <summary>
/// F11.1b — F11 envanterinin ikinci yarısındaki tanım ekranları (Blazor paritesi):
/// <c>/sigorta-sirketleri</c>, <c>/kdv-oranlari</c>, <c>/ceza-turleri</c>, <c>/lokasyonlar</c>, <c>/personel</c>,
/// <c>/arac-gruplari</c>, <c>/belge-sablonlari</c> ve <c>/rezervasyon-kaynaklari/{id}/yansit</c>.
/// <list type="bullet">
/// <item>İzinler Blazor uçlarıyla BİREBİR: tanımlar OperationsWrite; personel ve belge şablonu ManageUsers.</item>
/// <item>PUT tam değiştirmedir: zorunlu <c>surum</c> (xmin), kilit altında karşılaştırma, uyuşmazlık 409 <c>cakisma</c>.</item>
/// <item>Salt <c>MasterTanimService</c> tabanlı tanımlar (ödeme tipi, vites türü, renk) ve rezervasyon kaynağının
/// temel CRUD'u F11.1a genel tanım tabanına bırakıldı (ortak çekirdek tek PR'da değişir).</item>
/// </list>
/// </summary>
public static partial class SystemDefinitionsApi
{
    public static void MapSystemDefinitionsApi(this RouteGroupBuilder v1)
    {
        MapInsuranceCompanies(v1);
        MapKdvRates(v1);
        MapPenaltyTypes(v1);
        MapLocations(v1);
        MapPersonnel(v1);
        MapVehicleGroups(v1);
        MapDocumentTemplates(v1);
        MapReservationSourceExtras(v1);
    }

    /// <summary>Metin alanlarında Türkçe-duyarsız arama + aktiflik süzgeci (bellekte; tanım tabloları küçük).</summary>
    private static IReadOnlyList<T> Filter<T>(IEnumerable<T> rows, string? search, bool? active,
        Func<T, IEnumerable<string?>> fields, Func<T, bool> isActive)
    {
        var q = rows;
        if (active is bool a) q = q.Where(x => isActive(x) == a);
        if (SystemApiCommon.Clean(search) is { } s)
        {
            var needle = TurkishText.Normalize(s);
            q = q.Where(x => fields(x).Any(f => f is not null && TurkishText.Normalize(f).Contains(needle, StringComparison.Ordinal)));
        }
        return q.ToList();
    }

    private static void Text(string? value, int max, string field, string label) => Kira.Sinirlar.Metin(value, max, field, label);

    private static void Amount(decimal? value, string field, string label) => Kira.Sinirlar.Tutar(value, field, label);

    /// <summary>DB'ye giden an UTC (Npgsql timestamptz yalnız offset 0).</summary>
    private static DateTimeOffset? Utc(DateTimeOffset? value) => F5Ortak.Utc(value);

    private static T? EnumName<T>(string? value, string field) where T : struct, Enum => F5Ortak.EnumAdi<T>(value, field);

    private static void NotNegative(int? value, string field, string label)
    {
        if (value is < 0) throw new ValidationException($"{label} negatif olamaz.", field);
    }
}
