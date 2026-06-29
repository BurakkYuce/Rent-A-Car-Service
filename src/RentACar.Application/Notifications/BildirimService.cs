using RentACar.Application.Crm;
using RentACar.Application.Periods;
using RentACar.Application.Regulation;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Notifications;

/// <summary>Bildirim merkezi agrega (roadmap G6): vade uyarıları + açık şikayet sayısı/listesi + dönem durumu.</summary>
public sealed record BildirimDto(
    int VadeGecmis,
    int VadeYakin,
    IReadOnlyList<VadeItem> Vadeler,
    int AcikSikayet,
    IReadOnlyList<Sikayet> Sikayetler,
    DateTimeOffset? DonemKapanis);

/// <summary>
/// Bildirim merkezi (roadmap G6): MEVCUT servislerden salt-okur derleme — yeni tablo/entity YOK.
/// Vade uyarıları (sigorta/muayene/MTV), açık şikayetler, dönem kapanış durumu tek yerde toplanır.
/// </summary>
public sealed class BildirimService(VadeService vade, SikayetService sikayet, DonemKilidiService donem)
{
    private readonly VadeService _vade = vade;
    private readonly SikayetService _sikayet = sikayet;
    private readonly DonemKilidiService _donem = donem;

    public async Task<BildirimDto> GetAsync(DateTimeOffset? now = null, CancellationToken ct = default)
    {
        var warnings = await _vade.GetWarningsAsync(now, ct);
        var gecmis = warnings.Count(w => w.Bucket == VadeBucket.Gecmis);
        var yakin = warnings.Count(w => w.Bucket != VadeBucket.Gecmis);

        var acik = (await _sikayet.ListAsync(ct)).Where(s => s.Durum == SikayetDurum.Acik).ToList();
        var kapanis = await _donem.GetClosingDateAsync(ct);

        return new BildirimDto(gecmis, yakin, warnings, acik.Count, acik, kapanis);
    }
}
