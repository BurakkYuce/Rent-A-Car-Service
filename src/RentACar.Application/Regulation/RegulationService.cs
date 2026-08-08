using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Periods;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Regulation;

/// <summary>
/// Kısmi ödeme girdisi (FAZ-14). <see cref="Tutar"/> null ise kalan bakiyenin TAMAMI ödenir —
/// eski tek-seferde-kapat davranışı böylece imza değişmeden korunur.
/// </summary>
public sealed class RegulasyonOdemeInput
{
    public decimal? Tutar { get; set; }
    public string? EvrakNo { get; set; }
    public string? IslemYapan { get; set; }
    public string? Aciklama { get; set; }
    public string? KasaKodu { get; set; }
    public string? HesapNo { get; set; }
    /// <summary>Çift-submit koruması — form her render'da yeni GUID basar.</summary>
    public Guid? IslemAnahtari { get; set; }
}

/// <summary>Sigorta/MTV/Muayene CRUD + doğrulama (araç zorunlu, tarih tutarlılığı) + MTV ödeme→defter (J1).</summary>
public sealed class RegulationService(IRegulationRepository repository, ICurrentUser currentUser, IPeriodLockGuard periodLock,
    RentACar.Application.Kur.KurCozucu kurCozucu)
{
    private readonly IRegulationRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IPeriodLockGuard _lock = periodLock;
    private readonly RentACar.Application.Kur.KurCozucu _kurCozucu = kurCozucu;

    /// <summary>Sigorta poliçesi para birimi beyaz-listesi (Currency kolonu HasMaxLength(3)).</summary>
    private static readonly HashSet<string> AllowedCurrencies = new(StringComparer.Ordinal) { "TRY", "EUR", "USD", "GBP" };

    public Task<IReadOnlyList<InsurancePolicy>> ListInsuranceAsync(CancellationToken ct = default)
        => _repository.ListInsuranceAsync(ct);
    public Task<IReadOnlyList<MtvRecord>> ListMtvAsync(CancellationToken ct = default)
        => _repository.ListMtvAsync(ct);
    public Task<IReadOnlyList<InspectionRecord>> ListInspectionAsync(CancellationToken ct = default)
        => _repository.ListInspectionAsync(ct);

    public async Task<Guid> AddInsuranceAsync(
        Guid vehicleId, InsuranceType tip, DateTimeOffset baslangic, DateTimeOffset bitis,
        decimal prim, string? policeNo, string? firma, string? acenta,
        string? doviz = "TRY", CancellationToken ct = default)
    {
        RequireVehicle(vehicleId);
        if (bitis <= baslangic) throw new ValidationException("Bitiş başlangıçtan sonra olmalıdır.");
        if (prim < 0) throw new ValidationException("Prim negatif olamaz.");
        // Çok-döviz: ithal araç poliçesi EUR/USD olabilir → Currency create'te set edilir; ödemede
        // (SigortaOdeAsync) kur ile baz tutara çevrilir. Boş → TRY (yerel poliçe). Beyaz-liste dışı
        // reddedilir (adversarial Low: crafted POST'la çöp/uzun döviz → 3-hane kolon DbUpdateException).
        var currency = string.IsNullOrWhiteSpace(doviz) ? "TRY" : doviz.Trim().ToUpperInvariant();
        if (!AllowedCurrencies.Contains(currency))
            throw new ValidationException($"Geçersiz para birimi: {currency}. İzinli: {string.Join(", ", AllowedCurrencies)}.");
        var p = new InsurancePolicy
        {
            VehicleId = vehicleId, Tip = tip, Baslangic = baslangic, Bitis = bitis,
            Prim = prim, Currency = currency, PoliceNo = Trim(policeNo), Firma = Trim(firma), Acenta = Trim(acenta)
        };
        await _repository.AddInsuranceAsync(p, ct);
        return p.Id;
    }

    public async Task<Guid> AddMtvAsync(
        Guid vehicleId, string donem, decimal tutar, DateTimeOffset vade,
        string? aciklama = null, CancellationToken ct = default)
    {
        RequireVehicle(vehicleId);
        if (string.IsNullOrWhiteSpace(donem)) throw new ValidationException("Dönem zorunludur.");
        if (tutar < 0) throw new ValidationException("Tutar negatif olamaz.");
        // Kalan = Tutar (FAZ-14): kısmi ödemeler bunu düşürür. Kaydın kendisi 0 tutarlıysa
        // Kalan da 0 olur ve ödeme "bakiye yok" ile reddedilir (doğru).
        var m = new MtvRecord { VehicleId = vehicleId, Donem = donem.Trim(), Tutar = tutar, Kalan = tutar, Vade = vade, Aciklama = Trim(aciklama) };
        await _repository.AddMtvAsync(m, ct);
        return m.Id;
    }

    public async Task<Guid> AddInspectionAsync(
        Guid vehicleId, DateTimeOffset muayeneTarihi, DateTimeOffset bitis, decimal ucret,
        int? islemKm = null, string? aciklama = null, CancellationToken ct = default)
    {
        RequireVehicle(vehicleId);
        if (bitis <= muayeneTarihi) throw new ValidationException("Bitiş muayene tarihinden sonra olmalıdır.");
        if (ucret < 0) throw new ValidationException("Ücret negatif olamaz.");
        // Kalan = Ucret (FAZ-14). Ceza ödeme anında girilir ve o an borcu artırır — bu yüzden
        // açılışta kalana DAHİL EDİLMEZ (henüz doğmamış bir borç).
        if (islemKm is < 0) throw new ValidationException("İşlem KM negatif olamaz.");
        var i = new InspectionRecord
        {
            VehicleId = vehicleId, MuayeneTarihi = muayeneTarihi, Bitis = bitis, Ucret = ucret,
            Kalan = ucret, IslemKm = islemKm, Aciklama = Trim(aciklama)
        };
        await _repository.AddInspectionAsync(i, ct);
        return i.Id;
    }

    /// <summary>
    /// MTV ödeme (roadmap J1 + FAZ-14 KISMİ ÖDEME). Kaydın <c>Kalan</c> bakiyesinden
    /// <c>odeme.Tutar</c> kadar (null → kalanın TAMAMI, eski davranış) düşülür; o adım KENDİ
    /// dengeli defter çiftini postlar (Borç Gider / Alacak Kasa-Banka). Kalan 0'a inince
    /// <c>Odendi=true</c>. FinanceWrite + dönem kilidi.
    ///
    /// <para><b>Para birimi TRY ile sınırlandı.</b> MTV kaydında döviz alanı YOK — tutar
    /// yapıca TRY. Eski imza <c>doviz</c>/<c>kur</c> kabul ediyordu ve "EUR" geçilince 1000 TL'lik
    /// MTV deftere 1000 EUR × kur olarak yazılıyordu (sessiz şişme). Artık TRY dışı REDDEDİLİR.
    /// Kısmi ödemede bu zaten zorunlu: <c>Kalan</c> TRY iken ondan EUR düşülemez.</para>
    /// </summary>
    public async Task<RegulasyonOdemeSonuc> MtvOdeAsync(Guid mtvId, LedgerAccountType hesap,
        DateTimeOffset? odemeTarih = null, string? doviz = "TRY", decimal? kur = null,
        RegulasyonOdemeInput? odeme = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        HesapKontrol(hesap);
        var paraBirimi = ParaBirimiKontrol(doviz, "MTV");

        var rec = await _repository.FindMtvAsync(mtvId, ct) ?? throw new ValidationException("MTV kaydı bulunamadı.");
        if (rec.Odendi) throw new ValidationException("MTV zaten ödendi.");
        if (rec.Tutar <= 0m) throw new ValidationException("MTV tutarı pozitif olmalıdır.");

        // Adversarial H1: ödeme tarihi artık FORMDAN geliyor → gelecek tarih reddi ŞART. Dönem
        // kilidi bunu yakalamaz (yalnız kapanış tarihine kadarını reddeder, gelecek serbest);
        // guard'sız 2099 tarihli 1000 TL gider deftere düşer ve hiçbir dönemde mutabık olmaz.
        TarihPolitikasi.ParaTarihi(odemeTarih, "MTV ödeme");   // para-yolu simetrisi (AracKredi dersi)
        var tarih = odemeTarih ?? DateTimeOffset.UtcNow;
        await _lock.EnsureOpenAsync(tarih, ct); // dönem kilidi: kapalı döneme MTV ödemesi YOK
        var cozulenKur = await _kurCozucu.CozAsync(paraBirimi, kur, tarih, ct);
        var g = odeme ?? new RegulasyonOdemeInput();
        AnahtarKontrol(g);

        return await _repository.PostMtvOdemeAsync(mtvId, (kalan, sira) =>
        {
            var tutar = TutarKontrol(g.Tutar, kalan, "MTV");
            var money = new Money(tutar, paraBirimi, cozulenKur);
            var desc = $"MTV ödeme {rec.Donem} (#{sira})";
            var satir = new MtvOdeme
            {
                MtvId = mtvId, Sira = sira, Tutar = tutar, Tarih = tarih, Hesap = hesap,
                KasaKodu = Trim(g.KasaKodu), HesapNo = Trim(g.HesapNo), EvrakNo = Trim(g.EvrakNo),
                IslemYapan = Trim(g.IslemYapan), Aciklama = Trim(g.Aciklama),
                IslemAnahtari = Anahtar(g.IslemAnahtari)
            };
            // SourceId = ÖDEMENİN id'si (kaydınki değil) → mevcut kısmi unique index
            // (TenantId, SourceType, SourceId, Direction) değişmeden her ödemeyi tekilleştirir.
            IReadOnlyList<AccountLedgerEntry> entries =
            [
                new AccountLedgerEntry { EntryDateUtc = tarih, AccountType = LedgerAccountType.Gider, AccountRef = rec.VehicleId,
                    Direction = LedgerDirection.Debit, Amount = money, SourceType = "MtvOdeme", SourceId = satir.Id, Description = desc },
                new AccountLedgerEntry { EntryDateUtc = tarih, AccountType = hesap, AccountRef = null,
                    Direction = LedgerDirection.Credit, Amount = money, SourceType = "MtvOdeme", SourceId = satir.Id, Description = desc }
            ];
            return (satir, entries);
        }, ct);
    }

    /// <summary>Bir MTV kaydının ödeme geçmişi (sıraya göre).</summary>
    public Task<IReadOnlyList<MtvOdeme>> ListMtvOdemeAsync(Guid mtvId, CancellationToken ct = default)
        => _repository.ListMtvOdemeAsync(mtvId, ct);

    /// <summary>Adversarial M2/L9 — TÜM MTV ödemeleri tek sorguda (liste sayfası kayıt başına
    /// sorgu atmasın ve kapanan kaydın geçmişi ekrandan KAYBOLMASIN).</summary>
    public Task<IReadOnlyList<MtvOdeme>> ListMtvOdemeHepsiAsync(CancellationToken ct = default)
        => _repository.ListMtvOdemeHepsiAsync(ct);

    /// <summary>
    /// Muayene ödeme (roadmap J2 + FAZ-14 KISMİ ÖDEME). Ödemede girilen <c>Ceza</c> BORCU ARTIRIR
    /// (<c>Kalan = Kalan + Ceza − Tutar</c>) — kendiliğinden ödenmiş sayılmaz. Tutar null ise
    /// "kalan + bu ceza"nın tamamı ödenir (eski tek-seferde-kapat davranışının karşılığı).
    /// Para birimi TRY ile sınırlı (bkz. <see cref="MtvOdeAsync"/>).
    /// </summary>
    public async Task<RegulasyonOdemeSonuc> MuayeneOdeAsync(Guid inspectionId, LedgerAccountType hesap,
        decimal ceza = 0m, DateTimeOffset? odemeTarih = null, string? doviz = "TRY", decimal? kur = null,
        RegulasyonOdemeInput? odeme = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        HesapKontrol(hesap);
        if (ceza < 0m) throw new ValidationException("Ceza negatif olamaz.");
        var paraBirimi = ParaBirimiKontrol(doviz, "Muayene");

        var rec = await _repository.FindInspectionAsync(inspectionId, ct) ?? throw new ValidationException("Muayene kaydı bulunamadı.");
        if (rec.Odendi) throw new ValidationException("Muayene zaten ödendi.");

        TarihPolitikasi.ParaTarihi(odemeTarih, "Muayene ödeme");   // adversarial H1
        var tarih = odemeTarih ?? DateTimeOffset.UtcNow;
        await _lock.EnsureOpenAsync(tarih, ct); // dönem kilidi
        var cozulenKur = await _kurCozucu.CozAsync(paraBirimi, kur, tarih, ct);
        var g = odeme ?? new RegulasyonOdemeInput();
        AnahtarKontrol(g);

        return await _repository.PostMuayeneOdemeAsync(inspectionId, (kalan, sira) =>
        {
            // Ceza önce borcu büyütür; ödenebilir tavan bu yüzden kalan + ceza.
            var tavan = kalan + ceza;
            var tutar = TutarKontrol(g.Tutar, tavan, "Muayene");
            var money = new Money(tutar, paraBirimi, cozulenKur);
            var desc = $"Muayene ödeme {rec.MuayeneTarihi.LocalDateTime:dd.MM.yyyy} (#{sira})"
                + (ceza > 0m ? $" (+ceza {ceza})" : "");
            var satir = new MuayeneOdeme
            {
                InspectionId = inspectionId, Sira = sira, Tutar = tutar, Ceza = ceza, Tarih = tarih, Hesap = hesap,
                KasaKodu = Trim(g.KasaKodu), HesapNo = Trim(g.HesapNo), EvrakNo = Trim(g.EvrakNo),
                IslemYapan = Trim(g.IslemYapan), Aciklama = Trim(g.Aciklama),
                IslemAnahtari = Anahtar(g.IslemAnahtari)
            };
            IReadOnlyList<AccountLedgerEntry> entries =
            [
                new AccountLedgerEntry { EntryDateUtc = tarih, AccountType = LedgerAccountType.Gider, AccountRef = rec.VehicleId,
                    Direction = LedgerDirection.Debit, Amount = money, SourceType = "MuayeneOdeme", SourceId = satir.Id, Description = desc },
                new AccountLedgerEntry { EntryDateUtc = tarih, AccountType = hesap, AccountRef = null,
                    Direction = LedgerDirection.Credit, Amount = money, SourceType = "MuayeneOdeme", SourceId = satir.Id, Description = desc }
            ];
            return (satir, entries);
        }, ct);
    }

    /// <summary>Bir muayene kaydının ödeme geçmişi (sıraya göre).</summary>
    public Task<IReadOnlyList<MuayeneOdeme>> ListMuayeneOdemeAsync(Guid inspectionId, CancellationToken ct = default)
        => _repository.ListMuayeneOdemeAsync(inspectionId, ct);

    /// <summary>Adversarial M2/L9 — tüm muayene ödemeleri tek sorguda.</summary>
    public Task<IReadOnlyList<MuayeneOdeme>> ListMuayeneOdemeHepsiAsync(CancellationToken ct = default)
        => _repository.ListMuayeneOdemeHepsiAsync(ct);

    private static void HesapKontrol(LedgerAccountType hesap)
    {
        if (hesap is not (LedgerAccountType.Kasa or LedgerAccountType.Banka))
            throw new ValidationException("Ödeme hesabı Kasa veya Banka olmalıdır.");
    }

    /// <summary>MTV/Muayene kayıtlarında döviz alanı yok → TRY dışı para birimi reddedilir.</summary>
    private static string ParaBirimiKontrol(string? doviz, string ne)
    {
        var p = string.IsNullOrWhiteSpace(doviz) ? "TRY" : doviz.Trim().ToUpperInvariant();
        if (p != "TRY")
            throw new ValidationException(
                $"{ne} tutarları TRY'dir (kayıtta döviz alanı yok); ödeme para birimi {p} olamaz.");
        return p;
    }

    /// <summary>Kısmi tutar doğrulaması: null → tavanın tamamı; pozitif olmalı; tavanı AŞAMAZ.</summary>
    private static decimal TutarKontrol(decimal? istenen, decimal tavan, string ne)
    {
        if (tavan <= 0m) throw new ValidationException($"{ne} kaydında ödenecek bakiye yok.");
        if (istenen is not { } t) return tavan;
        // Adversarial L5: yuvarlama ÖNCE — 0,00004 gibi bir tutar pozitiflik kontrolünü geçip
        // yuvarlandıktan sonra 0'a düşüyor ve DB CHECK'ine 500 ile takılıyordu.
        var yuvarlak = decimal.Round(t, 4, MidpointRounding.AwayFromZero);
        if (yuvarlak <= 0m) throw new ValidationException("Ödeme tutarı pozitif olmalıdır.");
        // Aşım REDDEDİLİR: kabul etseydik Kalan negatife düşer, Odendi true olur ve deftere
        // borçtan fazlası yazılırdı (mutabakat bozulur).
        if (yuvarlak > tavan) throw new ValidationException($"Ödeme tutarı kalan bakiyeyi aşamaz (kalan {tavan:N2}).");
        return yuvarlak;
    }

    /// <summary>
    /// Adversarial M3 — KISMİ ödemede işlem anahtarı ZORUNLU.
    ///
    /// <para>Tam ödemede tekrar koruması YAPISALDIR: ikinci çağrı <c>Odendi</c>/<c>Kalan=0</c>
    /// çitine takılır. Kısmi ödemede ise "aynı tutarı ikinci kez yazmak" MEŞRU bir iş
    /// senaryosudur (1000'in 400'ü iki kez ödenebilir) — bu yüzden hiçbir yapısal kısıt kazara
    /// tekrarı ayıramaz. Tek ayıraç açık bir anahtardır; opsiyonel bırakılsaydı bir retry döngüsü
    /// sessizce çift gider yazardı. Form her render'da yeni GUID basar; programatik çağıranlar
    /// (job/REST) anahtarı üretmek ZORUNDADIR.</para>
    /// </summary>
    private static void AnahtarKontrol(RegulasyonOdemeInput g)
    {
        if (g.Tutar is not null && Anahtar(g.IslemAnahtari) is null)
            throw new ValidationException(
                "Kısmi ödemede işlem anahtarı zorunludur (çift gönderim koruması).");
    }

    private static Guid? Anahtar(Guid? a) => a is { } g && g != Guid.Empty ? g : null;

    /// <summary>
    /// Sigorta ödeme (roadmap J3): DENGELİ defter — Borç Gider / Alacak Kasa-Banka (Prim + zeyil ek prim).
    /// FinanceWrite + dönem-kilidi + idempotency (SourceId=policyId). InsurancePolicy.Odendi=true + ZeyilPrim
    /// (atomik, tek tx). Para birimi poliçenin Currency'si; kur ile baz tutara çevrilir.
    /// </summary>
    public async Task SigortaOdeAsync(Guid policyId, LedgerAccountType hesap, decimal zeyilEkPrim = 0m,
        DateTimeOffset? odemeTarih = null, decimal? kur = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (hesap is not (LedgerAccountType.Kasa or LedgerAccountType.Banka))
            throw new ValidationException("Ödeme hesabı Kasa veya Banka olmalıdır.");
        if (zeyilEkPrim < 0m) throw new ValidationException("Zeyil ek prim negatif olamaz.");

        var rec = await _repository.FindInsuranceAsync(policyId, ct) ?? throw new ValidationException("Sigorta poliçesi bulunamadı.");
        if (rec.Odendi) throw new ValidationException("Sigorta zaten ödendi.");
        var toplam = rec.Prim + zeyilEkPrim;
        if (toplam <= 0m) throw new ValidationException("Sigorta ödeme tutarı pozitif olmalıdır.");

        var tarih = odemeTarih ?? DateTimeOffset.UtcNow;
        await _lock.EnsureOpenAsync(tarih, ct); // dönem kilidi

        // Kur çözümü (1.1): açık kur aynen; boş → TRY=1 / döviz poliçede KurService (yoksa net red).
        var cozulenKur = await _kurCozucu.CozAsync(rec.Currency, kur, tarih, ct);
        var money = new Money(toplam, (rec.Currency ?? "TRY").Trim().ToUpperInvariant(), cozulenKur);
        var desc = $"Sigorta ödeme {rec.Tip}" + (zeyilEkPrim > 0m ? $" (+zeyil {zeyilEkPrim})" : "");
        await _repository.PostSigortaOdemeAsync(policyId, zeyilEkPrim,
        [
            new AccountLedgerEntry { EntryDateUtc = tarih, AccountType = LedgerAccountType.Gider, AccountRef = rec.VehicleId,
                Direction = LedgerDirection.Debit, Amount = money, SourceType = "SigortaOdeme", SourceId = policyId, Description = desc },
            new AccountLedgerEntry { EntryDateUtc = tarih, AccountType = hesap, AccountRef = null,
                Direction = LedgerDirection.Credit, Amount = money, SourceType = "SigortaOdeme", SourceId = policyId, Description = desc }
        ], ct);
    }

    private static void RequireVehicle(Guid vehicleId)
    {
        if (vehicleId == Guid.Empty) throw new ValidationException("Araç seçilmelidir.");
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
