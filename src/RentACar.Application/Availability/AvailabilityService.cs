using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Availability;

/// <summary>
/// Araç müsaitlik arama: tarih aralığı (+ opsiyonel grup/şube) → kiralanabilir araçlar.
/// Rol bazlı şube kapsamı uygulanır (operatör yalnız kendi şubesi). Rezervasyon açmanın
/// doğal girişi.
/// </summary>
public sealed class AvailabilityService(IAvailabilityRepository repository, ICurrentUser currentUser)
{
    private readonly IAvailabilityRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public async Task<IReadOnlyList<Vehicle>> FindAvailableAsync(
        DateTimeOffset from, DateTimeOffset to, string? grup = null, string? sube = null, CancellationToken ct = default)
    {
        if (to <= from) throw new ValidationException("Bitiş tarihi başlangıçtan sonra olmalıdır.");

        // C3: kapsam FK-farkındalı zorlanır. MEVCUT SEMANTİK korunur: kapsamlı operatörün UI şube
        // seçimi YOK SAYILIR (kendi şubesi gösterilir — kesişim değil; AvailabilityTests kilitli davranış).
        var kapsam = BranchScope.EffectiveFilter(_currentUser);
        var effectiveSube = kapsam.Unrestricted
            ? (string.IsNullOrWhiteSpace(sube) ? null : sube.Trim())
            : null;
        var effectiveGrup = string.IsNullOrWhiteSpace(grup) ? null : grup.Trim();

        return await _repository.GetAvailableAsync(from, to, effectiveGrup, effectiveSube, kapsam, ct);
    }

    /// <summary>
    /// FAZ-19 — müsait araçların SON kullanım bilgisi (boştaki süre + son müşteri).
    /// Aracın kendi kapsam/izolasyonu üstteki müsaitlik sorgusunda uygulanmıştır; bu çağrı yalnız
    /// ZATEN gösterilen araçları zenginleştirir, yeni araç GETİRMEZ.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, SonKullanimRow>> SonKullanimAsync(
        IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct = default)
        => (await _repository.GetSonKullanimAsync(vehicleIds, ct)).ToDictionary(x => x.VehicleId);

    /// <summary>
    /// FAZ-48 — arama penceresi kurma: gün + saat girdilerinden [from, to). SAF fonksiyon; ekran
    /// formül taşımaz. Kurallar:
    /// <list type="bullet">
    /// <item>Başlangıç günü zorunlu; yoksa null (arama yapılmaz).</item>
    /// <item><paramref name="gun"/> verilirse (>0) bitiş = başlangıç + gün — bitiş tarihi alanı
    /// gerekmez ("3 günlük" araması). Gün 1 → tek günlük anlık durum sorgusu.</item>
    /// <item>Gün verilmediyse bitiş günü kullanılır.</item>
    /// <item>Saatler verilmezse 00:00 — ESKİ DAVRANIŞLA BİREBİR (regresyon çiti). Bitiş saati
    /// verilmezse başlangıç saati kullanılır (gün-sayısı modunda doğal karşılık).</item>
    /// <item>Offset DAİMA Zero: ekranın (ve fiyat motorunun) mevcut takvim-günü konvansiyonu.</item>
    /// </list>
    /// Sıralama doğrulaması (bitiş > başlangıç) BİLEREK burada değil — <see cref="FindAvailableAsync"/>
    /// zaten tek noktadan reddediyor, ikinci bir mesaj kaynağı üretilmez.
    /// </summary>
    public static (DateTimeOffset From, DateTimeOffset To)? Pencere(
        DateOnly? basGun, DateOnly? bitGun, int? gun, TimeOnly? basSaat, TimeOnly? bitSaat)
    {
        if (basGun is not DateOnly bg) return null;
        var bs = basSaat ?? TimeOnly.MinValue;
        var from = new DateTimeOffset(bg.ToDateTime(bs), TimeSpan.Zero);

        var ts = bitSaat ?? bs;
        DateTimeOffset to;
        if (gun is > 0)
            to = new DateTimeOffset(bg.AddDays(gun.Value).ToDateTime(ts), TimeSpan.Zero);
        else if (bitGun is DateOnly tg)
            to = new DateTimeOffset(tg.ToDateTime(ts), TimeSpan.Zero);
        else
            return null;

        return (from, to);
    }

    /// <summary>Boştaki gün sayısı (son dönüşten bugüne, kapsayıcı DEĞİL — aynı gün 0).</summary>
    public static int BostaGun(DateTimeOffset sonDonus, DateTimeOffset now)
    {
        var g = (now.UtcDateTime.Date - sonDonus.UtcDateTime.Date).Days;
        return g < 0 ? 0 : g;
    }
}
