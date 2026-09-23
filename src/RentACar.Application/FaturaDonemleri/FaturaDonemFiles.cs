using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.FaturaDonemleri;

public interface IFaturaDonemRepository
{
    Task<IReadOnlyList<FaturaDonemi>> ListForRentalAsync(Guid rentalId, CancellationToken ct = default);

    /// <summary>Planı TEK transaction'da yeniler: verilen rental'ın PLANLANDİ satırları silinir,
    /// <paramref name="yeniPlanlar"/> eklenir. Kesildi/Atlandi satırlara DOKUNULMAZ (çağıran onların
    /// sıralarını yeni listeden çıkarmıştır).</summary>
    Task ReplacePlannedAsync(Guid rentalId, IReadOnlyList<FaturaDonemi> yeniPlanlar, CancellationToken ct = default);

    /// <summary>
    /// FAZ-30 — ELLE TETİKLEME adayları: vadesi gelmiş (DonemBit <= now) PLANLANDI dönemler ×
    /// KİRADA <b>ve DonemselFaturalama AÇIK</b> sözleşmeler. Şube kapsamı çağıranda uygulanır.
    ///
    /// <para><b>OPT-IN ÇİTİ ŞART (adversarial H1):</b> ilk sürümde bu bayrak sorulmuyordu ve
    /// periyodik faturalamayı hiç açmamış her uzun kira ekranda aday çıkıyordu — tek tıkla,
    /// kesilmemesi gereken sözleşmelerde değişmez fatura + tahsilat yazılabiliyordu. Job'un kapısı
    /// (<c>DonemFaturaUretici</c>) da budur; iki yol AYNI kuralı konuşmalı.</para>
    /// </summary>
    Task<IReadOnlyList<OtomatikTahsilatAdayi>> AdaylarAsync(
        OtomatikTahsilatFiltre filtre, CancellationToken ct = default);

    /// <summary>B2: kesilecek tahakkuku kalmayan dönemi ATLANDI işaretler (yalnız Planlandi→Atlandi;
    /// kalıcı iz — sonraki kesim denemesi gürültülü red).</summary>
    Task<bool> AtlandiIsaretleAsync(Guid donemId, CancellationToken ct = default);
}

/// <summary>FAZ-30 — elle tetikleme ekranının aday satırı (salt okuma; tutarlar bilgi).</summary>
public sealed record OtomatikTahsilatAdayi(
    Guid RentalId, string SozlesmeNo, int DonemSira, DateTimeOffset DonemBas, DateTimeOffset DonemBit,
    Guid CariId, string CariAd, string? Sube, Guid? SubeId, string Doviz, decimal KiraTutar,
    decimal CariBakiye);

/// <summary>FAZ-30 aday filtresi. Boş alan = kısıt yok.</summary>
public sealed class OtomatikTahsilatFiltre
{
    public string? SozlesmeNo { get; set; }
    public DateTimeOffset? VadeMin { get; set; }
    public DateTimeOffset? VadeMax { get; set; }
    /// <summary>true → yalnız cari bakiyesi BORÇLU (>0) olanlar.</summary>
    public bool SadeceBakiyeli { get; set; }
    /// <summary>Şube kapsamı (operatör kısıtı + kullanıcının seçtiği şube) — çağıran doldurur.</summary>
    public IReadOnlyCollection<Guid>? SubeIdler { get; set; }
    public string? SubeAdi { get; set; }
}

