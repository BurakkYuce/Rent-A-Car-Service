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
public sealed class AracKrediService(IAracKrediRepository repository, ICurrentUser currentUser,
    RentACar.Application.Periods.IPeriodLockGuard periodLock, RentACar.Application.Kur.KurCozucu kurCozucu,
    RentACar.Application.FinancialAccounts.HesapCozucu hesapCozucu)
{
    private readonly IAracKrediRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly RentACar.Application.Periods.IPeriodLockGuard _lock = periodLock;
    private readonly RentACar.Application.Kur.KurCozucu _kurCozucu = kurCozucu;
    private readonly RentACar.Application.FinancialAccounts.HesapCozucu _hesapCozucu = hesapCozucu;

    public Task<IReadOnlyList<AracKredi>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    /// <summary>FAZ-13 — filtreli liste (Cari/Plaka/Dosya/Durum/Tarih aralığı). Salt-okur; ekranın
    /// açık olduğu dört rol de görebilsin diye izin OR'lanır (Operatör'de yalnız OperationsWrite var,
    /// Muhasebe'de yalnız FinanceWrite/ViewReports).</summary>
    public Task<IReadOnlyList<AracKredi>> SearchAsync(AracKrediFilter? filtre = null, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser,
            Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports);
        return _repository.SearchAsync(filtre ?? new AracKrediFilter(), ct);
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
            Durum = KrediDurum.Aktif,
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
    public async Task<bool> TaksitOdeAsync(Guid id, LedgerAccountType hesap = LedgerAccountType.Kasa,
        DateTimeOffset? odemeTarih = null, Guid? islemAnahtari = null,
        Guid? hesapId = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite); // defter yazar
        if (hesap is not (LedgerAccountType.Kasa or LedgerAccountType.Banka))
            throw new ValidationException("Ödeme hesabı Kasa veya Banka olmalıdır.");

        var kredi = await _repository.FindAsync(id, ct);
        if (kredi is null) return false;
        if (kredi.Durum == KrediDurum.Iptal) throw new ValidationException("İptal kredinin taksiti ödenemez.");

        TarihPolitikasi.ParaTarihi(odemeTarih, "Taksit"); // gelecek tarih reddi (para-yolu simetrisi — L2)
        var tarih = odemeTarih ?? DateTimeOffset.UtcNow;
        await _lock.EnsureOpenAsync(tarih, ct); // dönem kilidi
        var kur = await _kurCozucu.CozAsync(kredi.Currency, null, tarih, ct); // 1.1 sözleşmesi
        var ozet = Hesapla(kredi);
        var anahtar = islemAnahtari is { } a && a != Guid.Empty ? a : (Guid?)null;
        var hesapRef = await _hesapCozucu.CozAsync(hesapId, hesap, ct); // FAZ-50 (lambda'dan ONCE: async)

        return await _repository.TaksitOdeAsync(id, sira =>
        {
            // Kalan-yöntemi (adversarial 1.3 L1): SON taksit = toplam − (n−1)×aylık → Σ taksit
            // kuruş-birebir toplam geri ödemeye eşit (yuvarlama kayması deftere sızmaz).
            var taksit = sira == kredi.TaksitSayisi
                ? Math.Round(ozet.ToplamGeriOdeme - ozet.AylikTaksit * (kredi.TaksitSayisi - 1), 2, MidpointRounding.AwayFromZero)
                : ozet.AylikTaksit;
            var money = new Money(taksit, kredi.Currency, kur);
            var desc = $"Kredi taksiti {kredi.No} #{sira}/{kredi.TaksitSayisi} ({kredi.BankaAdi})";
            var expense = new Expense
            {
                Tip = ExpenseType.Finansman,
                Tarih = tarih,
                VehicleId = kredi.VehicleId,
                NetTutar = taksit, KdvOrani = 0m, KdvTutar = 0m, GenelToplam = taksit, // sira-bazlı (kalan-yöntemi)
                Currency = kredi.Currency, Kur = kur,
                OdemeYontemi = hesap == LedgerAccountType.Banka ? OdemeYontemi.Banka : OdemeYontemi.Nakit,
                KasaBankaHesap = hesap,
                FinansalHesapId = hesapRef,   // FAZ-50: belge de hangi hesaptan odendigini tasir
                Aciklama = desc,
                IslemAnahtari = anahtar
            };
            IReadOnlyList<AccountLedgerEntry> entries =
            [
                new AccountLedgerEntry { EntryDateUtc = tarih, AccountType = LedgerAccountType.Gider, AccountRef = kredi.VehicleId,
                    Direction = LedgerDirection.Debit, Amount = money, SourceType = "Gider", SourceId = expense.Id, Description = desc },
                // FAZ-50: nakit bacagi hangi kasa/bankadan odendigini tasir (null -> legacy kova).
                new AccountLedgerEntry { EntryDateUtc = tarih, AccountType = hesap, AccountRef = hesapRef,
                    Direction = LedgerDirection.Credit, Amount = money, SourceType = "Gider", SourceId = expense.Id, Description = desc }
            ];
            return (expense, entries);
        }, ct);
    }

    public Task<bool> IptalAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.SetDurumAsync(id, KrediDurum.Iptal, ct);
    }

    /// <summary>
    /// FAZ-13 — "Taksitleri İptal Et" (toplu). Seçili kredilerin <b>KALAN (ödenmemiş) taksitlerini</b>
    /// iptal eder: kredi <see cref="KrediDurum.Iptal"/>'e geçer ve bir daha taksit ödenemez
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
    public async Task<int> TaksitleriIptalEtAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // deftere yazmaz → FinanceWrite gerekmez
        if (ids is null || ids.Count == 0) throw new ValidationException("İptal edilecek kredi seçilmedi.");

        // Aktif-mi kontrolü + yazım TEK transaction'da, satır kilidi altında (repository) — servis
        // tarafında "önce oku sonra yaz" yapsaydık eşzamanlı son taksit ödemesiyle yarışıp
        // KAPANMIŞ bir krediyi İptal'e düşürebilirdi.
        return await _repository.TopluIptalAsync(ids, ct);
    }

    /// <summary>Taksit planı + kalan bakiye (salt-hesap). Basit faiz: toplamFaiz = tutar × faiz × taksit/12;
    /// aylık taksit = toplam geri ödeme / taksit sayısı; kalan = toplam − ödenen.
    /// <paramref name="bugun"/> yalnız "bu ayki taksit" kutusu içindir (test edilebilirlik: varsayılan
    /// <c>UtcNow</c> deterministik testi imkânsız kılardı).</summary>
    public static AracKrediOzet Hesapla(AracKredi k, DateTimeOffset? bugun = null)
    {
        // Elle/dış yoldan girilmiş bozuk satır (TaksitSayisi ≤ 0) liste sayfasını DivideByZero ile
        // düşürmesin: hesap yapılamaz, boş özet döner (CreateAsync zaten 1..360 çitini uygular).
        if (k.TaksitSayisi < 1)
            return new AracKrediOzet(0m, k.KrediTutari, 0m, 0m, k.KrediTutari, [], null, 0m, 0m);

        var toplamFaiz = R(k.KrediTutari * k.FaizOran * k.TaksitSayisi / 12m);
        var toplamGeriOdeme = k.KrediTutari + toplamFaiz;
        var aylikTaksit = R(toplamGeriOdeme / k.TaksitSayisi);

        // Kalan-yöntemi (L1): son taksit farkı emer → Σ taksit == toplam geri ödeme kuruş-birebir
        // (kapanmış kredide "0,01 kalan" hayaleti ve deftere fazla/eksik yazım biter).
        var sonTaksit = R(toplamGeriOdeme - aylikTaksit * (k.TaksitSayisi - 1));
        var taksitler = new List<AracKrediTaksit>(k.TaksitSayisi);
        for (var i = 0; i < k.TaksitSayisi; i++)
            taksitler.Add(new AracKrediTaksit(i + 1, k.BaslangicTarihi.AddMonths(i),
                i == k.TaksitSayisi - 1 ? sonTaksit : aylikTaksit, i < k.OdenenTaksit));

        var odenenAdet = Math.Clamp(k.OdenenTaksit, 0, k.TaksitSayisi);
        var odenenTutar = odenenAdet >= k.TaksitSayisi
            ? toplamGeriOdeme
            : R(odenenAdet * aylikTaksit);
        var kalanBakiye = Math.Max(0m, toplamGeriOdeme - odenenTutar);

        var referans = bugun ?? DateTimeOffset.UtcNow;
        var buAy = taksitler.Where(t => AyniAy(t.Vade, referans)).Sum(t => t.Tutar);

        return new AracKrediOzet(toplamFaiz, toplamGeriOdeme, aylikTaksit, odenenTutar, kalanBakiye,
            taksitler, taksitler[^1].Vade, sonTaksit, buAy);
    }

    /// <summary>
    /// FAZ-13 — liste üstü 5 özet kart. Ekrandaki (filtrelenmiş) küme üzerinden toplar; İPTAL krediler
    /// hariç tutulur. Salt gösterge — deftere yazmaz, hiçbir rapor toplamına karışmaz.
    /// </summary>
    public static AracKrediPano Pano(IEnumerable<AracKredi> krediler, DateTimeOffset bugun)
    {
        var ozetler = krediler
            .Where(k => k.Durum != KrediDurum.Iptal)  // iptal kredinin borcu/faizi yoktur
            .Select(k => Hesapla(k, bugun))
            .ToList();
        if (ozetler.Count == 0) return new AracKrediPano(0m, null, 0m, 0m, 0m);

        // "Son vade" = kümedeki en geç biten kredi; "son taksit tutarı" O kredinin son taksitidir
        // (tutarları toplamak anlamsız olurdu — farklı kredilerin son taksitleri aynı ödeme değil).
        var enGec = ozetler.Where(o => o.SonVadeGunu is not null)
            .OrderByDescending(o => o.SonVadeGunu!.Value).FirstOrDefault();

        return new AracKrediPano(
            ozetler.Sum(o => o.ToplamFaiz),
            enGec?.SonVadeGunu,
            enGec?.SonTaksitTutari ?? 0m,
            ozetler.Sum(o => o.BuAyToplamTaksit),
            ozetler.Sum(o => o.KalanBakiye));
    }

    /// <summary>
    /// Aynı takvim ayı mı? <b>YEREL</b> zamana göre karşılaştırılır — ekran vadeleri
    /// <c>.LocalDateTime</c> ile bastığı için UTC'ye göre kovalamak "tabloda 01.08 yazıyor ama temmuz
    /// kutusunda sayılıyor" tutarsızlığı üretirdi: form tarihi <c>FormParse.Date</c> ile yerel
    /// gece-yarısından UTC'ye çevrilir (01.08 → 31.07T21:00Z).
    /// </summary>
    private static bool AyniAy(DateTimeOffset a, DateTimeOffset b)
    {
        var (x, y) = (a.LocalDateTime, b.LocalDateTime);
        return x.Year == y.Year && x.Month == y.Month;
    }

    private static decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
