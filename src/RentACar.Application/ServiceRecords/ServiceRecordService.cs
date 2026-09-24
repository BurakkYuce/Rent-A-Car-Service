using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Periods;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.ServiceRecords;

/// <summary>
/// Servis/bakım: kayıt (kalemlerle) + durum akışı (Rezerve → Açık → Serviste → Tamamlandi / Iptal) +
/// araç durumu kuplajı (servise alınınca Serviste, çıkınca Musait). İşçilik kalemleri eklenir.
/// Mali belge değildir (maliyet bilgilendirme); gerçek gider Gider dilimine bağlanır (follow-up).
/// Hasar rücu: tamamlanmış servis maliyeti kusur-oranıyla cari'ye yansıtılır (J4).
///
/// <para><b>FAZ-16 KİLİTLİ KARAR (docs/KARARLAR.md "FAZ-16"):</b> kaza/fatura/ödeme
/// blokları BİLGİDİR — <b>defterle BAĞLANMAZ</b>. Bu servis, o alanlar için hiçbir
/// <c>AccountLedgerEntry</c> üretmez, dönem kilidine ve <c>KurCozucu</c>'ya uğramaz. Gerçek
/// maliyet Giderler ekranından girilmeye devam eder; iki yazma yolu açmak ÇİFT-SAYIM olurdu
/// (raporlar P&amp;L'i yalnız defterden okur). Deftere giden TEK sayı, aşağıdaki
/// <see cref="YansitAsync"/> rücusudur ve o da <c>ToplamIscilik</c> (Σ kalem NET) üzerinden gider —
/// yeni fatura/ödeme alanlarına DOKUNMAZ.</para>
/// </summary>
public sealed class ServiceRecordService(
    IServiceRecordRepository repository, ICurrentUser currentUser, IPeriodLockGuard periodLock,
    RentACar.Application.Kur.KurCozucu kurCozucu, IRowVersionStore? rowVersions = null)
{
    private readonly IServiceRecordRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IPeriodLockGuard _lock = periodLock;
    private readonly RentACar.Application.Kur.KurCozucu _kurCozucu = kurCozucu;

    public Task<IReadOnlyList<ServiceRecord>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);
    public Task<ServiceRecord?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(ServiceRecordInput input, CancellationToken ct = default)
    {
        if (input.VehicleId == Guid.Empty) throw new ValidationException("Araç seçilmelidir.");
        if (input.GirisKm < 0) throw new ValidationException("Giriş KM negatif olamaz.");
        if (input.KusurOrani is < 0 or > 1) throw new ValidationException("Kusur oranı 0 ile 1 arasında olmalıdır.");
        BilgiDogrula(input);
        var kalemler = input.Lines.Select(KalemHazirla).ToList();

        var record = new ServiceRecord
        {
            VehicleId = input.VehicleId,
            Tip = input.Tip,
            // FAZ-16: rezervasyon = planlanmış randevu; araç servise GİRMEZ (Create zaten araç
            // durumuna dokunmuyor), "Servise Al" ile Açık'a döner.
            Durum = input.Rezervasyon ? ServisDurum.Rezerve : ServisDurum.Acik,
            // Rezervasyonda giriş tarihi henüz GERÇEKLEŞMEDİ; listeyi randevu gününe göre
            // sıralayabilmek için plan başlangıcına düşürülür ("Servise Al" gerçek anla ezer).
            GirisTarihi = input.GirisTarihi
                ?? (input.Rezervasyon ? input.PlanBasTarihi : null)
                ?? DateTimeOffset.UtcNow,
            GirisKm = input.GirisKm,
            HasarSorumlu = input.HasarSorumlu,
            KusurOrani = input.KusurOrani,
            Lines = kalemler
        };
        if (input.Id is { } id && id != Guid.Empty) record.Id = id; // F9.1: Idempotency-Key → PK
        BilgiUygula(record, input);
        await _repository.CreateAsync(record, ct);
        return record.Id;
    }

    /// <summary>
    /// FAZ-16 — servis kaydının BİLGİ bloklarını (kaza/fatura/ödeme/yakıt/plan) günceller.
    /// <para><b>Defter etkisi YOKTUR</b> — bilinçli (KARARLAR.md FAZ-16). Fatura genellikle servis
    /// bittikten sonra gelir; bu yüzden kapanmış kayıtta da güncellenebilir. Durum/KM/işçilik/
    /// yansıtma alanları <see cref="ServiceRecordBilgiInput"/>'ta OLMADIĞI için bu yolla değişemez.</para>
    /// </summary>
    public Task<bool> BilgiGuncelleAsync(Guid id, ServiceRecordBilgiInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        BilgiDogrula(input);
        return _repository.UpdateBilgiAsync(id, r => BilgiUygula(r, input), ct);
    }

    /// <summary>F9.1 — opaque row version for the full-replacement PUT of <c>/api/ui</c> (bilgi blokları).</summary>
    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default)
        => RowVersionStoreGuard.Require(rowVersions).GetVersionAsync<ServiceRecord>(id, ct);

    /// <summary>F9.1 — <see cref="BilgiGuncelleAsync"/> under a row lock with a version check (409 <c>cakisma</c>).
    /// Same whitelist: only the FAZ-16 information blocks; no ledger effect.</summary>
    public Task<bool> BilgiGuncelleVersionedAsync(Guid id, ServiceRecordBilgiInput input, string expectedVersion,
        CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        BilgiDogrula(input);
        return RowVersionStoreGuard.Require(rowVersions).UpdateAsync<ServiceRecord>(id, expectedVersion, r =>
        {
            BilgiUygula(r, input);
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, "Kayıt zaten var.", ct);
    }

    /// <summary>
    /// FAZ-16 — "Servise Al": Rezerve → Açık. Randevu gerçekleşti; GERÇEK giriş anı ve (verildiyse)
    /// giriş KM'si o an yazılır — plan penceresi (PlanBas/PlanBit) DEĞİŞMEZ ki plan-gerçek farkı
    /// ölçülebilsin. Araç durumu burada değişmez (Açık = "sırada"; araç ancak "Servise Başla" ile
    /// Serviste'ye geçer — mevcut davranışla birebir).
    /// </summary>
    public Task<bool> ServiseAlAsync(Guid id, int? girisKm = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (girisKm is < 0) throw new ValidationException("Giriş KM negatif olamaz.");
        return _repository.TransitionAsync(id, r =>
        {
            if (r.Durum != ServisDurum.Rezerve)
                throw new ValidationException("Yalnız 'Rezerve' kayıt servise alınabilir.");
            r.Durum = ServisDurum.Acik;
            r.GirisTarihi = DateTimeOffset.UtcNow;
            if (girisKm is int km) r.GirisKm = km;
        }, setVehicleTo: null, onlyWhenVehicleIs: null, ct: ct);
    }

    /// <summary>Açık → Serviste; araç Serviste'ye geçer.</summary>
    public Task<bool> BaslatAsync(Guid id, CancellationToken ct = default)
        => _repository.TransitionAsync(id, r =>
        {
            // Rezerve buraya DÜŞEMEZ: önce "Servise Al" ile Açık'a gelmesi gerekir (randevu
            // gerçekleşmeden araç bakımda görünmesin).
            if (r.Durum != ServisDurum.Acik)
                throw new ValidationException("Yalnız 'Açık' servis başlatılabilir.");
            r.Durum = ServisDurum.Serviste;
        }, setVehicleTo: VehicleStatus.Serviste, onlyWhenVehicleIs: null, ct: ct);

    /// <summary>Serviste → Tamamlandi; çıkış KM/tarih, sonraki bakım KM; araç Musait'e döner.</summary>
    public Task<bool> TamamlaAsync(Guid id, int cikisKm, int? sonrakiBakimKm = null, CancellationToken ct = default)
        => _repository.TransitionAsync(id, r =>
        {
            if (r.Durum != ServisDurum.Serviste)
                throw new ValidationException("Yalnız 'Serviste' kayıt tamamlanabilir.");
            if (cikisKm < r.GirisKm)
                throw new ValidationException("Çıkış KM giriş KM'den küçük olamaz.");
            r.Durum = ServisDurum.Tamamlandi;
            r.CikisKm = cikisKm;
            r.CikisTarihi = DateTimeOffset.UtcNow;
            r.SonrakiBakimKm = sonrakiBakimKm;
        }, setVehicleTo: VehicleStatus.Musait, onlyWhenVehicleIs: VehicleStatus.Serviste,
        // FAZ 2.5: servis çıkış odometresi km zaman-serisine AYNI transaction'da düşer.
        kmLog: r => new VehicleKmLog
        { VehicleId = r.VehicleId, Tarih = r.CikisTarihi!.Value, Km = cikisKm, Kaynak = KmLogKaynak.Servis },
        ct: ct);

    /// <summary>Rezerve/Açık/Serviste → Iptal; araç Serviste'den çıktıysa Musait'e döner.</summary>
    public Task<bool> IptalAsync(Guid id, CancellationToken ct = default)
    {
        // İnceltme sırasında bulunan AÇIK: bu metotta hiç guard yoktu — oturum açan herkes
        // (rol ne olursa olsun) servis kaydı iptal edebiliyordu. Diğer geçişler OperationsWrite
        // istiyordu; iptal artık OperationsDelete sınıfında.
        PermissionGuard.Require(_currentUser, Permission.OperationsDelete);
        return _repository.TransitionAsync(id, r =>
        {
            // Rezerve de iptal edilebilir (randevu iptali). İptal kayıtları bakım günü SAYILMAZ
            // (FAZ-76 düzeltmesi) — Rezerve de aynı şekilde sayılmaz.
            if (r.Durum is ServisDurum.Tamamlandi or ServisDurum.Iptal)
                throw new ValidationException("Kapanmış servis iptal edilemez.");
            r.Durum = ServisDurum.Iptal;
        }, setVehicleTo: VehicleStatus.Musait, onlyWhenVehicleIs: VehicleStatus.Serviste, ct: ct);
    }

    /// <summary>Serbest tutarlı kalem (eski imza — bileşensiz kullanım korunur).</summary>
    public Task<bool> KalemEkleAsync(Guid id, string aciklama, decimal tutar, CancellationToken ct = default)
        => KalemEkleAsync(id, new ServiceLineInput { Aciklama = aciklama, Tutar = tutar }, ct);

    /// <summary>
    /// FAZ-16 — kalem ekleme (birim fiyat/miktar/indirim/KDV bileşenleriyle). Tutar verilmezse
    /// bileşenlerden TÜRETİLİR; her hâlde KDV HARİÇ nettir (ToplamIscilik'in anlamı korunur).
    /// </summary>
    public Task<bool> KalemEkleAsync(Guid id, ServiceLineInput kalem, CancellationToken ct = default)
        => _repository.AddLineAsync(id, KalemHazirla(kalem), ct);

    /// <summary>
    /// Servis maliyetini hasar rücu olarak cari'ye yansıt (roadmap J4): DENGELİ defter — Borç Cari /
    /// Alacak Gelir. Yansıtılan = ToplamIscilik × KusurOrani. Yalnız Tamamlanmış + sorumlusu Müşteri/Sigorta +
    /// kusur>0 + henüz yansıtılmamış. FinanceWrite + dönem-kilidi + idempotency (SourceId=serviceId).
    /// <para>FAZ-16 notu: taban HÂLÂ <c>ToplamIscilik</c>'tir — yeni <c>FaturaTutar</c>/<c>Odeme</c>
    /// alanları bu hesaba GİRMEZ (bilgi alanı; girseydi rücu sessizce şişerdi).</para>
    /// </summary>
    public async Task YansitAsync(Guid serviceId, Guid cariId, DateTimeOffset? tarih = null,
        string? doviz = "TRY", decimal? kur = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (cariId == Guid.Empty) throw new ValidationException("Yansıtılacak cari seçilmelidir.");

        var rec = await _repository.FindAsync(serviceId, ct) ?? throw new ValidationException("Servis kaydı bulunamadı.");
        if (rec.Yansitildi) throw new ValidationException("Servis maliyeti zaten yansıtıldı.");
        if (rec.Durum != ServisDurum.Tamamlandi) throw new ValidationException("Yalnız tamamlanmış servis yansıtılabilir.");
        if (rec.HasarSorumlu is not (HasarSorumlu.Musteri or HasarSorumlu.Sigorta))
            throw new ValidationException("Yansıtma yalnız sorumlusu Müşteri/Sigorta olan hasarda yapılır.");
        if (rec.KusurOrani is not > 0m) throw new ValidationException("Kusur oranı pozitif olmalıdır.");

        var yansitilan = decimal.Round(rec.ToplamIscilik * rec.KusurOrani.Value, 2, MidpointRounding.AwayFromZero);
        if (yansitilan <= 0m) throw new ValidationException("Yansıtılacak tutar pozitif olmalıdır.");

        var entryDate = tarih ?? DateTimeOffset.UtcNow;
        await _lock.EnsureOpenAsync(entryDate, ct); // dönem kilidi

        // Kur çözümü (1.1): açık kur aynen; boş → TRY=1 / döviz KurService (yoksa net red).
        var cozulenKur = await _kurCozucu.CozAsync(doviz, kur, entryDate, ct);
        var money = new Money(yansitilan, (doviz ?? "TRY").Trim().ToUpperInvariant(), cozulenKur);
        var desc = $"Servis rücu {rec.No} (kusur %{rec.KusurOrani.Value * 100m:0.##})";
        await _repository.PostYansitmaAsync(serviceId, cariId, yansitilan,
        [
            new AccountLedgerEntry { EntryDateUtc = entryDate, AccountType = LedgerAccountType.Cari, AccountRef = cariId,
                Direction = LedgerDirection.Debit, Amount = money, SourceType = "ServisYansitma", SourceId = serviceId, Description = desc },
            new AccountLedgerEntry { EntryDateUtc = entryDate, AccountType = LedgerAccountType.Gelir, AccountRef = null,
                Direction = LedgerDirection.Credit, Amount = money, SourceType = "ServisYansitma", SourceId = serviceId, Description = desc }
        ], ct);
    }

    // ==================== FAZ-16 yardımcıları ====================

    private static string? Kirp(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Kalem girdisini doğrular ve kalıcı satıra çevirir (tutar gerekiyorsa türetilir).</summary>
    private static ServiceLine KalemHazirla(ServiceLineInput l)
    {
        if (string.IsNullOrWhiteSpace(l.Aciklama)) throw new ValidationException("Kalem açıklaması zorunludur.");
        if (l.BirimFiyat is < 0m) throw new ValidationException("Kalem birim fiyatı negatif olamaz.");
        if (l.Miktar is < 0m) throw new ValidationException("Kalem miktarı negatif olamaz.");
        if (l.Indirim is < 0m) throw new ValidationException("Kalem indirimi negatif olamaz.");
        if (l.KdvOran is < 0m or > 1m) throw new ValidationException("Kalem KDV oranı 0 ile 1 arasında olmalıdır.");

        // Miktar yalnız birim fiyatla anlam kazanır; verilmediyse 1 KALICI yazılır ki satırın
        // bileşenleri kendi içinde tutarlı olsun (ızgarada "boş × 250" görünmesin).
        var miktar = l.BirimFiyat is null ? l.Miktar : l.Miktar ?? 1m;

        var tutar = l.Tutar ?? (l.BirimFiyat is { } bf
            ? ServisKalemHesap.Net(bf, miktar, l.Indirim)
            : throw new ValidationException("Kalem tutarı ya da birim fiyat girilmelidir."));
        if (tutar < 0m)
            throw new ValidationException("Kalem tutarı negatif olamaz (indirim satır brütünü aşıyor).");

        var line = new ServiceLine
        {
            Aciklama = l.Aciklama.Trim(), Tutar = tutar,
            BirimFiyat = l.BirimFiyat, Miktar = miktar, Indirim = l.Indirim, KdvOran = l.KdvOran
        };
        if (l.Id is { } id && id != Guid.Empty) line.Id = id; // F9.1: Idempotency-Key → PK
        return line;
    }

    /// <summary>BİLGİ bloklarının doğrulaması. Para hareketi YOK — yalnız "saçma değer" reddi.</summary>
    private static void BilgiDogrula(ServiceRecordBilgiInput b)
    {
        if (b.DegerKaybi is < 0m) throw new ValidationException("Değer kaybı negatif olamaz.");
        if (b.FaturaTutar is < 0m) throw new ValidationException("Fatura tutarı negatif olamaz.");
        if (b.FaturaKdv is < 0m) throw new ValidationException("Fatura KDV'si negatif olamaz.");
        if (b.Odeme is < 0m) throw new ValidationException("Ödeme tutarı negatif olamaz.");
        if (b.OdemeKur is <= 0m) throw new ValidationException("Ödeme kuru pozitif olmalıdır.");
        if (Kirp(b.OdemeDoviz) is { Length: not 3 })
            throw new ValidationException("Ödeme dövizi 3 harfli olmalıdır (ör. TRY).");
        // Yakıt ölçeği kira sözleşmesiyle AYNI (0-12); iki ekranda iki ölçek olması karşılaştırmayı bozar.
        if (b.CikisYakit is < 0 or > 12) throw new ValidationException("Çıkış yakıt seviyesi 0 ile 12 arasında olmalıdır.");
        if (b.DonusYakit is < 0 or > 12) throw new ValidationException("Dönüş yakıt seviyesi 0 ile 12 arasında olmalıdır.");
        // Belge tarihleri geleceğe yazılamaz (TarihPolitikasi para-tarihi kuralı, 1 gün TZ toleransı).
        TarihPolitikasi.ParaTarihi(b.KazaTarihi, "Kaza");
        TarihPolitikasi.ParaTarihi(b.FaturaTarihi, "Fatura");
        TarihPolitikasi.ParaTarihi(b.OdemeTarihi, "Ödeme");
        // Plan penceresi GELECEĞE açıktır (randevu) — yalnız sıra kontrolü yapılır.
        if (b.PlanBasTarihi is { } pb && b.PlanBitTarihi is { } pt && pt < pb)
            throw new ValidationException("Plan bitiş tarihi başlangıçtan önce olamaz.");
    }

    private static void BilgiUygula(ServiceRecord r, ServiceRecordBilgiInput b)
    {
        r.AtolyeAdi = Kirp(b.AtolyeAdi);
        r.Aciklama = Kirp(b.Aciklama);

        r.BeyanTuru = Kirp(b.BeyanTuru);
        r.KarsiPlaka = Kirp(b.KarsiPlaka)?.ToUpperInvariant();
        r.KarsiTrafikSigortasi = Kirp(b.KarsiTrafikSigortasi);
        r.KazaTarihi = b.KazaTarihi;
        r.KazaSorumlusu = Kirp(b.KazaSorumlusu);
        r.HasarDosyaNo = Kirp(b.HasarDosyaNo);
        r.DegerKaybi = b.DegerKaybi;

        r.FaturaTarihi = b.FaturaTarihi;
        r.FaturaNo = Kirp(b.FaturaNo);
        r.FaturaTutar = b.FaturaTutar;
        r.FaturaKdv = b.FaturaKdv;
        // TÜRETİLİR (kullanıcıdan alınmaz): matrah + KDV. İkisi de boşsa toplam da boş kalır —
        // 0,00 yazmak "fatura var, tutarı sıfır" yalanı olurdu.
        r.FaturaGenelToplam = b.FaturaTutar is null && b.FaturaKdv is null
            ? null
            : ServisKalemHesap.Yuvarla((b.FaturaTutar ?? 0m) + (b.FaturaKdv ?? 0m));

        r.OdemeTarihi = b.OdemeTarihi;
        r.Odeme = b.Odeme;
        r.OdemeDoviz = Kirp(b.OdemeDoviz)?.ToUpperInvariant();
        r.OdemeKur = b.OdemeKur;
        r.OdemeTuru = b.OdemeTuru;
        r.KasaKodu = Kirp(b.KasaKodu);
        r.HesapNo = Kirp(b.HesapNo);

        r.CikisYakit = b.CikisYakit;
        r.DonusYakit = b.DonusYakit;
        r.PlanBasTarihi = b.PlanBasTarihi;
        r.PlanBitTarihi = b.PlanBitTarihi;
    }
}
