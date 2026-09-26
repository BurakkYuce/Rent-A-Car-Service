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
    /// <summary>F9.1 — ekranın gördüğü kalan (isteğe bağlı). Doluysa SATIR KİLİDİ altında okunan kalanla (muayenede
    /// ceza eklenmeden önceki) karşılaştırılır; farklıysa <see cref="ConcurrentModificationException"/> (409 cakisma) —
    /// bayat ekran / iki sekme "kalanın tamamı" ile beklemediği tutarı ödemez. <c>null</c> → karşılaştırma yok (Blazor).</summary>
    public decimal? BeklenenKalan { get; set; }
    /// <summary>FAZ-50 — ödemenin geçtiği SPESİFİK kasa/banka hesabı (<c>FinancialAccount</c>).
    /// <see cref="KasaKodu"/>/<see cref="HesapNo"/> serbest METİN künyesidir; bu alan defterin
    /// nakit bacağına <c>AccountRef</c> olarak yazılan gerçek bağdır. Boş → legacy kova.</summary>
    public Guid? HesapId { get; set; }
}

/// <summary>
/// FAZ-15 zeyil (poliçe eki) girdisi. TÜM tutar alanları <b>BİLGİ</b>dir — deftere yazılmaz
/// (bkz. <see cref="RentACar.Domain.Entities.InsurancePolicyZeyil"/>).
/// </summary>
public sealed class ZeyilInput
{
    public Guid PolicyId { get; set; }
    public string? ZeyilNo { get; set; }
    public DateTimeOffset? Tarih { get; set; }
    public DateTimeOffset? Tanzim { get; set; }
    public decimal? Deger { get; set; }
    public decimal? Brut { get; set; }
    public decimal? Net { get; set; }
    public decimal? FonVergi { get; set; }
    public string? Tipi { get; set; }
    public string? Neden { get; set; }
}

