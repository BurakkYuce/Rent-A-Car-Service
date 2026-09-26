using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Crm;

/// <summary>
/// Müşteri anketi iş mantığı (roadmap C3): CRUD + Puan doğrulama (0-10). Yazma → OperationsWrite.
/// Tenant izolasyonu/audit alt katmanda.
/// </summary>
public sealed class SurveyService(
    ISurveyRepository repository, RentACar.Application.Bookings.IBookingRepository bookings, ICurrentUser currentUser,
    CrmScopeGuard scope)
{
    /// <summary>r317 M1: güncellemede mevcut kaydın ve hedefin şube kapsamı (her iki arayüz için tek yer).</summary>
    private async Task<bool> ScopedUpdateAsync(Guid id, AnketInput input, CancellationToken ct)
    {
        if (await _repository.FindAsync(id, ct) is not { } current) return false;
        await scope.RequireUpdateAsync(current.RentalId, current.CikisOfisi, input.RentalId, input.CikisOfisi, ct);
        return true;
    }

    private readonly ISurveyRepository _repository = repository;
    private readonly RentACar.Application.Bookings.IBookingRepository _bookings = bookings;
    private readonly ICurrentUser _currentUser = currentUser;

    /// <summary>
    /// FAZ-42 varsayılan soru seti. Soru METNİ cevapla birlikte saklandığı için bu liste
    /// değişse bile GEÇMİŞ anketler bozulmaz — yalnız yeni formun varsayılanıdır.
    /// </summary>
    public static readonly string[] DefaultQuestions =
    [
        "Araç teslim alırken temiz miydi?",
        "Araçta eksik/hasarlı bir şey var mıydı?",
        "Personelimiz ilgili miydi?",
        "İşlem süresinden memnun kaldınız mı?",
        "Fiyatlandırma açık ve anlaşılır mıydı?",
        "Ofisimizi kolay buldunuz mu?",
        "Bizi tekrar tercih eder misiniz?",
        "Eklemek istediğiniz bir görüş var mı?"
    ];

    public Task<IReadOnlyList<Anket>> ListAsync(CancellationToken ct = default) => _repository.ListAsync(ct);

    /// <summary>FAZ-42 filtreli liste.</summary>
    public Task<IReadOnlyList<Anket>> SearchAsync(AnketFilter? filter = null, CancellationToken ct = default)
        => filter is null ? _repository.ListAsync(ct) : _repository.ListAsync(filter, ct);

    public Task<Anket?> GetAsync(Guid id, CancellationToken ct = default) => _repository.FindAsync(id, ct);

    /// <summary>Anket + cevapları.</summary>
    public async Task<AnketDetay?> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        var a = await _repository.FindAsync(id, ct);
        return a is null ? null : new AnketDetay(a, await _repository.ListResponsesAsync(id, ct));
    }

    public async Task<Guid> CreateAsync(AnketInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        Validate(input);
        await scope.RequireTargetAsync(input.RentalId, input.CikisOfisi, creating: true, ct);
        var row = new Anket();
        await ApplyAsync(row, input, ct);
        await _repository.CreateWithAnswersAsync(row, Answers(input), ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, AnketInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (!await ScopedUpdateAsync(id, input, ct)) return false;
        Validate(input);
        // Snapshot çözümü (sözleşme okuması) transaction DIŞINDA yapılır; sonuç kopyaya yazılıp
        // transaction içinde uygulanır.
        var copy = new Anket();
        await ApplyAsync(copy, input, ct);
        return await _repository.UpdateWithResponseAsync(id, row =>
        {
            row.CariId = copy.CariId; row.Puan = copy.Puan; row.Yorum = copy.Yorum;
            row.Tarih = copy.Tarih; row.Kaynak = copy.Kaynak;
            row.RentalId = copy.RentalId; row.AnketTuru = copy.AnketTuru;
            row.Durum = copy.Durum; row.CikisOfisi = copy.CikisOfisi;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, Answers(input), ct);
    }

    /// <summary>F7.1 — tam değiştirme, iyimser eşzamanlılıkla (satır kilidi altında sürüm; farklı → 409).</summary>
    public async Task<bool> UpdateAsync(Guid id, AnketInput input, string expectedVersion, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (!await ScopedUpdateAsync(id, input, ct)) return false;
        Validate(input);
        var copy = new Anket();
        await ApplyAsync(copy, input, ct);
        return await _repository.UpdateWithAnswersAsync(id, expectedVersion, row =>
        {
            row.CariId = copy.CariId; row.Puan = copy.Puan; row.Yorum = copy.Yorum;
            row.Tarih = copy.Tarih; row.Kaynak = copy.Kaynak;
            row.RentalId = copy.RentalId; row.AnketTuru = copy.AnketTuru;
            row.Durum = copy.Durum; row.CikisOfisi = copy.CikisOfisi;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, Answers(input), ct);
    }

    /// <summary>F7.1 — satır sürümü (PUT'un <c>surum</c>'u); yok/başka kiracı → null.</summary>
    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default) => _repository.GetVersionAsync(id, ct);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (await _repository.FindAsync(id, ct) is not { } current) return false;
        await scope.RequireRecordAsync(current.RentalId, current.CikisOfisi, ct); // r317 M1
        return await _repository.DeleteAsync(id, ct);
    }

    private static void Validate(AnketInput n)
    {
        if (n.Puan is < 0 or > 10) throw new ValidationException("Puan 0 ile 10 arasında olmalıdır.");
        // Gelecek tarihli anket olmaz (tarih politikasıyla aynı yön).
        DatePolicy.MoneyDate(n.Tarih, "Anket");
        // Aynı soru sırası iki kez gönderilirse DB unique'e takılırdı; burada net mesajla reddet.
        var numbers = n.Cevaplar.Where(c => !string.IsNullOrWhiteSpace(c.Soru)).Select(c => c.SoruNo).ToList();
        if (numbers.Count != numbers.Distinct().Count())
            throw new ValidationException("Aynı soru sırası birden çok kez gönderildi.");
    }

    /// <summary>Cevap satırları — SORUSU BOŞ olan satır atılır (boş form satırı kayıt üretmesin).</summary>
    private static List<AnketCevap> Answers(AnketInput n)
        => n.Cevaplar
            .Where(c => !string.IsNullOrWhiteSpace(c.Soru))
            .OrderBy(c => c.SoruNo)
            .Select(c => new AnketCevap
            {
                SoruNo = c.SoruNo,
                Soru = c.Soru!.Trim(),
                Cevap = string.IsNullOrWhiteSpace(c.Cevap) ? null : c.Cevap.Trim(),
                Aciklama = string.IsNullOrWhiteSpace(c.Aciklama) ? null : c.Aciklama.Trim()
            })
            .ToList();

    private async Task ApplyAsync(Anket row, AnketInput n, CancellationToken ct)
    {
        row.RentalId = n.RentalId;
        row.AnketTuru = n.AnketTuru;
        row.Durum = n.Durum;
        // Çıkış ofisi SNAPSHOT: boşsa sözleşmeden kopyalanır. Kullanıcı yazmışsa DEĞERİ KORUNUR.
        var office = string.IsNullOrWhiteSpace(n.CikisOfisi) ? null : n.CikisOfisi.Trim();
        if (n.RentalId is Guid rid && rid != Guid.Empty)
        {
            var rental = await _bookings.FindRentalAsync(rid, ct)
                ?? throw new ValidationException("Kira sözleşmesi bulunamadı.");
            office ??= rental.CikisOfisi;
        }
        row.CikisOfisi = office;

        row.CariId = n.CariId;
        row.Puan = n.Puan;
        row.Yorum = string.IsNullOrWhiteSpace(n.Yorum) ? null : n.Yorum.Trim();
        row.Tarih = n.Tarih ?? DateTimeOffset.UtcNow;
        row.Kaynak = string.IsNullOrWhiteSpace(n.Kaynak) ? null : n.Kaynak.Trim();
    }
}
