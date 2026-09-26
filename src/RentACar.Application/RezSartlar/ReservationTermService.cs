using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.RezSartlar;

/// <summary>
/// Rez şartı (müşteri özel talebi) iş mantığı: doğrulama + CRUD + karşılandı işaretleme.
/// Tüm yazma işlemleri <see cref="Permission.OperationsWrite"/> ister; okuma yetkisizdir (liste
/// ekranı zaten <c>[Authorize]</c> arkasında). Tenant izolasyonu/audit alt katmanda otomatik.
///
/// <para>Para taşımaz, deftere kayıt POSTLAMAZ — saf operasyonel not.</para>
/// </summary>
public sealed class ReservationTermService(
    IReservationTermRepository repository, ICustomerRepository customers, ICurrentUser currentUser)
{
    private readonly IReservationTermRepository _repository = repository;
    private readonly ICustomerRepository _customers = customers;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<RezSart>> ListAsync(RezSartFilter? filter = null, CancellationToken ct = default)
        => _repository.ListAsync(filter, ct);

    public Task<RezSart?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(RezSartInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        await CustomerExists(n.MusteriId, ct);

        var row = new RezSart { TalepTarihi = n.TalepTarihi ?? DateTimeOffset.UtcNow };
        Apply(row, n);
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public Task<bool> UpdateAsync(Guid id, RezSartInput input, CancellationToken ct = default)
        => UpdateAsync(id, input, expectedVersion: null, ct);

    /// <summary>F5.1 — <paramref name="expectedVersion"/> doluysa satır kilidi altında sürüm karşılaştırmalı tam
    /// değiştirme (<see cref="ConcurrentModificationException"/>); null → Blazor yolu (davranış değişmedi).</summary>
    public async Task<bool> UpdateAsync(Guid id, RezSartInput input, string? expectedVersion, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        await CustomerExists(n.MusteriId, ct);

        void ApplyChanges(RezSart row)
        {
            // TalepTarihi boş gelirse MEVCUT değer korunur (form onu göndermiyor olabilir) — create'te
            // UtcNow'a düşer. Kayıt tarihini sessizce "şimdi"ye kaydırmak geçmişi bozardı.
            if (n.TalepTarihi is { } tt) row.TalepTarihi = tt;
            Apply(row, n);
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        return expectedVersion is null
            ? await _repository.UpdateAsync(id, ApplyChanges, ct)
            : await _repository.UpdateAsync(id, expectedVersion, ApplyChanges, ct);
    }

    /// <summary>Talebi karşılandı işaretler (tek tıklık akış). Zaten karşılanmışsa tarihi DEĞİŞTİRMEZ —
    /// ilk karşılanma anı kayıttır, tekrar tıklamak onu ileri kaydırmamalı.</summary>
    public async Task<bool> MarkFulfilledAsync(Guid id, string? deliverer = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return await _repository.UpdateAsync(id, row =>
        {
            row.KarsilamaTarihi ??= DateTimeOffset.UtcNow;
            var t = TrimOrNull(deliverer);
            if (t is not null) row.TeslimEden = t;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>Karşılandı işaretini geri alır (yanlış işaretleme düzeltmesi).</summary>
    public async Task<bool> UndoFulfillmentAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return await _repository.UpdateAsync(id, row =>
        {
            row.KarsilamaTarihi = null;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private async Task CustomerExists(Guid customerId, CancellationToken ct)
    {
        // Tenant filtresi repo katmanında → başka tenant'ın müşterisi "bulunamadı" döner.
        if (await _customers.FindAsync(customerId, ct) is null)
            throw new ValidationException("Seçilen müşteri bulunamadı.");
    }

    private static void Validate(RezSartInput n)
    {
        if (n.MusteriId == Guid.Empty) throw new ValidationException("Müşteri seçilmelidir.");
        if (string.IsNullOrWhiteSpace(n.Sart)) throw new ValidationException("Şart/talep metni zorunludur.");
        if (n.Sart.Length > 512) throw new ValidationException("Şart metni en çok 512 karakter olabilir.");
        if (n.BasTar is { } b && n.BitTar is { } t && t < b)
            throw new ValidationException("Geçerlilik bitişi başlangıçtan önce olamaz.");

        // Talep/karşılama GEÇMİŞ olaylardır — gelecek tarih veri girişi hatasıdır. 1 günlük tampon
        // saat dilimi kaynaklı yanlış reddi önler (TarihPolitikasi ile aynı yaklaşım).
        var limit = DateTimeOffset.UtcNow.AddDays(1);
        if (n.TalepTarihi is { } tal && tal > limit)
            throw new ValidationException("Talep tarihi gelecekte olamaz.");
        if (n.KarsilamaTarihi is { } profit && profit > limit)
            throw new ValidationException("Karşılama tarihi gelecekte olamaz.");
    }

    private static RezSartInput Normalize(RezSartInput input) => new()
    {
        MusteriId = input.MusteriId,
        Sart = (input.Sart ?? string.Empty).Trim(),
        Grup = TrimOrNull(input.Grup),
        BasTar = input.BasTar,
        BitTar = input.BitTar,
        TalepTarihi = input.TalepTarihi,
        KarsilamaTarihi = input.KarsilamaTarihi,
        TeslimEden = TrimOrNull(input.TeslimEden),
        ReservationId = input.ReservationId,
        QuotationId = input.QuotationId
    };

    private static string? TrimOrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void Apply(RezSart row, RezSartInput n)
    {
        row.MusteriId = n.MusteriId;
        row.Sart = n.Sart;
        row.Grup = n.Grup;
        row.BasTar = n.BasTar;
        row.BitTar = n.BitTar;
        row.KarsilamaTarihi = n.KarsilamaTarihi;
        row.TeslimEden = n.TeslimEden;
        row.ReservationId = n.ReservationId;
        row.QuotationId = n.QuotationId;
    }
}
