using System.Globalization;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Periods;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Penalties;

/// <summary>
/// Trafik cezası: kayıt (tebliğ→vade, ÇOK KALEMLİ) + müşteriye yansıtma (Borç Cari / Alacak
/// Gelir, dengeli, idempotent) + KALEM BAZLI KISMİ ÖDEME (Borç Gider / Alacak Kasa-Banka) +
/// durum (İptal). Defter yazan her yol FinanceWrite ister.
///
/// <para><b>FAZ-60 / KARARLAR.md — SATIR BAZINDA KALAN:</b> ödeme daima TEK bir kaleme yazılır;
/// başlık toplamları (<c>Tutar</c>/<c>OdenenTutar</c>/<c>Kalan</c>) her ödemede kalemlerden
/// YENİDEN hesaplanır. "Hangi kalemin ne kadarı ödendi" ile toplam arasında ayrışma yapısal
/// olarak imkânsızdır.</para>
///
/// <para><b>Neden ödeme deftere yazar:</b> yansıtma (Borç Cari / Alacak Gelir) cezanın GELİR
/// tarafıdır; cezanın devlete ödenmesi MALİYET tarafıdır ve bugüne dek defterde hiç
/// görünmüyordu — yansıtılan her ceza kârı olduğundan yüksek gösteriyordu. İki kayıt aynı
/// cezada birbirini dengeler (net 0), çift-sayım ÜRETMEZ. Desen FAZ-14 MTV/Muayene kısmi
/// ödemesiyle birebir aynıdır.</para>
/// </summary>
public sealed class PenaltyService(IPenaltyRepository repository, ICurrentUser currentUser, IPeriodLockGuard periodLock)
{
    private readonly IPenaltyRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IPeriodLockGuard _lock = periodLock;

