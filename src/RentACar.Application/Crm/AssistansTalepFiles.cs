using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Crm;

/// <summary>Assistans (yol yardım) talebi kalıcılığı — FAZ-44.</summary>
public interface IAssistansTalepRepository
{
    Task<IReadOnlyList<AssistansTalep>> SearchAsync(AssistansFilter filtre, CancellationToken ct = default);
    Task<AssistansTalep?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(AssistansTalep row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<AssistansTalep> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>F7.1 — satır kilidi + iyimser sürüm karşılaştırması (sürüm farklı → 409, hiçbir şey yazılmaz).</summary>
    Task<bool> UpdateAsync(Guid id, string expectedVersion, Action<AssistansTalep> apply, CancellationToken ct = default);

    /// <summary>F7.1 — satır sürümü (opak); yok/başka kiracı → null.</summary>
    Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Assistans liste filtresi. Boş alan = kısıt yok.</summary>
public sealed class AssistansFilter
{
    /// <summary>Plaka — kısmi eşleşme; sorgu öncesi normalize edilir (boşluk/büyük harf).</summary>
    public string? Plaka { get; set; }
    public DateTimeOffset? TarihMin { get; set; }
    public DateTimeOffset? TarihMax { get; set; }
    /// <summary>Mesaj/sebep/ad/telefon içinde geçen metin.</summary>
    public string? Ara { get; set; }
    /// <summary>null = hepsi; false = yalnız açık, true = yalnız kapalı.</summary>
    public bool? Kapandi { get; set; }
    public bool? YedekLastikMi { get; set; }
    /// <summary>true = yalnız hareket EDEMEYEN araçlar (çekici gerekenler).</summary>
    public bool? HareketEdemiyor { get; set; }
}

/// <summary>Assistans talebi giriş modeli.</summary>
public sealed class AssistansInput
{
    public Guid? RentalId { get; set; }
    /// <summary>Boş bırakılırsa sözleşmeden doldurulur; dolu ise KULLANICI DEĞERİ kazanır.</summary>
    public string? Plaka { get; set; }
    public string? AdSoyad { get; set; }
    public string? CepTel { get; set; }
    public DateTimeOffset? Zaman { get; set; }
    public string? Mesaj { get; set; }
    public string? Sebep { get; set; }
    public bool YedekLastikMi { get; set; }
    public bool AracHareketMi { get; set; }
    public bool Kapandi { get; set; }
    public string? Cozum { get; set; }
    /// <summary>#295 L1: ad soyad bilinçli temizlendi → boş kalır, sözleşmeden yeniden DOLDURULMAZ.</summary>
    public bool ClearContactName { get; set; }
    /// <summary>#295 L1: telefon bilinçli temizlendi → boş kalır, sözleşmeden yeniden DOLDURULMAZ.</summary>
    public bool ClearContactPhone { get; set; }
}

/// <summary>
/// Assistans talebi iş mantığı — FAZ-44. Yazma <see cref="Permission.OperationsWrite"/>.
///
/// <para><b>Snapshot doldurma:</b> sözleşme seçilince plaka/ad/telefon SÖZLEŞMEDEN kopyalanır ama
/// KULLANICI DEĞERİ ÖNCELİKLİDİR — çağrıyı yapan kişi sözleşmedeki müşteri olmayabilir (ikinci
/// sürücü, yoldaki bir yakını). Otomatik doldurma kullanıcının yazdığını ezseydi çağrı merkezinin
/// elindeki tek doğru numara kaybolurdu.</para>
/// </summary>
public sealed class AssistansTalepService(
    IAssistansTalepRepository repository, IBookingRepository bookings,
    ICustomerRepository customers, IVehicleRepository vehicles, ICurrentUser currentUser)
{
    private readonly IAssistansTalepRepository _repository = repository;
    private readonly IBookingRepository _bookings = bookings;
    private readonly ICustomerRepository _customers = customers;
    private readonly IVehicleRepository _vehicles = vehicles;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<AssistansTalep>> SearchAsync(AssistansFilter? filtre = null, CancellationToken ct = default)
        => _repository.SearchAsync(filtre ?? new AssistansFilter(), ct);

    public Task<AssistansTalep?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(AssistansInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var row = new AssistansTalep();
        await UygulaAsync(row, input, ct);
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, AssistansInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var mevcut = await _repository.FindAsync(id, ct);
        if (mevcut is null) return false;

        var kopya = new AssistansTalep { Id = mevcut.Id };
        await UygulaAsync(kopya, input, ct);

        return await _repository.UpdateAsync(id, r =>
        {
            r.RentalId = kopya.RentalId;
            r.Plaka = kopya.Plaka; r.AdSoyad = kopya.AdSoyad; r.CepTel = kopya.CepTel;
            r.Zaman = kopya.Zaman; r.Mesaj = kopya.Mesaj; r.Sebep = kopya.Sebep;
            r.YedekLastikMi = kopya.YedekLastikMi; r.AracHareketMi = kopya.AracHareketMi;
            r.Kapandi = kopya.Kapandi; r.Cozum = kopya.Cozum;
        }, ct);
    }

    /// <summary>F7.1 — tam değiştirme, iyimser eşzamanlılıkla (satır kilidi altında sürüm; farklı → 409).</summary>
    public async Task<bool> UpdateAsync(Guid id, AssistansInput input, string expectedVersion, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var copy = new AssistansTalep { Id = id };
        await UygulaAsync(copy, input, ct);
        return await _repository.UpdateAsync(id, expectedVersion, r =>
        {
            r.RentalId = copy.RentalId;
            r.Plaka = copy.Plaka; r.AdSoyad = copy.AdSoyad; r.CepTel = copy.CepTel;
            r.Zaman = copy.Zaman; r.Mesaj = copy.Mesaj; r.Sebep = copy.Sebep;
            r.YedekLastikMi = copy.YedekLastikMi; r.AracHareketMi = copy.AracHareketMi;
            r.Kapandi = copy.Kapandi; r.Cozum = copy.Cozum;
        }, ct);
    }

    /// <summary>F7.1 — satır sürümü (PUT'un <c>surum</c>'u); yok/başka kiracı → null.</summary>
    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default) => _repository.GetVersionAsync(id, ct);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return await _repository.DeleteAsync(id, ct);
    }

