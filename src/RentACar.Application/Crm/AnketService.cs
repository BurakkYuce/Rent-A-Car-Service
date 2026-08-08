using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Crm;

/// <summary>
/// Müşteri anketi iş mantığı (roadmap C3): CRUD + Puan doğrulama (0-10). Yazma → OperationsWrite.
/// Tenant izolasyonu/audit alt katmanda.
/// </summary>
public sealed class AnketService(
    IAnketRepository repository, RentACar.Application.Bookings.IBookingRepository bookings, ICurrentUser currentUser)
{
    private readonly IAnketRepository _repository = repository;
    private readonly RentACar.Application.Bookings.IBookingRepository _bookings = bookings;
    private readonly ICurrentUser _currentUser = currentUser;

    /// <summary>
    /// FAZ-42 varsayılan soru seti. Soru METNİ cevapla birlikte saklandığı için bu liste
    /// değişse bile GEÇMİŞ anketler bozulmaz — yalnız yeni formun varsayılanıdır.
    /// </summary>
    public static readonly string[] VarsayilanSorular =
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
    public Task<IReadOnlyList<Anket>> SearchAsync(AnketFilter? filtre = null, CancellationToken ct = default)
        => filtre is null ? _repository.ListAsync(ct) : _repository.ListAsync(filtre, ct);

    public Task<Anket?> GetAsync(Guid id, CancellationToken ct = default) => _repository.FindAsync(id, ct);

    /// <summary>Anket + cevapları.</summary>
    public async Task<AnketDetay?> GetDetayAsync(Guid id, CancellationToken ct = default)
    {
        var a = await _repository.FindAsync(id, ct);
        return a is null ? null : new AnketDetay(a, await _repository.ListCevapAsync(id, ct));
    }

    public async Task<Guid> CreateAsync(AnketInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        Validate(input);
        var row = new Anket();
        await ApplyAsync(row, input, ct);
        await _repository.CreateWithCevapAsync(row, Cevaplar(input), ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, AnketInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        Validate(input);
        // Snapshot çözümü (sözleşme okuması) transaction DIŞINDA yapılır; sonuç kopyaya yazılıp
        // transaction içinde uygulanır.
        var kopya = new Anket();
        await ApplyAsync(kopya, input, ct);
        return await _repository.UpdateWithCevapAsync(id, row =>
        {
            row.CariId = kopya.CariId; row.Puan = kopya.Puan; row.Yorum = kopya.Yorum;
            row.Tarih = kopya.Tarih; row.Kaynak = kopya.Kaynak;
            row.RentalId = kopya.RentalId; row.AnketTuru = kopya.AnketTuru;
            row.Durum = kopya.Durum; row.CikisOfisi = kopya.CikisOfisi;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, Cevaplar(input), ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(AnketInput n)
    {
        if (n.Puan is < 0 or > 10) throw new ValidationException("Puan 0 ile 10 arasında olmalıdır.");
        // Gelecek tarihli anket olmaz (tarih politikasıyla aynı yön).
        TarihPolitikasi.ParaTarihi(n.Tarih, "Anket");
        // Aynı soru sırası iki kez gönderilirse DB unique'e takılırdı; burada net mesajla reddet.
        var nolar = n.Cevaplar.Where(c => !string.IsNullOrWhiteSpace(c.Soru)).Select(c => c.SoruNo).ToList();
        if (nolar.Count != nolar.Distinct().Count())
            throw new ValidationException("Aynı soru sırası birden çok kez gönderildi.");
    }

    /// <summary>Cevap satırları — SORUSU BOŞ olan satır atılır (boş form satırı kayıt üretmesin).</summary>
    private static List<AnketCevap> Cevaplar(AnketInput n)
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
        var ofis = string.IsNullOrWhiteSpace(n.CikisOfisi) ? null : n.CikisOfisi.Trim();
        if (n.RentalId is Guid rid && rid != Guid.Empty)
        {
            var kira = await _bookings.FindRentalAsync(rid, ct)
                ?? throw new ValidationException("Kira sözleşmesi bulunamadı.");
            ofis ??= kira.CikisOfisi;
        }
        row.CikisOfisi = ofis;

        row.CariId = n.CariId;
        row.Puan = n.Puan;
        row.Yorum = string.IsNullOrWhiteSpace(n.Yorum) ? null : n.Yorum.Trim();
        row.Tarih = n.Tarih ?? DateTimeOffset.UtcNow;
        row.Kaynak = string.IsNullOrWhiteSpace(n.Kaynak) ? null : n.Kaynak.Trim();
    }
}