/// <summary>
/// Dönem kes + (opsiyonel) tahsilat orkestratörü (FAZ 4.2-B3) — endpoint ve testler AYNI akıştan
/// geçer. Tahsilat idempotency anahtarı DETERMİNİSTİK: CashService.RowKey(rentalId, donemSira) —
/// çift-submit ikinci tahsilatı yazamaz (kısmi unique index + yutma), fatura tarafı zaten idempotent
/// (Kesildi → mevcut InvoiceId). Tahsilat fatura DÖVİZ + KURUYLA kaydedilir (cari mutabakatı
/// kuruş-birebir). NOT: tahsilat sonrası fatura İADE edilirse tahsilat DURUR (para alındı) —
/// geri ödeme manuel "Ödeme" akışıyla (UI metninde).
/// </summary>
public sealed class DonemTahsilatService(
    Finance.InvoiceService invoices,
    Finance.CashService kasa,
    Finance.IInvoiceRepository invoiceRepo,
    Finance.ICashRepository kasaRepo)
{
    public async Task<Guid> KesVeTahsilEtAsync(
        Guid rentalId, int donemSira, bool tahsilatKaydi, LedgerAccountType hesap,
        CancellationToken ct = default)
        => (await KesVeTahsilEtDetayAsync(rentalId, donemSira, tahsilatKaydi, hesap, ct)).InvoiceId;

    /// <summary>
    /// <see cref="KesVeTahsilEtAsync"/> ile AYNI akış; ek olarak tahsilatın GERÇEKTEN yazılıp
    /// yazılmadığını bildirir. FAZ-30 adversarial M1: idempotent yutulan durumda çağıran
    /// "N tahsilat yazıldı" diyordu — sayaç yalan söylüyordu.
    /// </summary>
    public async Task<(Guid InvoiceId, bool TahsilatYazildi)> KesVeTahsilEtDetayAsync(
        Guid rentalId, int donemSira, bool tahsilatKaydi, LedgerAccountType hesap,
        CancellationToken ct = default)
    {
        var invId = await invoices.CreateDonemFaturasiAsync(rentalId, donemSira, ct: ct);
        if (!tahsilatKaydi) return (invId, false);

        var inv = await invoiceRepo.FindAsync(invId, ct)
            ?? throw new ValidationException("Dönem faturası okunamadı.");
        var anahtar = Finance.CashService.RowKey(rentalId, donemSira);
        try
        {
            await kasa.CollectAsync(new Finance.CashInput
            {
                CariId = inv.CariId,
                RentalId = rentalId,
                Tutar = inv.GenelToplam,
                Doviz = inv.Currency,
                Kur = inv.Kur,
                Hesap = hesap,
                Aciklama = $"Dönem {donemSira} tahsilatı ({inv.No})",
                IslemAnahtari = anahtar
            }, ct);
        }
        catch (ValidationException ex) when (ex.Message.Contains("zaten kaydedilmiş"))
        {
            // F4.4a adversarial MEDIUM-3: anahtar TAHMİN EDİLEBİLİR (kira kimliği ⊕ sıra). Başka bir işlem onu
            // önceden kullandıysa (ör. başka kiranın tahsilatına uydurma anahtar olarak verildiyse) bu dönemin
            // tahsilatı "daha önce alınmış" sayılıp SESSİZCE bastırılıyordu. Yalnız kayıt gerçekten bu kiranın
            // tahsilatıysa idempotent no-op; değilse gürültülü hata (fatura kesildi, tahsilat yazılmadı).
            var mevcut = await kasaRepo.FindByIslemAnahtariAsync(anahtar, ct);
            if (mevcut is null || mevcut.RentalId != rentalId || mevcut.Tip != CashTransactionType.Tahsilat)
                throw new ValidationException(
                    $"Dönem {donemSira} faturası kesildi ancak tahsilat yazılamadı: dönem tahsilat anahtarı başka bir " +
                    "kayıtta kullanılmış. Kayıtları kontrol edip tahsilatı ayrıca girin.");
            // R04 (Low temizliği B): anahtar tahmin edilebilir ve Blazor kasa formu ham IslemAnahtari kabul eder →
            // aynı kiraya FARKLI tutar/döviz/kurla bir tahsilat bu anahtarı ÖNDEN alabilir. Sessiz başarı yalnız
            // kayıt dönem faturasının tutarı + dövizi + kuruyla BİREBİR aynıysa (idempotency envanteri "sessiz
            // başarı kuralı"); değilse 409 farklı içerik — dönem tahsilatı YAZILMADI, gizlenmez.
            if (!AyniDonemTahsilati(mevcut, inv))
                throw new MukerrerIslemException(
                    $"Dönem {donemSira} faturası kesildi ancak tahsilat YAZILMADI: dönem tahsilat anahtarı farklı " +
                    $"içerikli bir tahsilatta (No {mevcut.No}, {mevcut.Amount.Amount:0.00} {mevcut.Amount.Currency}) " +
                    "kullanılmış. " + MukerrerIslemException.FarkliIcerikMesaji,
                    new MevcutIslem(mevcut.Id, mevcut.No, mevcut.Amount.Amount, mevcut.Amount.Currency, AyniIcerik: false));
            // Deterministik anahtar mükerreri = bu dönemin tahsilatı DAHA ÖNCE alınmış (çift-submit /
            // yeniden deneme) → idempotent no-op; fatura tarafı da idempotent olduğundan akış sessiz biter.
            return (invId, false);   // ÇAĞIRAN "yazıldı" saymasın (FAZ-30 M1)
        }
        return (invId, true);
    }

    /// <summary>R04: RowKey'li mevcut tahsilat bu dönem faturasının tahsilatıyla içerik olarak aynı mı? Tutar
    /// DB ölçeğinde (numeric(19,4)), kur numeric(19,6) hassasiyetinde karşılaştırılır; decimal eşitliği ölçekten
    /// bağımsızdır (300.0000 == 300).</summary>
    private static bool AyniDonemTahsilati(CashTransaction mevcut, Invoice inv) =>
        Math.Round(mevcut.Amount.Amount, 4, MidpointRounding.AwayFromZero)
            == Math.Round(inv.GenelToplam, 4, MidpointRounding.AwayFromZero)
        && string.Equals(mevcut.Amount.Currency, inv.Currency, StringComparison.OrdinalIgnoreCase)
        && Math.Round(mevcut.Amount.Rate, 6, MidpointRounding.AwayFromZero)
            == Math.Round(inv.Kur, 6, MidpointRounding.AwayFromZero);
}