    /// <summary>Doğrulama + snapshot doldurma. Yeni alan eklenirse UpdateAsync'e de eklenmeli.</summary>
    private async Task UygulaAsync(AssistansTalep row, AssistansInput input, CancellationToken ct)
    {
        var mesaj = (input.Mesaj ?? "").Trim();
        if (string.IsNullOrWhiteSpace(mesaj)) throw new ValidationException("Mesaj zorunludur.");

        // Gelecek tarihli olay tutanağı olmaz (tarih politikası simetrisi).
        TarihPolitikasi.ParaTarihi(input.Zaman, "Assistans talebi");

        string? plaka = Trim(input.Plaka);
        string? ad = Trim(input.AdSoyad);
        string? tel = Trim(input.CepTel);

        if (input.RentalId is Guid rid && rid != Guid.Empty)
        {
            var kira = await _bookings.FindRentalAsync(rid, ct)
                ?? throw new ValidationException("Kira sözleşmesi bulunamadı.");
            row.RentalId = rid;

            // KULLANICI DEĞERİ ÖNCELİKLİ — yalnız BOŞ alanlar sözleşmeden doldurulur.
            plaka ??= (await _vehicles.FindAsync(kira.VehicleId, ct))?.Plaka;
            // #295 L2: KVKK ile anonimleştirilmiş müşterinin adı/telefonu snapshot'a KOPYALANMAZ (ekranda gizli olacak bir
            // değeri çoğaltmak anonimleştirmeyi delerdi); bilinçli temizlenen alan da yeniden doldurulmaz.
            var fillName = ad is null && !input.ClearContactName;
            var fillPhone = tel is null && !input.ClearContactPhone;
            if (fillName || fillPhone)
            {
                var m = await _customers.FindAsync(kira.MusteriId, ct);
                if (fillName && m is { AnonimAd: false }) ad = m.DisplayName;
                if (fillPhone && m is { AnonimTelefon: false }) tel = m.CepTel;
            }
        }
        else row.RentalId = null;

        row.Plaka = plaka is null ? null : PlakaNormalize(plaka);
        row.AdSoyad = ad;
        row.CepTel = tel;
        row.Zaman = input.Zaman ?? DateTimeOffset.UtcNow;
        row.Mesaj = mesaj;
        row.Sebep = Trim(input.Sebep);
        row.YedekLastikMi = input.YedekLastikMi;
        row.AracHareketMi = input.AracHareketMi;
        row.Kapandi = input.Kapandi;
        row.Cozum = Trim(input.Cozum);
    }

    /// <summary>Plaka DB'de normalize saklanır (büyük harf, boşluksuz) — arama da öyle normalize eder.</summary>
    public static string PlakaNormalize(string plaka)
        => new(plaka.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