    public Task<IReadOnlyList<Penalty>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);
    public Task<Penalty?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    /// <summary>FAZ-60 — filtreli liste + çözülmüş bilgi kolonları (canlı ceza_listesi.aspx).</summary>
    public Task<IReadOnlyList<PenaltyRow>> ListRowsAsync(PenaltyFilter? filter = null, CancellationToken ct = default)
        => _repository.ListRowsAsync(filter, ct);

    public Task<IReadOnlyList<PenaltySatir>> ListSatirAsync(Guid penaltyId, CancellationToken ct = default)
        => _repository.ListSatirAsync(penaltyId, ct);

    public Task<IReadOnlyList<PenaltyOdeme>> ListOdemeAsync(Guid penaltyId, CancellationToken ct = default)
        => _repository.ListOdemeAsync(penaltyId, ct);

    /// <summary>Bir kiraya bağlı cezalar — kira formu "Ceza/Geçişler" alt-sekmesi (salt-okuma).</summary>
    public Task<IReadOnlyList<Penalty>> ListByRentalAsync(Guid rentalId, CancellationToken ct = default)
        => _repository.ListByRentalAsync(rentalId, ct);

    public async Task<Guid> CreateAsync(PenaltyInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial L1 (para yolu ayrı FinanceWrite)
        if (string.IsNullOrWhiteSpace(input.CezaTuru)) throw new ValidationException("Ceza türü zorunludur.");
        if (input.VadeGun < 0) throw new ValidationException("Vade günü negatif olamaz.");

        // KALEMLER: verilmediyse başlık tutarı TEK kalem olarak maddeleştirilir. Böylece
        // "satırsız ceza" özel durumu hiç doğmaz ve Tutar == Σ Satır.Tutar KOŞULSUZ geçerlidir.
        List<PenaltySatirInput> girdiler = input.Satirlar.Count > 0
            ? input.Satirlar
            : [new PenaltySatirInput { Tutar = input.Tutar, Sebep = input.Sebep }];
        if (girdiler.Count > 20) throw new ValidationException("Bir cezaya en fazla 20 kalem girilebilir.");

        var satirlar = new List<PenaltySatir>();
        var sira = 1;
        foreach (var g in girdiler)
        {
            if (g.Tutar <= 0) throw new ValidationException("Ceza kalemi tutarı pozitif olmalıdır.");
            // numeric(19,4) tavanı: guard'sız crafted POST DbUpdateException ile 500 üretir
            // (kullanıcıya anlamsız hata + log gürültüsü). Ödeme yolu ayrıca kalanla sınırlı.
            if (g.Tutar >= 1_000_000_000_000m)
                throw new ValidationException("Ceza kalemi tutarı makul sınırların dışında.");
            satirlar.Add(new PenaltySatir
            {
                Sira = sira++,
                Tutar = Yuvarla(g.Tutar),
                Sebep = Trim(g.Sebep),
                Odenen = 0m,
                Kalan = Yuvarla(g.Tutar)
            });
        }
        var toplam = Yuvarla(satirlar.Sum(s => s.Tutar));
        if (toplam <= 0) throw new ValidationException("Ceza tutarı pozitif olmalıdır.");

        var teblig = input.TebligTarihi ?? DateTimeOffset.UtcNow;
        var penalty = new Penalty
        {
            CezaTuru = input.CezaTuru.Trim(),
            TebligTarihi = teblig,
            VadeTarihi = teblig.AddDays(input.VadeGun),
            VehicleId = input.VehicleId,
            CariId = input.CariId,
            RentalId = input.RentalId,
            Tutar = toplam,
            // Başlık sebebi: tek kalemde kalemin sebebi, çok kalemde kullanıcının girdiği özet
            // (yoksa kalem sebepleri birleştirilir — liste ekranı boş görünmesin).
            Sebep = Trim(input.Sebep) ?? Ozet(satirlar),
            Durum = CezaDurum.Yeni,
            OdenenTutar = 0m,
            Kalan = toplam,
            Saat = Trim(input.Saat),
            Yer = Trim(input.Yer),
            CepTel = Trim(input.CepTel),
            MakbuzNo = Trim(input.MakbuzNo),
            IslemSube = Trim(input.IslemSube)
        };
        await _repository.CreateAsync(penalty, satirlar, ct);
        return penalty.Id;
    }

    /// <summary>Cezayı müşteriye yansıt (Borç Cari / Alacak Gelir). Yalnız Yeni ceza, cari zorunlu.</summary>
    public async Task<bool> YansitAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        var penalty = await _repository.FindAsync(id, ct) ?? throw new ValidationException("Ceza bulunamadı.");
        if (penalty.Durum != CezaDurum.Yeni)
            throw new ValidationException("Yalnız 'Yeni' durumundaki ceza yansıtılabilir.");
        if (penalty.CariId is null || penalty.CariId == Guid.Empty)
            throw new ValidationException("Yansıtma için müşteri (cari) seçilmelidir.");
        await _lock.EnsureOpenAsync(DateTimeOffset.UtcNow, ct); // dönem kilidi: yansıtma bugün tarihli postlanır

        return await _repository.ReflectAsync(id, p =>
        [
            new AccountLedgerEntry
            {
                EntryDateUtc = DateTimeOffset.UtcNow, AccountType = LedgerAccountType.Cari, AccountRef = p.CariId,
                Direction = LedgerDirection.Debit, Amount = new Money(p.Tutar, "TRY", 1m),
                SourceType = "Ceza", SourceId = p.Id, Description = $"Ceza {p.No}"
            },
            new AccountLedgerEntry
            {
                EntryDateUtc = DateTimeOffset.UtcNow, AccountType = LedgerAccountType.Gelir, AccountRef = null,
                Direction = LedgerDirection.Credit, Amount = new Money(p.Tutar, "TRY", 1m),
                SourceType = "Ceza", SourceId = p.Id, Description = $"Ceza {p.No}"
            }
        ], ct);
    }

    /// <summary>
    /// FAZ-60 — bir ceza KALEMİNE kısmi ödeme. Tutar null ise o kalemin kalanının tamamı ödenir.
    /// Dengeli defter çifti postlar: <c>Borç Gider(araç) / Alacak Kasa-Banka</c>.
    ///
    /// <para><b>Para birimi TRY ile SINIRLI</b> — trafik cezası devlete TRY ödenir. Döviz
    /// parametresi bilinçli olarak YOKTUR (FAZ-14'te "EUR geçilince deftere kur ile şişmiş
    /// tutar yazılıyordu" hatası bu şekilde yapısal olarak kapatıldı).</para>
    /// </summary>
    public async Task<CezaOdemeSonuc> KismiOdeAsync(Guid penaltyId, CezaOdemeInput odeme, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        ArgumentNullException.ThrowIfNull(odeme);
        if (odeme.SatirId == Guid.Empty) throw new ValidationException("Ödenecek ceza kalemi seçilmelidir.");
        if (odeme.Tutar is <= 0m) throw new ValidationException("Ödeme tutarı pozitif olmalıdır.");
        if (odeme.Hesap is not (LedgerAccountType.Kasa or LedgerAccountType.Banka))
            throw new ValidationException("Ödeme hesabı Kasa veya Banka olmalıdır.");

        TarihPolitikasi.ParaTarihi(odeme.Tarih, "Ceza ödeme");   // gelecek tarihli para kaydı yok
        var tarih = odeme.Tarih ?? DateTimeOffset.UtcNow;
        await _lock.EnsureOpenAsync(tarih, ct);                  // dönem kilidi: kapalı döneme ödeme yok

        return await _repository.PostOdemeAsync(penaltyId, odeme.SatirId, (ceza, satir, kalan, sira) =>
        {
            // Tutar verilmediyse kalemin KALANI (kilidin arkasında okunmuş gerçek değer).
            var tutar = Yuvarla(odeme.Tutar ?? kalan);
            var money = new Money(tutar, "TRY", 1m);
            var desc = $"Ceza ödeme {ceza.No} kalem {satir.Sira} (#{sira})";
            var satirOdeme = new PenaltyOdeme
            {
                PenaltyId = penaltyId, SatirId = satir.Id, Sira = sira, Tutar = tutar, Tarih = tarih,
                Hesap = odeme.Hesap,
                // DETERMİNİSTİK anahtar + MONOTON bileşen (sira). Değer/tarih anlık görüntüsü
                // kullanılsaydı aynı kaleme aynı tutarlı iki MEŞRU ödeme çakışırdı.
                Anahtar = string.Create(CultureInfo.InvariantCulture,
                    $"ceza:{penaltyId}:satir:{satir.Id}:odeme:{sira}"),
                IslemAnahtari = odeme.IslemAnahtari == Guid.Empty ? null : odeme.IslemAnahtari,
                KasaKodu = Trim(odeme.KasaKodu), HesapNo = Trim(odeme.HesapNo),
                MakbuzNo = Trim(odeme.MakbuzNo), IslemYapan = Trim(odeme.IslemYapan),
                Aciklama = Trim(odeme.Aciklama)
            };
            // SourceId = ÖDEMENİN id'si → kısmi unique index (TenantId, SourceType, SourceId,
            // Direction) her ödemeyi tekilleştirir (ödeme başına 1 borç + 1 alacak).
            IReadOnlyList<AccountLedgerEntry> entries =
            [
                new AccountLedgerEntry
                {
                    EntryDateUtc = tarih, AccountType = LedgerAccountType.Gider, AccountRef = ceza.VehicleId,
                    Direction = LedgerDirection.Debit, Amount = money,
                    SourceType = "CezaOdeme", SourceId = satirOdeme.Id, Description = desc
                },
                new AccountLedgerEntry
                {
                    EntryDateUtc = tarih, AccountType = odeme.Hesap, AccountRef = null,
                    Direction = LedgerDirection.Credit, Amount = money,
                    SourceType = "CezaOdeme", SourceId = satirOdeme.Id, Description = desc
                }
            ];
            return (satirOdeme, entries);
        }, ct);
    }

    /// <summary>
    /// Cezanın TAMAMINI öder (eski "Öde" düğmesinin karşılığı) — açık her kaleme kendi kalanı
    /// kadar ödeme yazar. Kalem başına AYRI dengeli defter çifti postlanır; toplamlar
    /// karıştırılmaz.
    ///
    /// <para><b>Yetki DEĞİŞTİ (bilinçli):</b> artık defter yazdığı için OperationsWrite değil
    /// FinanceWrite ister — yansıtmayla aynı kural (para yolu = FinanceWrite).</para>
    /// </summary>
    public async Task<bool> OdeAsync(Guid id, LedgerAccountType hesap = LedgerAccountType.Kasa,
        DateTimeOffset? tarih = null, string? makbuzNo = null, string? islemYapan = null,
        CancellationToken ct = default)
    {
        var satirlar = await _repository.ListSatirAsync(id, ct);
        if (satirlar.Count == 0) throw new ValidationException("Ceza bulunamadı.");
        var acik = satirlar.Where(s => s.Kalan > 0m).ToList();
        if (acik.Count == 0) throw new ValidationException("Ceza zaten ödendi.");

        foreach (var s in acik)
        {
            await KismiOdeAsync(id, new CezaOdemeInput
            {
                SatirId = s.Id, Tutar = null, Hesap = hesap, Tarih = tarih,
                MakbuzNo = makbuzNo, IslemYapan = islemYapan
            }, ct);
        }
        return true;
    }

    public Task<bool> IptalAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial L1
        return _repository.UpdateAsync(id, p =>
        {
            if (p.Durum == CezaDurum.Yansitildi) throw new ValidationException("Yansıtılmış ceza iptal edilemez (ters kayıt gerekir).");
            // FAZ-60 adversarial: ödemesi olan ceza da iptal EDİLEMEZ — defterde gider/kasa
            // hareketi var; iptal onları ters kayıtsız görünmez kılardı (sessiz para kaybı).
            if (p.OdenenTutar > 0m) throw new ValidationException("Ödemesi olan ceza iptal edilemez (ters kayıt gerekir).");
            p.Durum = CezaDurum.Iptal;
            p.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    private static decimal Yuvarla(decimal x) => decimal.Round(x, 4, MidpointRounding.AwayFromZero);
    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string? Ozet(IReadOnlyList<PenaltySatir> satirlar)
    {
        var parcalar = satirlar.Select(s => s.Sebep).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (parcalar.Count == 0) return null;
        var ozet = string.Join(" + ", parcalar);
        return ozet.Length <= 512 ? ozet : ozet[..512];
    }
}