/// <summary>Dönem önizleme satırı (B1): plan satırı + pro-rata tahakkuk (salt hesap; B2 kesimde
/// cap/fark mekanizması ayrıca devreye girer).</summary>
public sealed record FaturaDonemOnizleme(
    int DonemSira, DateTimeOffset DonemBas, DateTimeOffset DonemBit,
    FaturaDonemDurum Durum, decimal Tahakkuk, Guid? InvoiceId, decimal? KesilenTutar);

/// <summary>
/// Periyodik faturalama dönem PLANI (FAZ 4.2-B1; parasız — deftere/faturaya dokunmaz).
/// UYGUNLUK: KiralamaTuru "Uzun Kiralama"/"Aylık" (gerçek değer listesi; "Uzun Dönem" diye değer
/// YOK) VEYA Gun >= 28. Dönem tarihleri BasTar'ın GÜN-OF-AY ÇIPASIYLA üretilir (her dönem
/// BasTar.AddMonths(i) — 31 Oca çıpası 28 Şub'a kırpılır ama Mart'ta 31'e döner); son dönem
/// BitTar'da biter. Tahakkuk = GÜN-BAZLI pro-rata (son dönem = KALAN-YÖNTEMİ: Tutar − Σ önceki →
/// yuvarlama kayması yapısal SIFIR; Σ tahakkuk == Tutar invaryantı). Yalnız PLANLANDİ satırlar
/// yeniden üretilir; Kesildi/Atlandi sıralar korunur (uzatma planı büyütür, kesilmişe dokunmaz).
/// </summary>
public sealed class FaturaDonemPlanService(
    IFaturaDonemRepository repository,
    Bookings.IBookingRepository bookings,
    ICurrentUser currentUser)
{
    public static bool UygunMu(RentalContract c) =>
        string.Equals(c.KiralamaTuru?.Trim(), "Uzun Kiralama", StringComparison.OrdinalIgnoreCase)
        || string.Equals(c.KiralamaTuru?.Trim(), "Aylık", StringComparison.OrdinalIgnoreCase)
        || c.Gun >= 28;

    /// <summary>Ay-çıpalı dönem aralıkları: [Bas+i ay, min(Bas+(i+1) ay, Bit)); Bit'e ulaşınca durur.</summary>
    public static IReadOnlyList<(DateTimeOffset Bas, DateTimeOffset Bit)> DonemAraliklari(
        DateTimeOffset bas, DateTimeOffset bit)
    {
        var donemler = new List<(DateTimeOffset, DateTimeOffset)>();
        for (var i = 0; ; i++)
        {
            var dBas = bas.AddMonths(i);          // origin'den AddMonths → gün-of-ay çıpası korunur
            if (dBas >= bit) break;
            var dBit = bas.AddMonths(i + 1);
            donemler.Add((dBas, dBit < bit ? dBit : bit));
            if (dBit >= bit) break;
        }
        return donemler;
    }

    /// <summary>GÜN-BAZLI pro-rata tahakkuk; SON dönem kalan-yöntemi (Σ == tutar, kuruş-birebir).</summary>
    public static IReadOnlyList<decimal> ProRataAccrual(decimal tutar, IReadOnlyList<int> donemGunleri)
    {
        var toplamGun = donemGunleri.Sum();
        if (toplamGun <= 0 || donemGunleri.Count == 0) return [];
        var sonuc = new decimal[donemGunleri.Count];
        decimal dagitilan = 0m;
        for (var i = 0; i < donemGunleri.Count - 1; i++)
        {
            sonuc[i] = Math.Round(tutar * donemGunleri[i] / toplamGun, 2, MidpointRounding.AwayFromZero);
            dagitilan += sonuc[i];
        }
        sonuc[^1] = tutar - dagitilan; // kalan-yöntemi
        return sonuc;
    }

    /// <summary>Planı kurar/yeniler (create + uzatma sonrası çağrılır). Uygun değilse mevcut Planlandi
    /// satırlarını temizler (Kesildi/Atlandi kalır). İDEMPOTENT.</summary>
    public async Task EnsurePlanAsync(Guid rentalId, CancellationToken ct = default)
    {
        var c = await bookings.FindRentalAsync(rentalId, ct);
        if (c is null) return;

        var mevcut = await repository.ListForRentalAsync(rentalId, ct);
        var korunanSiralar = mevcut
            .Where(d => d.Durum != FaturaDonemDurum.Planlandi)
            .Select(d => d.DonemSira).ToHashSet();

        var yeniPlanlar = new List<FaturaDonemi>();
        if (UygunMu(c) && c.Durum != RentalStatus.Iptal)
        {
            var araliklar = DonemAraliklari(c.BasTar, c.BitTar);
            for (var i = 0; i < araliklar.Count; i++)
            {
                var sira = i + 1;
                if (korunanSiralar.Contains(sira)) continue; // Kesildi/Atlandi — dokunma
                yeniPlanlar.Add(new FaturaDonemi
                {
                    RentalId = rentalId, DonemSira = sira,
                    DonemBas = araliklar[i].Bas, DonemBit = araliklar[i].Bit
                });
            }
        }
        await repository.ReplacePlannedAsync(rentalId, yeniPlanlar, ct);
    }

    /// <summary>Dönem listesi + pro-rata tahakkuk önizlemesi (UI alt-sekmesi / job matematiğiyle ortak).</summary>
    public async Task<IReadOnlyList<FaturaDonemOnizleme>> PreviewAsync(Guid rentalId, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        var c = await bookings.FindRentalAsync(rentalId, ct)
            ?? throw new ValidationException("Kira sözleşmesi bulunamadı.");
        var satirlar = (await repository.ListForRentalAsync(rentalId, ct))
            .OrderBy(d => d.DonemSira).ToList();
        if (satirlar.Count == 0) return [];

        var gunler = satirlar
            .Select(d => Math.Max(1, (d.DonemBit.UtcDateTime.Date - d.DonemBas.UtcDateTime.Date).Days))
            .ToList();
        var tahakkuklar = ProRataAccrual(c.Tutar, gunler);
        return satirlar.Select((d, i) => new FaturaDonemOnizleme(
            d.DonemSira, d.DonemBas, d.DonemBit, d.Durum, tahakkuklar[i], d.InvoiceId, d.KesilenTutar)).ToList();
    }
}
