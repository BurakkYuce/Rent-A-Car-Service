using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Integrations;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.GelenEFaturalar;

/// <summary>
/// Gelen e-Fatura triage iş mantığı: liste + elle giriş + GİB-sync (stub) + durum akışı
/// (Beklemede→Onaylandı/Reddedildi; Onaylandı→İşlendi). Yazma → <see cref="Permission.FinanceWrite"/>.
/// ETTN tenant içinde benzersiz (elle + sync upsert idempotent). DEFTERE POSTLAMAZ — gelen faturayı
/// gidere dönüştürme (para) ileriki adım. Tenant izolasyonu/audit alt katmanda otomatik.
/// </summary>
public sealed class GelenEFaturaService(
    IGelenEFaturaRepository repository, IEInvoiceService einvoice, ICurrentUser currentUser)
{
    private readonly IGelenEFaturaRepository _repository = repository;
    private readonly IEInvoiceService _einvoice = einvoice;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<GelenEFatura>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<GelenEFatura?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateManualAsync(GelenEFaturaInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        var ettn = (input.Ettn ?? string.Empty).Trim();
        var vkn = (input.GonderenVkn ?? string.Empty).Trim();
        var unvan = (input.GonderenUnvan ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(ettn)) throw new ValidationException("ETTN zorunludur.");
        if (string.IsNullOrWhiteSpace(vkn)) throw new ValidationException("Gönderen VKN zorunludur.");
        if (string.IsNullOrWhiteSpace(unvan)) throw new ValidationException("Gönderen ünvanı zorunludur.");
        if (input.NetTutar < 0m || input.KdvTutar < 0m || input.GenelToplam < 0m)
            throw new ValidationException("Tutarlar negatif olamaz.");
        if (await _repository.EttnExistsAsync(ettn, ct))
            throw new ValidationException($"'{ettn}' ETTN'li gelen fatura zaten kayıtlı.");

        var row = new GelenEFatura
        {
            Ettn = ettn,
            GonderenVkn = vkn,
            GonderenUnvan = unvan,
            Tarih = input.Tarih ?? DateTimeOffset.UtcNow,
            NetTutar = input.NetTutar,
            KdvTutar = input.KdvTutar,
            GenelToplam = input.GenelToplam,
            Currency = string.IsNullOrWhiteSpace(input.Currency) ? "TRY" : input.Currency!.Trim().ToUpperInvariant(),
            Aciklama = string.IsNullOrWhiteSpace(input.Aciklama) ? null : input.Aciklama!.Trim(),
            Durum = GelenEFaturaDurum.Beklemede
        };
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    /// <summary>GİB gelen kutusunu çekip ETTN'e göre upsert eder (mevcut ETTN atlanır → idempotent).
    /// Stub boş döndüğünden kimlik yapılandırılana dek 0 ekler. Eklenen adet döner.</summary>
    public async Task<int> SyncFromGibAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        var items = await _einvoice.FetchInboxAsync(from, to, ct);
        int added = 0;
        foreach (var it in items)
        {
            if (string.IsNullOrWhiteSpace(it.Ettn) || await _repository.EttnExistsAsync(it.Ettn.Trim(), ct)) continue;
            await _repository.CreateAsync(new GelenEFatura
            {
                Ettn = it.Ettn.Trim(),
                GonderenVkn = it.GonderenVkn,
                GonderenUnvan = it.GonderenUnvan,
                Tarih = it.Tarih,
                NetTutar = it.NetTutar,
                KdvTutar = it.KdvTutar,
                GenelToplam = it.GenelToplam,
                Currency = string.IsNullOrWhiteSpace(it.Currency) ? "TRY" : it.Currency,
                Durum = GelenEFaturaDurum.Beklemede
            }, ct);
            added++;
        }
        return added;
    }

    public Task<bool> OnaylaAsync(Guid id, CancellationToken ct = default)
        => TransitionAsync(id, GelenEFaturaDurum.Beklemede, GelenEFaturaDurum.Onaylandi,
            "Yalnız beklemedeki fatura onaylanabilir.", null, ct);

    public Task<bool> ReddetAsync(Guid id, string? neden, CancellationToken ct = default)
        => TransitionAsync(id, GelenEFaturaDurum.Beklemede, GelenEFaturaDurum.Reddedildi,
            "Yalnız beklemedeki fatura reddedilebilir.", string.IsNullOrWhiteSpace(neden) ? null : neden.Trim(), ct);

    public Task<bool> IsleAsync(Guid id, CancellationToken ct = default)
        => TransitionAsync(id, GelenEFaturaDurum.Onaylandi, GelenEFaturaDurum.Islendi,
            "Yalnız onaylanmış fatura işlenebilir.", null, ct);

    private async Task<bool> TransitionAsync(
        Guid id, GelenEFaturaDurum from, GelenEFaturaDurum to, string hata, string? redNedeni, CancellationToken ct)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        return await _repository.UpdateAsync(id, row =>
        {
            if (row.Durum != from) throw new ValidationException(hata);
            row.Durum = to;
            if (redNedeni is not null) row.RedNedeni = redNedeni;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }
}
