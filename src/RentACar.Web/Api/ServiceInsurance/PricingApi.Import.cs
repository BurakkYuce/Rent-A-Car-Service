using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Common;
using RentACar.Application.RateMatrices;
using RentACar.Domain.Enums;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Import;

namespace RentACar.Web.Api.ServiceInsurance;

internal static partial class PricingApi
{
    private static readonly SortFieldMap<RateMatrixDto> ImportSort = SortFieldMap<RateMatrixDto>
        .Create(x => x.Id).Alan("kod", x => x.Kod).Alan("kanal", x => x.Kanal).Alan("sube", x => x.Sube)
        .Alan("aracGrupKod", x => x.AracGrupKod).Alan("onayDurumu", x => x.OnayDurumu).Alan("basTar", x => x.BasTar);

    /// <summary>
    /// Tarife aktar ekranı: süzülmüş satırlar + (kanal seçiliyse) toplu silmenin SİLECEĞİ satır sayısı. Sayı ekranın
    /// süzgecinden bağımsız, silmeyle AYNI saf yüklemle (<see cref="RateMatrixService.DeletionCandidate"/>) hesaplanır —
    /// kullanıcı "N satır silinecek" onayında gerçek sayıyı görür (kanalın TÜM şubeleri, yalnız bekleyenler).
    /// </summary>
    private static async Task<Ok<RateImportView>> ImportView(
        string? kanal, string? sube, string? durum, int? sayfa, int? boyut, string? sirala, RateMatrixService svc, CancellationToken ct)
    {
        var state = F5Ortak.EnumAdi<TariffApprovalStatus>(durum, "durum");
        var all = await svc.ListAsync(ct);
        var k = F5Ortak.Nz(kanal);
        var sb = F5Ortak.Nz(sube);
        var rows = all.Where(r => (k is null || string.Equals(r.Kanal?.Trim(), k, StringComparison.OrdinalIgnoreCase))
                                  && (sb is null || string.Equals(r.Sube?.Trim(), sb, StringComparison.OrdinalIgnoreCase))
                                  && (state is null || r.OnayDurumu == state)).ToList();
        int? toDelete = k is null ? null : all.Count(r => RateMatrixService.DeletionCandidate(r, k));
        return TypedResults.Ok(new RateImportView(
            F5Ortak.Sayfala(rows.Select(r => RateMatrixDto.From(r, null)).ToList(), ImportSort, sayfa, boyut, sirala),
            rows.Count(r => r.OnayDurumu == TariffApprovalStatus.Bekliyor), rows.Count(r => r.OnayDurumu == TariffApprovalStatus.Onayli),
            toDelete));
    }

    /// <summary>Excel/CSV yükleme: satırlar <c>Bekliyor</c> girer (onay kolonu yok sayılır — ImportService çiti).</summary>
    private static async Task<Ok<RateImportResult>> Upload(IFormFile? dosya, ImportService imp, CancellationToken ct)
    {
        if (dosya is null || dosya.Length == 0) throw new ValidationException("Dosya seçilmedi.", "dosya");
        if (dosya.Length > ImportRequestLimit) throw new ValidationException("Dosya en fazla 5 MB olabilir.", "dosya");
        var name = dosya.FileName ?? "";
        if (!(name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
              || name.EndsWith(".xls", StringComparison.OrdinalIgnoreCase)))
            throw new ValidationException("Yalnız .xlsx, .xls ya da .csv dosyası yüklenebilir.", "dosya");
        IReadOnlyList<Dictionary<string, string>> rows;
        try
        {
            await using var s = dosya.OpenReadStream();
            rows = ImportService.Parse(s, name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ValidationException)
        {
            throw new ValidationException("Dosya okunamadı (biçim bozuk ya da desteklenmiyor).", "dosya");
        }
        if (rows.Count > 20_000) throw new ValidationException("Tek seferde en çok 20.000 satır aktarılabilir.", "dosya");
        var r = await imp.ImportTarifelerAsync(rows, ct);
        return TypedResults.Ok(new RateImportResult(r.Eklenen, r.Atlanan, r.Hatali, r.Hatalar.Take(20).ToList()));
    }

    /// <summary>Bir kanalın BEKLEYEN satırlarını toplu siler; durum sabit (onaylılar bu yolla silinemez).</summary>
    private static async Task<Ok<RateChannelDeleteResult>> DeleteChannel(RateChannelDeleteRequest r, RateMatrixService svc,
        CancellationToken ct)
        => TypedResults.Ok(new RateChannelDeleteResult(await svc.DeleteByChannelAsync(F5Ortak.Nz(r.Kanal), TariffApprovalStatus.Bekliyor, ct)));
}
