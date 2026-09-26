using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.AracKredileri;

/// <summary>
/// Araç kredisi iş mantığı (roadmap L4): kredi oluştur/listele + taksit öde (kalan bakiye) + durum.
/// Banka entegrasyonu YOK, DEFTER POSTLAMAZ → salt kayıt/hesap; yazma OperationsWrite.
/// </summary>
public sealed class VehicleLoanService(IVehicleLoanRepository repository, ICurrentUser currentUser,
    RentACar.Application.Periods.IPeriodLockGuard periodLock, RentACar.Application.Kur.ExchangeRateResolver exchangeRateResolver,
    RentACar.Application.FinancialAccounts.AccountResolver accountResolver)
{
    private readonly IVehicleLoanRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly RentACar.Application.Periods.IPeriodLockGuard _lock = periodLock;
    private readonly RentACar.Application.Kur.ExchangeRateResolver _exchangeRateResolver = exchangeRateResolver;
    private readonly RentACar.Application.FinancialAccounts.AccountResolver _accountResolver = accountResolver;

    public Task<IReadOnlyList<AracKredi>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    /// <summary>FAZ-13 — filtreli liste (Cari/Plaka/Dosya/Durum/Tarih aralığı). Salt-okur; ekranın
    /// açık olduğu dört rol de görebilsin diye izin OR'lanır (Operatör'de yalnız OperationsWrite var,
    /// Muhasebe'de yalnız FinanceWrite/ViewReports).</summary>
    public Task<IReadOnlyList<AracKredi>> SearchAsync(AracKrediFilter? filter = null, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser,
            Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports);
        return _repository.SearchAsync(filter ?? new AracKrediFilter(), ct);
    }

    public Task<AracKredi?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(AracKrediInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (string.IsNullOrWhiteSpace(input.BankaAdi)) throw new ValidationException("Banka adı zorunludur.");
        if (input.KrediTutari <= 0m) throw new ValidationException("Kredi tutarı pozitif olmalıdır.");
        if (input.TaksitSayisi is < 1 or > 360) throw new ValidationException("Taksit sayısı 1 ile 360 arasında olmalıdır.");
        if (input.FaizOran < 0m) throw new ValidationException("Faiz oranı negatif olamaz.");
        if (input.Kur <= 0m) throw new ValidationException("Kur pozitif olmalıdır.");

        var row = new AracKredi
        {
            Id = input.IslemAnahtari is { } ia && ia != Guid.Empty ? ia : Guid.NewGuid(), // F6.1b idempotent oluşturma
            BankaAdi = input.BankaAdi.Trim(),
            VehicleId = input.VehicleId,
            // FAZ-13: cari yalnız İLİŞKİ — cari bakiyesine/defterine hiçbir kayıt gitmez.
            CariId = input.CariId,
            DosyaNo = string.IsNullOrWhiteSpace(input.DosyaNo) ? null : input.DosyaNo.Trim(),
            KrediTutari = input.KrediTutari,
            FaizOran = input.FaizOran,
            TaksitSayisi = input.TaksitSayisi,
            BaslangicTarihi = input.BaslangicTarihi ?? DateTimeOffset.UtcNow,
            OdenenTaksit = 0,
            Currency = string.IsNullOrWhiteSpace(input.Doviz) ? "TRY" : input.Doviz.Trim().ToUpperInvariant(),
            Kur = input.Kur,
            Durum = LoanStatus.Aktif,
            Aciklama = string.IsNullOrWhiteSpace(input.Aciklama) ? null : input.Aciklama.Trim()
        };
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    /// <summary>Taksit öde (FAZ 1.3): artık GERÇEK GİDER postlar — Expense(Tip=Finansman,
    /// VehicleId=kredinin aracı) + Borç Gider / Alacak Kasa-Banka, sayaçla AYNI transaction'da.
    /// Bu yüzden yetki OperationsWrite→FinanceWrite'a yükseldi (davranış değişikliği). Kur: ödeme
    /// günü 1.1 sözleşmesi (TRY=1; döviz sabit-kur/TCMB, yoksa red). Mevcut kredilerin GEÇMİŞ
    /// ödenmiş taksitleri retro postlanmaz. Çift-submit: islemAnahtari + Expense kısmi unique index.</summary>
    public async Task<bool> PayInstallmentAsync(Guid id, LedgerAccountType account = LedgerAccountType.Kasa,
        DateTimeOffset? paymentDate = null, Guid? operationKey = null,
        Guid? accountId = null, CancellationToken ct = default, int? expectedSequence = null)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite); // defter yazar
        if (account is not (LedgerAccountType.Kasa or LedgerAccountType.Banka))
            throw new ValidationException("Ödeme hesabı Kasa veya Banka olmalıdır.");

        var loan = await _repository.FindAsync(id, ct);
        if (loan is null) return false;
        if (loan.Durum == LoanStatus.Iptal) throw new ValidationException("İptal kredinin taksiti ödenemez.");

        DatePolicy.MoneyDate(paymentDate, "Taksit"); // gelecek tarih reddi (para-yolu simetrisi — L2)
        var date = paymentDate ?? DateTimeOffset.UtcNow;
        await _lock.EnsureOpenAsync(date, ct); // dönem kilidi
        var exchangeRate = await _exchangeRateResolver.ResolveAsync(loan.Currency, null, date, ct); // 1.1 sözleşmesi
        var summary = Calculate(loan);
        var key = operationKey is { } a && a != Guid.Empty ? a : (Guid?)null;
        // FAZ-50 (lambda'dan ONCE: async). F6.1b: kredi dövizi de verilir — hesap-döviz çiti (ADVERSARIAL M4)
        // taksitte uygulanmıyordu: USD tanımlı bankadan TRY taksit (ya da tersi) hesap mutabakatını bozuyordu.
        var accountRef = await _accountResolver.ResolveAsync(accountId, account, ct, loan.Currency);

        return await _repository.PayInstallmentAsync(id, order =>
        {
            // Kalan-yöntemi (adversarial 1.3 L1): SON taksit = toplam − (n−1)×aylık → Σ taksit
            // kuruş-birebir toplam geri ödemeye eşit (yuvarlama kayması deftere sızmaz).
            var installment = order == loan.TaksitSayisi
                ? Math.Round(summary.ToplamGeriOdeme - summary.AylikTaksit * (loan.TaksitSayisi - 1), 2, MidpointRounding.AwayFromZero)
                : summary.AylikTaksit;
            var money = new Money(installment, loan.Currency, exchangeRate);
            var desc = $"Kredi taksiti {loan.No} #{order}/{loan.TaksitSayisi} ({loan.BankaAdi})";
            var expense = new Expense
            {
                Tip = ExpenseType.Finansman,
                Tarih = date,
                VehicleId = loan.VehicleId,
                NetTutar = installment, KdvOrani = 0m, KdvTutar = 0m, GenelToplam = installment, // sira-bazlı (kalan-yöntemi)
                Currency = loan.Currency, Kur = exchangeRate,
                OdemeYontemi = account == LedgerAccountType.Banka ? PaymentMethod.Banka : PaymentMethod.Nakit,
                KasaBankaHesap = account,
                FinansalHesapId = accountRef,   // FAZ-50: belge de hangi hesaptan odendigini tasir
                Aciklama = desc,
                IslemAnahtari = key
            };
            IReadOnlyList<AccountLedgerEntry> entries =
            [
                new AccountLedgerEntry { EntryDateUtc = date, AccountType = LedgerAccountType.Gider, AccountRef = loan.VehicleId,
                    Direction = LedgerDirection.Debit, Amount = money, SourceType = "Gider", SourceId = expense.Id, Description = desc },
                // FAZ-50: nakit bacagi hangi kasa/bankadan odendigini tasir (null -> legacy kova).
                new AccountLedgerEntry { EntryDateUtc = date, AccountType = account, AccountRef = accountRef,
                    Direction = LedgerDirection.Credit, Amount = money, SourceType = "Gider", SourceId = expense.Id, Description = desc }
            ];
            return (expense, entries);
        }, ct, key, expectedSequence);
    }

    public Task<bool> CancelAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsDelete); // inceltme
        return _repository.SetStatusAsync(id, LoanStatus.Iptal, ct);
    }

    /// <summary>
    /// FAZ-13 — "Taksitleri İptal Et" (toplu). Seçili kredilerin <b>KALAN (ödenmemiş) taksitlerini</b>
    /// iptal eder: kredi <see cref="LoanStatus.Iptal"/>'e geçer ve bir daha taksit ödenemez
    /// (çit repository'de, satır kilidinin ARKASINDA — M1 deseni).
    ///
    /// <para><b>ÖDENMİŞ taksitlere DOKUNMAZ.</b> Canlı sistemde bu düğme taksit satırlarını siler;
    /// bizde ödenmiş taksit gerçek gider + dengeli defter kaydı üretmiştir (ExpenseType.Finansman) ve
    /// mali kayıt DEĞİŞMEZDİR (rc_prevent_mutation). Geçmişi geri almak ancak ters kayıtla olur ve o
    /// AYRI bir iştir — bu düğme sessizce para silmez.</para>
    ///
    /// <para>Zaten Aktif olmayan (Kapandi/Iptal) krediler sessizce ATLANIR: toplu seçimde bir satırın
    /// durumu yüzünden tüm işlem patlamamalı. Dönüş = gerçekten iptal edilen kredi adedi.</para>
    /// </summary>
    public async Task<int> CancelInstallmentsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsDelete); // deftere yazmaz ama İPTAL → Delete izni
        if (ids is null || ids.Count == 0) throw new ValidationException("İptal edilecek kredi seçilmedi.");

        // Aktif-mi kontrolü + yazım TEK transaction'da, satır kilidi altında (repository) — servis
        // tarafında "önce oku sonra yaz" yapsaydık eşzamanlı son taksit ödemesiyle yarışıp
        // KAPANMIŞ bir krediyi İptal'e düşürebilirdi.
        return await _repository.BulkCancelAsync(ids, ct);
    }

    /// <summary>Taksit planı + kalan bakiye (salt-hesap). Basit faiz: toplamFaiz = tutar × faiz × taksit/12;
    /// aylık taksit = toplam geri ödeme / taksit sayısı; kalan = toplam − ödenen.
    /// <paramref name="today"/> yalnız "bu ayki taksit" kutusu içindir (test edilebilirlik: varsayılan
    /// <c>UtcNow</c> deterministik testi imkânsız kılardı).</summary>
    public static AracKrediOzet Calculate(AracKredi k, DateTimeOffset? today = null)
    {
        // Elle/dış yoldan girilmiş bozuk satır (TaksitSayisi ≤ 0) liste sayfasını DivideByZero ile
        // düşürmesin: hesap yapılamaz, boş özet döner (CreateAsync zaten 1..360 çitini uygular).
        if (k.TaksitSayisi < 1)
            return new AracKrediOzet(0m, k.KrediTutari, 0m, 0m, k.KrediTutari, [], null, 0m, 0m);

        var totalInterest = R(k.KrediTutari * k.FaizOran * k.TaksitSayisi / 12m);
        var totalRepayment = k.KrediTutari + totalInterest;
        var monthlyInstallment = R(totalRepayment / k.TaksitSayisi);

        // Kalan-yöntemi (L1): son taksit farkı emer → Σ taksit == toplam geri ödeme kuruş-birebir
        // (kapanmış kredide "0,01 kalan" hayaleti ve deftere fazla/eksik yazım biter).
        var lastInstallment = R(totalRepayment - monthlyInstallment * (k.TaksitSayisi - 1));
        var installments = new List<AracKrediTaksit>(k.TaksitSayisi);
        for (var i = 0; i < k.TaksitSayisi; i++)
            installments.Add(new AracKrediTaksit(i + 1, k.BaslangicTarihi.AddMonths(i),
                i == k.TaksitSayisi - 1 ? lastInstallment : monthlyInstallment, i < k.OdenenTaksit));

        var paidCount = Math.Clamp(k.OdenenTaksit, 0, k.TaksitSayisi);
        var paidAmount = paidCount >= k.TaksitSayisi
            ? totalRepayment
            : R(paidCount * monthlyInstallment);
        var remainingBalance = Math.Max(0m, totalRepayment - paidAmount);

        var reference = today ?? DateTimeOffset.UtcNow;
        var thisMonth = installments.Where(t => SameMonth(t.Vade, reference)).Sum(t => t.Tutar);

        return new AracKrediOzet(totalInterest, totalRepayment, monthlyInstallment, paidAmount, remainingBalance,
            installments, installments[^1].Vade, lastInstallment, thisMonth);
    }

    /// <summary>
    /// FAZ-13 — liste üstü 5 özet kart. Ekrandaki (filtrelenmiş) küme üzerinden toplar; İPTAL krediler
    /// hariç tutulur. Salt gösterge — deftere yazmaz, hiçbir rapor toplamına karışmaz.
    /// </summary>
    public static AracKrediPano Dashboard(IEnumerable<AracKredi> loans, DateTimeOffset today)
    {
        var summaries = loans
            .Where(k => k.Durum != LoanStatus.Iptal)  // iptal kredinin borcu/faizi yoktur
            .Select(k => Calculate(k, today))
            .ToList();
        if (summaries.Count == 0) return new AracKrediPano(0m, null, 0m, 0m, 0m);

        // "Son vade" = kümedeki en geç biten kredi; "son taksit tutarı" O kredinin son taksitidir
        // (tutarları toplamak anlamsız olurdu — farklı kredilerin son taksitleri aynı ödeme değil).
        var enGec = summaries.Where(o => o.SonVadeGunu is not null)
            .OrderByDescending(o => o.SonVadeGunu!.Value).FirstOrDefault();

        return new AracKrediPano(
            summaries.Sum(o => o.ToplamFaiz),
            enGec?.SonVadeGunu,
            enGec?.SonTaksitTutari ?? 0m,
            summaries.Sum(o => o.BuAyToplamTaksit),
            summaries.Sum(o => o.KalanBakiye));
    }

    /// <summary>
    /// Aynı takvim ayı mı? <b>YEREL</b> zamana göre karşılaştırılır — ekran vadeleri
    /// <c>.LocalDateTime</c> ile bastığı için UTC'ye göre kovalamak "tabloda 01.08 yazıyor ama temmuz
    /// kutusunda sayılıyor" tutarsızlığı üretirdi: form tarihi <c>FormParse.Date</c> ile yerel
    /// gece-yarısından UTC'ye çevrilir (01.08 → 31.07T21:00Z).
    /// </summary>
    private static bool SameMonth(DateTimeOffset a, DateTimeOffset b)
    {
        var (x, y) = (a.LocalDateTime, b.LocalDateTime);
        return x.Year == y.Year && x.Month == y.Month;
    }

    private static decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
