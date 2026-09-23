using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Kur;

/// <summary>
/// Kur sabitleme master iş mantığı: doğrulama + kod-başına tek satır (upsert) + CRUD. Yazma mali
/// yapılandırmadır → <see cref="Permission.FinanceWrite"/>. Tenant izolasyonu/audit alt katmanda otomatik.
/// Tarihler UTC'ye normalize edilir (timestamptz güvenliği).
/// </summary>
public sealed class SabitKurService(ISabitKurRepository repository, ICurrentUser currentUser)
{
    private readonly ISabitKurRepository _repo = repository;
    private readonly ICurrentUser _user = currentUser;

    public Task<IReadOnlyList<SabitKur>> ListAsync(CancellationToken ct = default) => _repo.ListAsync(ct);
    public Task<SabitKur?> GetAsync(Guid id, CancellationToken ct = default) => _repo.FindAsync(id, ct);

    /// <summary>Kod-başına tek sabit kur: varsa günceller, yoksa oluşturur.</summary>
    public async Task<Guid> UpsertAsync(SabitKurInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_user, Permission.FinanceWrite);
        var kod = KurService.NormalizeKod(input.Kod);
        if (kod.Length is < 2 or > 3) throw new ValidationException("Döviz kodu 2-3 harf olmalı.");
        if (kod == "TRY") throw new ValidationException("Baz para (TL) için sabit kur tanımlanmaz.");
        if (input.Kur <= 0) throw new ValidationException("Sabit kur 0'dan büyük olmalı.");
        // Pencere GÜN granülünde (BasTar/BitTar birer DATE): BasTar → UTC gün BAŞI, BitTar → UTC gün SONU.
        // Böylece date-picker girdisi son gün öğleden sonra da geçerli kalır (gün-DAHİL; adversarial Medium fix).
        var bas = input.BasTar is { } bs ? GunBasi(bs) : (DateTimeOffset?)null;
        var bit = input.BitTar is { } bt ? GunBasi(bt).AddDays(1).AddTicks(-1) : (DateTimeOffset?)null;
        if (bas is { } b && bit is { } e && e < b)
            throw new ValidationException("Bitiş tarihi başlangıçtan önce olamaz.");

        var mevcut = (await _repo.ListAsync(ct)).FirstOrDefault(x => x.Kod == kod);
        if (mevcut is null)
        {
            var s = new SabitKur { Kod = kod, Kur = input.Kur, BasTar = bas, BitTar = bit, Aktif = input.Aktif };
            await _repo.CreateAsync(s, ct);
            return s.Id;
        }
        mevcut.Kur = input.Kur;
        mevcut.BasTar = bas;
        mevcut.BitTar = bit;
        mevcut.Aktif = input.Aktif;
        await _repo.UpdateAsync(mevcut, ct);
        return mevcut.Id;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_user, Permission.FinanceWrite);
        return await _repo.DeleteAsync(id, ct);
    }

    /// <summary>F8.1a — satır sürümü (iyimser eşzamanlılık; <see cref="UpdateAsync"/> ile karşılaştırılır).</summary>
    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default) => _repo.GetVersionAsync(id, ct);

    /// <summary>F8.1a — YENİ sabit kur (<c>/api/ui</c>). Kod zaten tanımlıysa 400 (<c>kod</c>): mevcut satır
    /// sürümle (<see cref="UpdateAsync"/>) güncellenir — upsert burada bayat formun başka oturumun kurunu
    /// sessizce ezmesine yol açardı.</summary>
    public async Task<Guid> CreateAsync(SabitKurInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_user, Permission.FinanceWrite);
        var (kod, bas, bit) = Validate(input.Kod, input);
        if (await _repo.KodExistsAsync(kod, null, ct))
            throw new ValidationException($"'{kod}' için sabit kur zaten var; mevcut kaydı düzenleyin.", "kod");
        var s = new SabitKur { Kod = kod, Kur = input.Kur, BasTar = bas, BitTar = bit, Aktif = input.Aktif };
        await _repo.CreateAsync(s, ct); // yarışta unique index → 400 (repo)
        return s.Id;
    }

    /// <summary>F8.1a — tam değiştirme (kur, pencere, aktiflik; kod DEĞİŞMEZ). <paramref name="expectedVersion"/>
    /// kilit altında karşılaştırılır; farklıysa 409 <c>cakisma</c>. Satır yoksa false.</summary>
    public async Task<bool> UpdateAsync(Guid id, SabitKurInput input, string expectedVersion, CancellationToken ct = default)
    {
        PermissionGuard.Require(_user, Permission.FinanceWrite);
        var mevcut = await _repo.FindAsync(id, ct);
        if (mevcut is null) return false;
        var (_, bas, bit) = Validate(mevcut.Kod, input);
        return await _repo.UpdateAsync(id, expectedVersion, s =>
        {
            s.Kur = input.Kur;
            s.BasTar = bas;
            s.BitTar = bit;
            s.Aktif = input.Aktif;
        }, ct);
    }

    /// <summary><see cref="UpsertAsync"/> ile AYNI kurallar (kod, pozitif kur, gün-granülü pencere).</summary>
    private static (string Kod, DateTimeOffset? Bas, DateTimeOffset? Bit) Validate(string? rawKod, SabitKurInput input)
    {
        var kod = KurService.NormalizeKod(rawKod);
        if (kod.Length is < 2 or > 3) throw new ValidationException("Döviz kodu 2-3 harf olmalı.", "kod");
        if (kod == "TRY") throw new ValidationException("Baz para (TL) için sabit kur tanımlanmaz.", "kod");
        if (input.Kur <= 0) throw new ValidationException("Sabit kur 0'dan büyük olmalı.", "kur");
        var bas = input.BasTar is { } bs ? GunBasi(bs) : (DateTimeOffset?)null;
        var bit = input.BitTar is { } bt ? GunBasi(bt).AddDays(1).AddTicks(-1) : (DateTimeOffset?)null;
        if (bas is { } b && bit is { } e && e < b)
            throw new ValidationException("Bitiş tarihi başlangıçtan önce olamaz.", "bitTar");
        return (kod, bas, bit);
    }

    /// <summary>Verilen anın UTC takvim gününün başı (00:00Z). Pencere gün-granülü karşılaştırma için.</summary>
    private static DateTimeOffset GunBasi(DateTimeOffset d)
    {
        var u = d.UtcDateTime;
        return new DateTimeOffset(u.Year, u.Month, u.Day, 0, 0, 0, TimeSpan.Zero);
    }
}