/// <summary>Sigorta/MTV/Muayene CRUD + doğrulama (araç zorunlu, tarih tutarlılığı) + MTV ödeme→defter (J1).</summary>
public sealed class RegulationService(IRegulationRepository repository, ICurrentUser currentUser, IPeriodLockGuard periodLock,
    RentACar.Application.Kur.ExchangeRateResolver exchangeRateResolver, RentACar.Application.FinancialAccounts.AccountResolver accountResolver)
{
    private readonly IRegulationRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IPeriodLockGuard _lock = periodLock;
    private readonly RentACar.Application.Kur.ExchangeRateResolver _exchangeRateResolver = exchangeRateResolver;
    private readonly RentACar.Application.FinancialAccounts.AccountResolver _accountResolver = accountResolver;

    /// <summary>Sigorta poliçesi para birimi beyaz-listesi (Currency kolonu HasMaxLength(3)).</summary>
    private static readonly HashSet<string> AllowedCurrencies = new(StringComparer.Ordinal) { "TRY", "EUR", "USD", "GBP" };

    public Task<IReadOnlyList<InsurancePolicy>> ListInsuranceAsync(CancellationToken ct = default)
        => _repository.ListInsuranceAsync(ct);
    public Task<IReadOnlyList<MtvRecord>> ListMtvAsync(CancellationToken ct = default)
        => _repository.ListMtvAsync(ct);
    public Task<IReadOnlyList<InspectionRecord>> ListInspectionAsync(CancellationToken ct = default)
        => _repository.ListInspectionAsync(ct);

    /// <summary>
    /// Poliçe ekler. <paramref name="vehicleValue"/>/<paramref name="immValue"/>/
    /// <paramref name="accessoryValue"/> FAZ-15 sigorta değer tabanıdır ve <b>BİLGİ ALANIDIR</b>
    /// (KARARLAR.md genel politikası) — deftere yazmaz, hiçbir hesaba/tavana girmez.
    /// <c>Kalan</c> açılışta <c>= prim</c> olur (ödeme onu 0'a düşürür).
    /// </summary>
    public async Task<Guid> AddInsuranceAsync(
        Guid vehicleId, InsuranceType tip, DateTimeOffset start, DateTimeOffset end,
        decimal premium, string? policeNo, string? company, string? agency,
        string? currencyCode = "TRY", decimal? vehicleValue = null, decimal? immValue = null,
        decimal? accessoryValue = null, CancellationToken ct = default, Guid? id = null)
    {
        RequireVehicle(vehicleId);
        if (end <= start) throw new ValidationException("Bitiş başlangıçtan sonra olmalıdır.");
        if (premium < 0) throw new ValidationException("Prim negatif olamaz.");
        // Teminat değerleri negatif olamaz (bilgi alanı da olsa saçma değer ekranı bozar).
        if (vehicleValue is < 0m) throw new ValidationException("Araç değeri negatif olamaz.");
        if (immValue is < 0m) throw new ValidationException("İMM değeri negatif olamaz.");
        if (accessoryValue is < 0m) throw new ValidationException("Aksesuar değeri negatif olamaz.");
        // Çok-döviz: ithal araç poliçesi EUR/USD olabilir → Currency create'te set edilir; ödemede
        // (SigortaOdeAsync) kur ile baz tutara çevrilir. Boş → TRY (yerel poliçe). Beyaz-liste dışı
        // reddedilir (adversarial Low: crafted POST'la çöp/uzun döviz → 3-hane kolon DbUpdateException).
        var currency = string.IsNullOrWhiteSpace(currencyCode) ? "TRY" : currencyCode.Trim().ToUpperInvariant();
        if (!AllowedCurrencies.Contains(currency))
            throw new ValidationException($"Geçersiz para birimi: {currency}. İzinli: {string.Join(", ", AllowedCurrencies)}.");
        var p = new InsurancePolicy
        {
            VehicleId = vehicleId, Tip = tip, Baslangic = start, Bitis = end,
            Prim = premium, Currency = currency, PoliceNo = Trim(policeNo), Firma = Trim(company), Acenta = Trim(agency),
            AracDegeri = vehicleValue, ImmDegeri = immValue, AksesuarDegeri = accessoryValue,
            Kalan = premium   // FAZ-15: bilgi amaçlı bakiye; ödeme 0'a düşürür (zeyil DEĞİŞTİRMEZ)
        };
        if (id is { } pid && pid != Guid.Empty) p.Id = pid; // F9.1: Idempotency-Key → PK
        await _repository.AddInsuranceAsync(p, ct);
        return p.Id;
    }

    public async Task<Guid> AddMtvAsync(
        Guid vehicleId, string period, decimal amount, DateTimeOffset due,
        string? description = null, CancellationToken ct = default, Guid? id = null)
    {
        RequireVehicle(vehicleId);
        if (string.IsNullOrWhiteSpace(period)) throw new ValidationException("Dönem zorunludur.");
        if (amount < 0) throw new ValidationException("Tutar negatif olamaz.");
        // Kalan = Tutar (FAZ-14): kısmi ödemeler bunu düşürür. Kaydın kendisi 0 tutarlıysa
        // Kalan da 0 olur ve ödeme "bakiye yok" ile reddedilir (doğru).
        var m = new MtvRecord { VehicleId = vehicleId, Donem = period.Trim(), Tutar = amount, Kalan = amount, Vade = due, Aciklama = Trim(description) };
        if (id is { } mid && mid != Guid.Empty) m.Id = mid; // F9.1: Idempotency-Key → PK
        await _repository.AddMtvAsync(m, ct);
        return m.Id;
    }

    public async Task<Guid> AddInspectionAsync(
        Guid vehicleId, DateTimeOffset inspectionDate, DateTimeOffset end, decimal fee,
        int? operationKm = null, string? description = null, CancellationToken ct = default, Guid? id = null)
    {
        RequireVehicle(vehicleId);
        if (end <= inspectionDate) throw new ValidationException("Bitiş muayene tarihinden sonra olmalıdır.");
        if (fee < 0) throw new ValidationException("Ücret negatif olamaz.");
        // Kalan = Ucret (FAZ-14). Ceza ödeme anında girilir ve o an borcu artırır — bu yüzden
        // açılışta kalana DAHİL EDİLMEZ (henüz doğmamış bir borç).
        if (operationKm is < 0) throw new ValidationException("İşlem KM negatif olamaz.");
        var i = new InspectionRecord
        {
            VehicleId = vehicleId, MuayeneTarihi = inspectionDate, Bitis = end, Ucret = fee,
            Kalan = fee, IslemKm = operationKm, Aciklama = Trim(description)
        };
        if (id is { } iid && iid != Guid.Empty) i.Id = iid; // F9.1: Idempotency-Key → PK
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
    public async Task<RegulasyonOdemeSonuc> PayMtvAsync(Guid mtvId, LedgerAccountType account,
        DateTimeOffset? paymentDate = null, string? currency = "TRY", decimal? exchangeRate = null,
        RegulasyonOdemeInput? payment = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        CheckAccount(account);
        var currencyUnit = CheckCurrency(currency, "MTV");

        var rec = await _repository.FindMtvAsync(mtvId, ct) ?? throw new ValidationException("MTV kaydı bulunamadı.");
        // F1.4: anahtarlı gönderimde "zaten ödendi" kararı repo'ya (kilidin arkasına) bırakılır — orada
        // ÖNCE anahtar aranır. Yoksa anahtarlı çift gönderim, ilk ödeme kaydı kapattıysa 400, kısmi
        // bıraktıysa 409 alıyordu.
        if (rec.Odendi && Key(payment?.IslemAnahtari) is null) throw new ValidationException("MTV zaten ödendi.");
        if (rec.Tutar <= 0m) throw new ValidationException("MTV tutarı pozitif olmalıdır.");

        // Adversarial H1: ödeme tarihi artık FORMDAN geliyor → gelecek tarih reddi ŞART. Dönem
        // kilidi bunu yakalamaz (yalnız kapanış tarihine kadarını reddeder, gelecek serbest);
        // guard'sız 2099 tarihli 1000 TL gider deftere düşer ve hiçbir dönemde mutabık olmaz.
        DatePolicy.MoneyDate(paymentDate, "MTV ödeme");   // para-yolu simetrisi (AracKredi dersi)
        var date = paymentDate ?? DateTimeOffset.UtcNow;
        await _lock.EnsureOpenAsync(date, ct); // dönem kilidi: kapalı döneme MTV ödemesi YOK
        var resolvedRate = await _exchangeRateResolver.ResolveAsync(currencyUnit, exchangeRate, date, ct);
        var g = payment ?? new RegulasyonOdemeInput();
        CheckKey(g);
        var accountRef = await _accountResolver.ResolveAsync(g.HesapId, account, ct); // FAZ-50 (lambda'dan ONCE: async)

        return await _repository.PostMtvPaymentAsync(mtvId, (remaining, order) =>
        {
            CheckStaleness(g, remaining);
            var amount = CheckAmount(g.Tutar, remaining, "MTV");
            var money = new Money(amount, currencyUnit, resolvedRate);
            var desc = $"MTV ödeme {rec.Donem} (#{order})";
            var row = new MtvOdeme
            {
                MtvId = mtvId, Sira = order, Tutar = amount, Tarih = date, Hesap = account,
                KasaKodu = Trim(g.KasaKodu), HesapNo = Trim(g.HesapNo), EvrakNo = Trim(g.EvrakNo),
                IslemYapan = Trim(g.IslemYapan), Aciklama = Trim(g.Aciklama),
                IslemAnahtari = Key(g.IslemAnahtari)
            };
            // SourceId = ÖDEMENİN id'si (kaydınki değil) → mevcut kısmi unique index
            // (TenantId, SourceType, SourceId, Direction) değişmeden her ödemeyi tekilleştirir.
            IReadOnlyList<AccountLedgerEntry> entries =
            [
                new AccountLedgerEntry { EntryDateUtc = date, AccountType = LedgerAccountType.Gider, AccountRef = rec.VehicleId,
                    Direction = LedgerDirection.Debit, Amount = money, SourceType = "MtvOdeme", SourceId = row.Id, Description = desc },
                // FAZ-50: nakit bacagi hangi kasa/bankadan odendigini tasir (null -> legacy kova).
                new AccountLedgerEntry { EntryDateUtc = date, AccountType = account, AccountRef = accountRef,
                    Direction = LedgerDirection.Credit, Amount = money, SourceType = "MtvOdeme", SourceId = row.Id, Description = desc }
            ];
            return (row, entries);
        }, ct, Key(g.IslemAnahtari));
    }

    /// <summary>Bir MTV kaydının ödeme geçmişi (sıraya göre).</summary>
    public Task<IReadOnlyList<MtvOdeme>> ListMtvPaymentsAsync(Guid mtvId, CancellationToken ct = default)
        => _repository.ListMtvPaymentsAsync(mtvId, ct);

    /// <summary>Adversarial M2/L9 — TÜM MTV ödemeleri tek sorguda (liste sayfası kayıt başına
    /// sorgu atmasın ve kapanan kaydın geçmişi ekrandan KAYBOLMASIN).</summary>
    public Task<IReadOnlyList<MtvOdeme>> ListAllMtvPaymentsAsync(CancellationToken ct = default)
        => _repository.ListAllMtvPaymentsAsync(ct);

    /// <summary>
    /// Muayene ödeme (roadmap J2 + FAZ-14 KISMİ ÖDEME). Ödemede girilen <c>Ceza</c> BORCU ARTIRIR
    /// (<c>Kalan = Kalan + Ceza − Tutar</c>) — kendiliğinden ödenmiş sayılmaz. Tutar null ise
    /// "kalan + bu ceza"nın tamamı ödenir (eski tek-seferde-kapat davranışının karşılığı).
    /// Para birimi TRY ile sınırlı (bkz. <see cref="PayMtvAsync"/>).
    /// </summary>
    public async Task<RegulasyonOdemeSonuc> PayInspectionAsync(Guid inspectionId, LedgerAccountType account,
        decimal penalty = 0m, DateTimeOffset? paymentDate = null, string? currency = "TRY", decimal? exchangeRate = null,
        RegulasyonOdemeInput? payment = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        CheckAccount(account);
        if (penalty < 0m) throw new ValidationException("Ceza negatif olamaz.");
        var currencyUnit = CheckCurrency(currency, "Muayene");

        var rec = await _repository.FindInspectionAsync(inspectionId, ct) ?? throw new ValidationException("Muayene kaydı bulunamadı.");
        // F1.4: bkz. MtvOdeAsync — anahtarlıysa "zaten ödendi" kararı kilidin arkasında, anahtardan SONRA.
        if (rec.Odendi && Key(payment?.IslemAnahtari) is null) throw new ValidationException("Muayene zaten ödendi.");

        DatePolicy.MoneyDate(paymentDate, "Muayene ödeme");   // adversarial H1
        var date = paymentDate ?? DateTimeOffset.UtcNow;
        await _lock.EnsureOpenAsync(date, ct); // dönem kilidi
        var resolvedRate = await _exchangeRateResolver.ResolveAsync(currencyUnit, exchangeRate, date, ct);
        var g = payment ?? new RegulasyonOdemeInput();
        CheckKey(g);
        var accountRef = await _accountResolver.ResolveAsync(g.HesapId, account, ct); // FAZ-50

        return await _repository.PostInspectionPaymentAsync(inspectionId, (remaining, order) =>
        {
            CheckStaleness(g, remaining);
            // Ceza önce borcu büyütür; ödenebilir tavan bu yüzden kalan + ceza.
            var cap = remaining + penalty;
            var amount = CheckAmount(g.Tutar, cap, "Muayene");
            var money = new Money(amount, currencyUnit, resolvedRate);
            var desc = $"Muayene ödeme {rec.MuayeneTarihi.LocalDateTime:dd.MM.yyyy} (#{order})"
                + (penalty > 0m ? $" (+ceza {penalty})" : "");
            var row = new MuayeneOdeme
            {
                InspectionId = inspectionId, Sira = order, Tutar = amount, Ceza = penalty, Tarih = date, Hesap = account,
                KasaKodu = Trim(g.KasaKodu), HesapNo = Trim(g.HesapNo), EvrakNo = Trim(g.EvrakNo),
                IslemYapan = Trim(g.IslemYapan), Aciklama = Trim(g.Aciklama),
                IslemAnahtari = Key(g.IslemAnahtari)
            };
            IReadOnlyList<AccountLedgerEntry> entries =
            [
                new AccountLedgerEntry { EntryDateUtc = date, AccountType = LedgerAccountType.Gider, AccountRef = rec.VehicleId,
                    Direction = LedgerDirection.Debit, Amount = money, SourceType = "MuayeneOdeme", SourceId = row.Id, Description = desc },
                // FAZ-50: nakit bacagi hangi kasa/bankadan odendigini tasir (null -> legacy kova).
                new AccountLedgerEntry { EntryDateUtc = date, AccountType = account, AccountRef = accountRef,
                    Direction = LedgerDirection.Credit, Amount = money, SourceType = "MuayeneOdeme", SourceId = row.Id, Description = desc }
            ];
            return (row, entries);
        }, ct, Key(g.IslemAnahtari));
    }

    /// <summary>Bir muayene kaydının ödeme geçmişi (sıraya göre).</summary>
    public Task<IReadOnlyList<MuayeneOdeme>> ListInspectionPaymentsAsync(Guid inspectionId, CancellationToken ct = default)
        => _repository.ListInspectionPaymentsAsync(inspectionId, ct);

    /// <summary>Adversarial M2/L9 — tüm muayene ödemeleri tek sorguda.</summary>
    public Task<IReadOnlyList<MuayeneOdeme>> ListAllInspectionPaymentsAsync(CancellationToken ct = default)
        => _repository.ListAllInspectionPaymentsAsync(ct);

    private static void CheckAccount(LedgerAccountType account)
    {
        if (account is not (LedgerAccountType.Kasa or LedgerAccountType.Banka))
            throw new ValidationException("Ödeme hesabı Kasa veya Banka olmalıdır.");
    }

    /// <summary>MTV/Muayene kayıtlarında döviz alanı yok → TRY dışı para birimi reddedilir.</summary>
    private static string CheckCurrency(string? currency, string ne)
    {
        var p = string.IsNullOrWhiteSpace(currency) ? "TRY" : currency.Trim().ToUpperInvariant();
        if (p != "TRY")
            throw new ValidationException(
                $"{ne} tutarları TRY'dir (kayıtta döviz alanı yok); ödeme para birimi {p} olamaz.");
        return p;
    }

    /// <summary>Kısmi tutar doğrulaması: null → tavanın tamamı; pozitif olmalı; tavanı AŞAMAZ.</summary>
    private static decimal CheckAmount(decimal? requested, decimal cap, string ne)
    {
        if (cap <= 0m) throw new ValidationException($"{ne} kaydında ödenecek bakiye yok.");
        if (requested is not { } t) return cap;
        // Adversarial L5: yuvarlama ÖNCE — 0,00004 gibi bir tutar pozitiflik kontrolünü geçip
        // yuvarlandıktan sonra 0'a düşüyor ve DB CHECK'ine 500 ile takılıyordu.
        var rounded = decimal.Round(t, 4, MidpointRounding.AwayFromZero);
        if (rounded <= 0m) throw new ValidationException("Ödeme tutarı pozitif olmalıdır.");
        // Aşım REDDEDİLİR: kabul etseydik Kalan negatife düşer, Odendi true olur ve deftere
        // borçtan fazlası yazılırdı (mutabakat bozulur).
        if (rounded > cap) throw new ValidationException($"Ödeme tutarı kalan bakiyeyi aşamaz (kalan {cap:N2}).");
        return rounded;
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
    private static void CheckKey(RegulasyonOdemeInput g)
    {
        if (g.Tutar is not null && Key(g.IslemAnahtari) is null)
            throw new ValidationException(
                "Kısmi ödemede işlem anahtarı zorunludur (çift gönderim koruması).");
    }

    private static Guid? Key(Guid? a) => a is { } g && g != Guid.Empty ? g : null;

    /// <summary>F9.1 — kilit altında okunan kalan, ekranın gördüğüyle aynı mı (bkz. <see cref="RegulasyonOdemeInput.BeklenenKalan"/>).</summary>
    private static void CheckStaleness(RegulasyonOdemeInput g, decimal remaining)
    {
        if (g.BeklenenKalan is { } expected && expected != remaining)
            throw new ConcurrentModificationException(
                $"Kayıt bu sırada değişti (kalan artık {remaining:N2}); ödeme yazılmadı. Kaydı yeniden açıp tekrar deneyin.");
    }

    /// <summary>
    /// Sigorta ödeme (roadmap J3): DENGELİ defter — Borç Gider / Alacak Kasa-Banka (Prim + zeyil ek prim).
    /// FinanceWrite + dönem-kilidi + idempotency (SourceId=policyId). InsurancePolicy.Odendi=true + ZeyilPrim
    /// (atomik, tek tx). Para birimi poliçenin Currency'si; kur ile baz tutara çevrilir.
    /// </summary>
    public async Task PayInsuranceAsync(Guid policyId, LedgerAccountType account, decimal endorsementExtraPremium = 0m,
        DateTimeOffset? paymentDate = null, decimal? exchangeRate = null,
        Guid? accountId = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (account is not (LedgerAccountType.Kasa or LedgerAccountType.Banka))
            throw new ValidationException("Ödeme hesabı Kasa veya Banka olmalıdır.");
        if (endorsementExtraPremium < 0m) throw new ValidationException("Zeyil ek prim negatif olamaz.");

        var rec = await _repository.FindInsuranceAsync(policyId, ct) ?? throw new ValidationException("Sigorta poliçesi bulunamadı.");
        if (rec.Odendi) throw new ValidationException("Sigorta zaten ödendi.");
        var total = rec.Prim + endorsementExtraPremium;
        if (total <= 0m) throw new ValidationException("Sigorta ödeme tutarı pozitif olmalıdır.");

        var date = paymentDate ?? DateTimeOffset.UtcNow;
        await _lock.EnsureOpenAsync(date, ct); // dönem kilidi

        // Kur çözümü (1.1): açık kur aynen; boş → TRY=1 / döviz poliçede KurService (yoksa net red).
        var resolvedRate = await _exchangeRateResolver.ResolveAsync(rec.Currency, exchangeRate, date, ct);
        var money = new Money(total, (rec.Currency ?? "TRY").Trim().ToUpperInvariant(), resolvedRate);
        var desc = $"Sigorta ödeme {rec.Tip}" + (endorsementExtraPremium > 0m ? $" (+zeyil {endorsementExtraPremium})" : "");
        var accountRef = await _accountResolver.ResolveAsync(accountId, account, ct); // FAZ-50
        await _repository.PostInsurancePaymentAsync(policyId, endorsementExtraPremium,
        [
            new AccountLedgerEntry { EntryDateUtc = date, AccountType = LedgerAccountType.Gider, AccountRef = rec.VehicleId,
                Direction = LedgerDirection.Debit, Amount = money, SourceType = "SigortaOdeme", SourceId = policyId, Description = desc },
            // FAZ-50: nakit bacagi hangi kasa/bankadan odendigini tasir (null -> legacy kova).
            new AccountLedgerEntry { EntryDateUtc = date, AccountType = account, AccountRef = accountRef,
                Direction = LedgerDirection.Credit, Amount = money, SourceType = "SigortaOdeme", SourceId = policyId, Description = desc }
        ], ct);
    }

    // ---- FAZ-15: poliçe zeyli (poliçe eki) ----

    /// <summary>Bir poliçenin zeyil geçmişi.</summary>
    public Task<IReadOnlyList<InsurancePolicyZeyil>> ListEndorsementsAsync(Guid policyId, CancellationToken ct = default)
        => _repository.ListEndorsementsAsync(policyId, ct);

    /// <summary>Tenant'ın tüm zeyilleri (liste ekranı için tek sorgu).</summary>
    public Task<IReadOnlyList<InsurancePolicyZeyil>> ListAllEndorsementsAsync(CancellationToken ct = default)
        => _repository.ListAllEndorsementsAsync(ct);

    /// <summary>
    /// Zeyil ekler (OperationsWrite). <b>DEFTERE HİÇBİR ŞEY YAZMAZ</b> — bu bilinçli bir karardır
    /// (KARARLAR.md "yeni tutar alanları deftere yazmaz"): gerçek para hareketi Kasa/Banka
    /// tahsilat-ödeme akışından geçer, ikinci bir yol çift-sayım üretirdi. Bu yüzden burada
    /// ne <c>IPeriodLockGuard</c> ne de <c>KurCozucu</c> çağrılır — mali bir işlem değildir.
    /// Poliçenin <c>Kalan</c>'ına da DOKUNMAZ (bkz. <c>InsurancePolicy.Kalan</c>).
    /// </summary>
    public async Task<Guid> AddEndorsementAsync(ZeyilInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);

        // Poliçe DOĞRULAMASI tenant sınırını da kurar: FindInsuranceAsync global query filter +
        // RLS arkasında çalışır → başka tenant'ın poliçe id'si "bulunamadı" ile reddedilir.
        _ = await _repository.FindInsuranceAsync(input.PolicyId, ct)
            ?? throw new ValidationException("Sigorta poliçesi bulunamadı.");

        var no = Trim(input.ZeyilNo) ?? throw new ValidationException("Zeyil no zorunludur.");
        if (no.Length > 32) throw new ValidationException("Zeyil no en fazla 32 karakter olabilir.");
        if (input.Tarih is not { } date) throw new ValidationException("Zeyil tarihi zorunludur.");
        // Uzunluklar SUNUCUDA da doğrulanır: formdaki maxlength yalnız tarayıcı çiti; elle
        // hazırlanmış POST kolon sınırını aşınca DbUpdateException → 500 verirdi (temiz red şart).
        var type = Trim(input.Tipi);
        if (type is { Length: > 64 }) throw new ValidationException("Zeyil tipi en fazla 64 karakter olabilir.");
        var reason = Trim(input.Neden);
        if (reason is { Length: > 512 }) throw new ValidationException("Zeyil nedeni en fazla 512 karakter olabilir.");
        // Değer bir TEMİNAT tabanıdır → negatif olamaz. Brüt/Net/Fon-Vergi ise tenzil (iade)
        // zeylinde negatiftir; bilgi alanı olduğu için işaret serbest (yön hatası üretemez).
        var value = Round(input.Deger);
        if (value < 0m) throw new ValidationException("Zeyil değeri negatif olamaz.");

        var z = new InsurancePolicyZeyil
        {
            PolicyId = input.PolicyId,
            ZeyilNo = no,
            Tarih = date,
            Tanzim = input.Tanzim,
            Deger = value,
            Brut = Round(input.Brut),
            Net = Round(input.Net),
            FonVergi = Round(input.FonVergi),
            Tipi = type,
            Neden = reason
        };
        await _repository.AddEndorsementAsync(z, ct);
        return z.Id;
    }

    /// <summary>
    /// Zeyil siler (OperationsWrite). Zeyil mali belge değildir (defter kaydı üretmez) → yanlış
    /// giriş SİLİNEBİLİR; ters kayıt gerektirmez. Bulunamazsa temiz doğrulama hatası.
    /// </summary>
    public async Task DeleteEndorsementAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (!await _repository.DeleteEndorsementAsync(id, ct))
            throw new ValidationException("Zeyil kaydı bulunamadı.");
    }

    /// <summary>Para alanı normalizasyonu — kolon numeric(19,4); null → 0.</summary>
    private static decimal Round(decimal? v)
        => v is { } d ? decimal.Round(d, 4, MidpointRounding.AwayFromZero) : 0m;

    private static void RequireVehicle(Guid vehicleId)
    {
        if (vehicleId == Guid.Empty) throw new ValidationException("Araç seçilmelidir.");
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
