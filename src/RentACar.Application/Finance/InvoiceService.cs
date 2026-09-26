using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Integrations;
using RentACar.Application.Kur;
using RentACar.Application.Periods;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Finance;

/// <summary>
/// Fatura kesimi (dahili belge + e-Fatura stub). Kira sözleşmesinin GenelToplam'ından
/// (KDV-dahil brüt) net+KDV ayrıştırır, faturayı kesip cari'yi BORÇLANDIRIR:
///   Borç Cari (brüt) / Alacak Gelir (net) / Alacak KDV (kdv) — DENGELİ.
/// Böylece tahsilat sonrası cari bakiye sıfıra yakınsar (faturalı).
/// </summary>
public sealed class InvoiceService(
    IInvoiceRepository repository,
    IBookingRepository bookingRepository,
    RentACar.Application.RentalAddOns.IRentalAddOnRepository addOnRepository,
    IEInvoiceService eInvoice,
    ICurrentUser currentUser,
    IPeriodLockGuard periodLock,
    ExchangeRateService exchangeRate,
    VatDefault vatDefault,
    RentACar.Application.FaturaDonemleri.IInvoicePeriodRepository invoicePeriods)
{
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IPeriodLockGuard _lock = periodLock;

    public Task<IReadOnlyList<Invoice>> ListAsync(CancellationToken ct = default)
        => repository.ListAsync(ct);

    /// <summary>FAZ-54 — süzgeçli fatura listesi (cari/araç künyesi çözümlenmiş). Salt okuma.</summary>
    public Task<IReadOnlyList<InvoiceRow>> SearchAsync(InvoiceFilter? filter = null, CancellationToken ct = default)
        => repository.SearchAsync(filter, ct);

    /// <summary>
    /// FAZ-52 — fatura SATIRI seviyesinde birleştirilmiş liste (canlı fatura_detay_listesi.aspx).
    /// Salt okuma; <see cref="Permission.ViewReports"/> ister (fatura listesi gibi finans görünümü).
    /// </summary>
    public Task<IReadOnlyList<FaturaSatirDto>> ListLinesAsync(
        FaturaSatirFilter? filter = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ViewReports);
        return repository.ListLinesAsync(filter, ct);
    }

    public Task<Invoice?> GetAsync(Guid id, CancellationToken ct = default)
        => repository.FindAsync(id, ct);

    /// <summary>Bir kiranın faturaları (base+fark+iade; iptal dahil) — kira formu "Faturalar" alt-sekmesi.
    /// Salt-okuma; ListAsync ile tutarlı olarak guard'sız.</summary>
    public Task<IReadOnlyList<Invoice>> ListByRentalAsync(Guid rentalId, CancellationToken ct = default)
        => repository.ListByRentalAsync(rentalId, ct);

    /// <summary>
    /// FAZ-54 — <b>toplu faturalama</b>: seçili kiraları TEK istekle faturalar.
    ///
    /// <para><b>Spec'in bıraktığı iki karar mevcut tekil yolda ZATEN verilmiş; burada tekrar
    /// karar üretilmez:</b>
    /// <list type="bullet">
    /// <item><b>Faturalanabilirlik</b> = <see cref="CreateFromRentalAsync"/>'in kabul ettiği ölçüt
    /// (iptal değil, tutar &gt; 0, dövizli-kirada ek hizmet çakışması yok). Yeni bir ölçüt icat
    /// etmek iki yolu ayrıştırır ve toplu kesim tekil kesimin reddettiğini yazabilirdi.</item>
    /// <item><b>KDV/kur</b> = her kiranın KENDİ zinciri (parametre ?? kira özel oranı ?? net-mod
    /// snapshot ?? tenant varsayılanı) ve KENDİ dövizi. Parti başına tek kur uygulamak, net-mod
    /// snapshot guard'ını delip matrahı operatör niyetinden saptırırdı.</item>
    /// </list></para>
    ///
    /// <para><b>Neden ATOMİK DEĞİL (bilinçli):</b> her fatura BAĞIMSIZ bir mali belgedir ve
    /// boşluksuz numara alır. Tek-transaction hep-ya-hiç olsaydı, seçimdeki tek bozuk sözleşme
    /// (ör. dövizli + ek hizmetli) 19 geçerli faturayı da geri alırdı ve kullanıcı hangisinin
    /// bozuk olduğunu deneme-yanılma ile bulurdu. FAZ-30 (dönem faturası elle tetikleme) aynı
    /// gerekçeyle satır-bazlı çalışır; buradaki desen onunla AYNI: her kira tek tek işlenir,
    /// biri hata verirse diğerleri devam eder ve sonuçta "kaç kesildi / neler atlandı" döner.</para>
    ///
    /// <para><b>Çift-submit:</b> ayrı bir anahtar gerekmez — <see cref="CreateFromRentalAsync"/>
    /// zaten faturalanmış kirada FARK faturasına düşer, fark yoksa "zaten tam faturalanmış" diye
    /// reddeder. Yani ikinci gönderim yeni belge üretmez, atlananlar listesine yazılır.</para>
    /// </summary>
    public async Task<TopluFaturaSonuc> BatchCreateFromRentalsAsync(
        IReadOnlyCollection<Guid> rentalIds, decimal? vatRate = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (rentalIds.Count == 0) throw new ValidationException("En az bir kira seçilmelidir.");
        if (rentalIds.Count > MaxBulkSelection)
            throw new ValidationException($"Tek seferde en çok {MaxBulkSelection} kira faturalanabilir.");

        var issued = new List<Guid>();
        var skipped = new List<string>();
        foreach (var id in rentalIds.Distinct())
        {
            // Sözleşme no mesajlarda TAŞINIR: çok seçimli kesimde "kira bulunamadı" yazan üç satır
            // birbirinden ayırt edilemezdi (FAZ-30 dersi).
            var rental = await bookingRepository.FindRentalAsync(id, ct);
            var label = rental?.SozlesmeNo ?? id.ToString()[..8];
            if (rental is null) { skipped.Add($"{label}: kira bulunamadı (kapsam dışı olabilir)."); continue; }
            try
            {
                issued.Add(await CreateFromRentalAsync(id, vatRate, ct: ct));
            }
            // Beklenmedik bir hata partiyi ORTADA bırakıp 500 vermemeli: önceki faturalar zaten
            // yazıldı, kullanıcı ne kesildiğini görmeli (FAZ-30 ile aynı genişlik).
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                skipped.Add($"{label}: {ex.Message}");
            }
        }
        return new TopluFaturaSonuc(issued, skipped);
    }

    /// <summary>Tek seferde faturalanabilecek en fazla kira (kazara "hepsini seç" freni).</summary>
    public const int MaxBulkSelection = 200;

    public async Task<Guid> CreateFromRentalAsync(
        Guid rentalId, decimal? vatRate = null, InvoiceTaxInfo? tax = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        var rental = await bookingRepository.FindRentalAsync(rentalId, ct)
            ?? throw new ValidationException("Kira sözleşmesi bulunamadı.");
        // Adversarial B2-Orta-2: İPTAL kiraya fatura kesilemez (kes→iptal→iade zinciri bu deliği
        // operasyonel erişilir kılıyordu — iade sonrası iptal kiraya tam fatura yeniden kesilebiliyordu).
        if (rental.Durum == RentalStatus.Iptal)
            throw new ValidationException("İptal edilmiş kiraya fatura kesilemez.");
        if (rental.GenelToplam <= 0)
            throw new ValidationException("Faturalanacak tutar yok.");

        // PR-F3 adversarial Bulgu-1: NET fiyat modlu kira (Günlük/Toplam — Tutar %20 net üstünden brüte
        // çevrildi) FARKLI KDV oranıyla faturalanamaz; aksi halde faturadaki net matrah operatör niyetinden
        // sapar (grossup %20, ayrıştırma başka oran). Brüt modlarda serbest (girilen zaten brüt; oran yalnız
        // yeniden ayrıştırır → niyet korunur).
        var netMod = string.Equals(rental.FiyatTuru?.Trim(), "Günlük", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(rental.FiyatTuru?.Trim(), "Toplam", StringComparison.OrdinalIgnoreCase);
        // FAZ 1.4→3.A6 oran zinciri: parametre ?? kira-seviyesi özel oran ?? (net-modda SNAPSHOT —
        // gross-up hangi orandan yapıldıysa ayrıştırma da o orandan; eski kiralarda 0.20) ??
        // TENANT VARSAYILANI ?? 0.20. Net-mod guard'ı ZİNCİR SONUCUNA bakar ve SABİT 0.20 yerine
        // SNAPSHOT ile karşılaştırır — tenant oranı fiyatlama-fatura arasında değişse bile matrah
        // operatör niyetinden sapmaz (A6 doğrulanan tutarlılık tehlikesi).
        var netModeRate = rental.KdvOranSnapshot ?? VatMath.DefaultRate;
        var rate = vatRate ?? rental.OzelKdvOran
            ?? (netMod ? netModeRate : await vatDefault.RateAsync(ct));
        if (netMod && rate != netModeRate)
            throw new ValidationException(
                $"Net fiyat modlu kirada KDV oranı değiştirilemez (fiyat %{netModeRate * 100:0.##} net üstünden hesaplandı).");

        // Ek hizmet kalemleri: her biri KENDİ KDV oranını korur (farklı oranlar karışmaz).
        var addOns = await addOnRepository.ListForRentalAsync(rental.Id, ct);
        // Denetim M3 (ikinci savunma): ek hizmet tutarları TL; FX kirada kira dövizine karışıp ×Kur ile
        // deftere ŞİŞKİN gider (120 TL koltuk → "120 EUR" → 4.800 TL). Ekleme zaten guard'lı (O2); guard-öncesi
        // legacy addon'lu FX kira da FATURALANAMAZ — ek hizmetler kaldırılınca serbest.
        if (addOns.Count > 0 && ExchangeRateService.NormalizeCode(rental.Doviz) != "TRY")
            throw new ValidationException("Dövizli kirada ek hizmetli fatura desteklenmiyor (birimler karışır); önce ek hizmetleri kaldırın.");
        var addOnGross = addOns.Sum(a => a.Toplam);

        // Baz kira (ek hizmet hariç) brütü: GenelToplam'dan ÇIKARMA yerine doğrudan baz formülünden
        // (Tutar + dönüş bedelleri) hesaplanır → GenelToplam (SUM-türevi) bayatsa/yanlışsa bile fatura
        // doğru kalır (savunma derinliği). baseGross + Σ addon.Toplam = gerçek tam tutar.
        var baseGross = VatMath.RoundGross(RentACar.Application.Bookings.RentalTotals.BaseGross(rental));
        if (baseGross < 0)
            throw new ValidationException("Baz kira tutarı negatif olamaz.");

        // İdempotency + FARK FATURASI (PR-F1): kira zaten faturalandıysa, fatura SONRASI oluşan ek bedel
        // (dönüş fazla km/yakıt/uzatma) için FARK faturası kesilir — aksi halde sözleşme GenelToplam büyür
        // ama defter büyümez (sessiz ıraksama, adversarial P9). Fark = güncel toplam brüt − şimdiye dek
        // faturalanan brüt. İlk fatura değilse base+addon satırları yerine tek "fark" satırı (kira dövizi).
        if (await addOnRepository.IsRentalInvoicedAsync(rental.Id, ct))
        {
            var currentGross = VatMath.RoundGross(baseGross + addOnGross);
            // Faturalanan brüt + fark sayısı TEK ATOMİK snapshot'ta (TOCTOU yok — adversarial Kritik-1). İade
            // netlenir (High-2/3): iade edilmiş base "faturalanmış" sayılmaz → iade sonrası dönüş/yeniden-fatura
            // defteri sözleşmeyle hizalar. Sıra = fark sayısı + 1 (idempotency doğal anahtarı; V6 fark-iadesi).
            var (invoiced, differenceCount) = await repository.GetDifferenceStateAsync(rental.Id, ct);
            var difference = VatMath.RoundGross(currentGross - invoiced);
            // NOT (adversarial 1.4 Low): kira-seviyesi damga FARK'a KOPYALANMAZ — damga sözleşme-başı tek
            // puldur; base faturada uygulanır. Operatör parametreyle açıkça verirse aynen geçer.
            if (difference <= 0m)
                throw new ValidationException("Kira zaten tam faturalanmış (yeni ek bedel yok).");
            // BİLİNEN SINIR (adversarial B2-B3): fark TEK satırdır ve verilen orandan ayrışır — farklı
            // KDV oranlı add-on'lar son deltada baz orana düzleşir (brüt/cari kuruş-doğru; yalnız KDV
            // beyan kırılımı sapar). Ayrı-satırlı fark, fark mekanizmasının yeniden tasarımı → açık iş.
            return await PostDifferenceInvoiceAsync(rental, difference, differenceCount + 1, rate, tax, ct);
        }

        // FAZ 1.4 damga varsayılanı (YALNIZ base fatura — sözleşme-başı tek pul; fark'ta tekrarlanmaz):
        // parametrede damga yoksa kiradaki kullanılır (bilgi kolonu; boş alan=kiradaki, açık 0=damgasız).
        if (rental.DamgaVergisi is { } rentalStampDuty && (tax is null || tax.DamgaVergisi is null))
            tax = (tax ?? new InvoiceTaxInfo(null, null, null, null, false, false)) with { DamgaVergisi = rentalStampDuty };

        var (baseNet, baseVat) = VatMath.FromGross(baseGross, rate);

        var net = baseNet + addOns.Sum(a => a.NetTutar);
        var vat = baseVat + addOns.Sum(a => a.KdvTutar);
        var gross = net + vat; // denge: NetTutar + KdvTutar = GenelToplam (her zaman)

        // Kira dövizi → fatura o dövizde kesilir; kur FATURA ANINDA yakalanır (tenant sabit kuru varsa o,
        // yoksa TCMB). Ledger Money(amount, doviz, oran).AmountInBase = amount×oran ile OTOMATİK TL yazar.
        var invoiceDate = DateTimeOffset.UtcNow;
        var currency = ExchangeRateService.NormalizeCode(rental.Doviz);
        var rateValue = currency == "TRY" ? 1m : await exchangeRate.GetRateAsync(currency, invoiceDate, ct: ct);

        var invoice = new Invoice
        {
            Durum = InvoiceStatus.Kesildi,
            CariId = rental.MusteriId,
            RentalId = rental.Id,
            Tarih = invoiceDate,
            NetTutar = net,
            KdvTutar = vat,
            GenelToplam = gross,
            Currency = currency,
            Kur = rateValue
        };
        await _lock.EnsureOpenAsync(invoice.Tarih, ct); // dönem kilidi: kapalı döneme fatura kesilemez
        invoice.Lines.Add(new InvoiceLine
        {
            InvoiceId = invoice.Id,
            Aciklama = $"Kira sözleşmesi {rental.SozlesmeNo}",
            Miktar = 1m,
            BirimNetFiyat = baseNet,
            KdvOrani = rate,
            SatirNet = baseNet,
            SatirKdv = baseVat,
            SatirToplam = baseNet + baseVat
        });
        foreach (var a in addOns)
        {
            invoice.Lines.Add(new InvoiceLine
            {
                InvoiceId = invoice.Id,
                Aciklama = $"{a.Ad} (ek hizmet)",
                Miktar = a.Miktar,
                BirimNetFiyat = a.BirimNetFiyat,
                KdvOrani = a.KdvOrani,
                SatirNet = a.NetTutar,
                SatirKdv = a.KdvTutar,
                SatirToplam = a.Toplam
            });
        }

        // Vergi/belge metadata (bilgi amaçlı; defter postlamasına YANSIMAZ → denge bozulmaz).
        ApplyTax(invoice, tax);

        // e-Fatura stub (Faz 2'de gerçek): ETTN al. Para birimi faturanınki (denetim: hardcoded "TRY" idi —
        // gerçek GİB entegrasyonu geldiğinde FX fatura yanlış birimle giderdi).
        var result = await eInvoice.SendAsync(
            new EInvoiceRequest("", "", net, vat, invoice.Currency), ct);
        if (result.Success)
        {
            invoice.EFaturaEttn = result.Ettn;
            invoice.EFaturaGonderildi = true;
        }

        var entries = BuildEntries(invoice);
        await repository.PostAsync(invoice, entries, ct);
        return invoice.Id;
    }

    /// <summary>Fark faturası (PR-F1): kira zaten faturalandıktan SONRA oluşan ek bedel (dönüş/uzatma) için tek
    /// satırlık fatura. RentalId = null (kira-fatura unique index'ine çarpmasın); kira bağı KaynakKiraId üzerinden.
    /// fark = brüt (kira dövizi); net/kdv verilen orandan (dönüş bedelleri baz-oranlı). Dengeli defter yazar.</summary>
    /// <summary>FAZ 4.2-B2 — DÖNEM FATURASI: dönem sırasına kadar olan KÜMÜLATİF tahakkukun henüz
    /// faturalanmamış kısmını FARK MEKANİZMASI üzerinden keser (KaynakKiraId + KaynakKiraFarkSira
    /// unique → çift-faturalama yapısal imkânsız; GetFarkStateAsync dönem faturalarını da saydığından
    /// dönüş sonrası normal "Fatura Kes" kalan deltayı keser — kompozisyon bedava). Tutar =
    /// min(Σ tahakkuk[1..sıra], güncel BAZ brüt) − faturalanan; kesilecek kalmadıysa dönem ATLANDI
    /// işaretlenir + gürültülü red (erken dönüş/tam-fatura durumu). Tahakkuk YALNIZ BAZ kiradan —
    /// add-on'lar dönüş sonrası son deltada (FX kirada birim karışmaz). Fatura Tarih = now (kapalı
    /// döneme post edilmez); damga dönem faturasına uygulanmaz (sözleşme-başı tek pul ilkesi; dönem
    /// akışında bilinçli hiç). İDEMPOTENT: Kesildi dönem mevcut InvoiceId döner. SIRALI kesim.</summary>
    public async Task<Guid> CreatePeriodInvoiceAsync(
        Guid rentalId, int periodSequence, decimal? vatRate = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (vatRate is < 0m or > 1m)
            throw new ValidationException("KDV oranı kesir olmalı (0.20 = %20); 0-1 arası."); // adversarial N1 (footgun paritesi)
        var rental = await bookingRepository.FindRentalAsync(rentalId, ct)
            ?? throw new ValidationException("Kira sözleşmesi bulunamadı.");
        if (rental.Durum == RentalStatus.Iptal)
            throw new ValidationException("İptal edilmiş kiraya dönem faturası kesilemez.");

        var periods = (await invoicePeriods.ListForRentalAsync(rentalId, ct))
            .OrderBy(d => d.DonemSira).ToList();
        var period = periods.FirstOrDefault(d => d.DonemSira == periodSequence)
            ?? throw new ValidationException($"Dönem {periodSequence} bulunamadı (kira periyodik faturalamaya uygun olmayabilir).");
        if (period.Durum == InvoicePeriodStatus.Kesildi) return await ExistingPeriodInvoiceAsync(period.InvoiceId!.Value, vatRate, ct); // idempotent
        if (period.Durum == InvoicePeriodStatus.Atlandi)
            throw new ValidationException($"Dönem {periodSequence} atlanmış (kesilecek tahakkuk kalmamıştı).");
        if (periods.Any(d => d.DonemSira < periodSequence && d.Durum == InvoicePeriodStatus.Planlandi))
            throw new ValidationException("Dönemler sırayla kesilir — önce önceki dönem(ler) kesilmelidir.");

        // KDV zinciri + net-mod guard'ı CreateFromRentalAsync ile BİREBİR (snapshot tabanı — A6).
        var netMod = string.Equals(rental.FiyatTuru?.Trim(), "Günlük", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(rental.FiyatTuru?.Trim(), "Toplam", StringComparison.OrdinalIgnoreCase);
        var netModeRate = rental.KdvOranSnapshot ?? VatMath.DefaultRate;
        var rate = vatRate ?? rental.OzelKdvOran ?? (netMod ? netModeRate : await vatDefault.RateAsync(ct));
        if (netMod && rate != netModeRate)
            throw new ValidationException(
                $"Net fiyat modlu kirada KDV oranı değiştirilemez (fiyat %{netModeRate * 100:0.##} net üstünden hesaplandı).");

        // Kümülatif tahakkuk — B1 SAF matematiğiyle ORTAK (önizleme/manuel/job özdeş; uzatma-ortası
        // canlı Tutar + yenilenen plan günleriyle kalan-yöntemi kaymayı emer).
        var gunler = periods
            .Select(d => Math.Max(1, (d.DonemBit.UtcDateTime.Date - d.DonemBas.UtcDateTime.Date).Days)).ToList();
        var accruals = RentACar.Application.FaturaDonemleri.InvoicePeriodPlanService.ProRataAccrual(rental.Tutar, gunler);
        var cumulative = periods.Select((d, i) => (d.DonemSira, T: accruals[i]))
            .Where(x => x.DonemSira <= periodSequence).Sum(x => x.T);

        var periodBaseGross = VatMath.RoundGross(RentACar.Application.Bookings.RentalTotals.BaseGross(rental));
        var (invoiced, differenceCount) = await repository.GetDifferenceStateAsync(rental.Id, ct);
        var toIssue = VatMath.RoundGross(Math.Min(cumulative, periodBaseGross) - invoiced);
        if (toIssue <= 0m)
        {
            if (!await invoicePeriods.MarkSkippedAsync(period.Id, ct)) // kalıcı iz (cap; idempotent red)
            {
                // F1.4: işaretlenemediyse dönem artık Planlandi değil. Eşzamanlı ikinci gönderim, dönem
                // listesini ilk gönderim commit etmeden ÖNCE, faturalananı SONRA okuduysa buraya düşer —
                // dönem Kesildi ise sıralı ikinci istekle AYNI sessiz başarı (mevcut fatura id'si).
                var current = (await invoicePeriods.ListForRentalAsync(rentalId, ct))
                    .FirstOrDefault(d => d.DonemSira == periodSequence);
                if (current is { Durum: InvoicePeriodStatus.Kesildi, InvoiceId: Guid existing })
                    return await ExistingPeriodInvoiceAsync(existing, vatRate, ct);
            }
            throw new ValidationException($"Dönem {periodSequence} için kesilecek tahakkuk kalmadı — dönem ATLANDI işaretlendi.");
        }

        var (net, vat) = VatMath.FromGross(toIssue, rate);
        var date = DateTimeOffset.UtcNow;
        var currency = ExchangeRateService.NormalizeCode(rental.Doviz);
        var rateValue = currency == "TRY" ? 1m : await exchangeRate.GetRateAsync(currency, date, ct: ct);
        var invoice = new Invoice
        {
            Durum = InvoiceStatus.Kesildi,
            CariId = rental.MusteriId,
            RentalId = null,              // kira-fatura unique index'ine çarpmasın (fark deseni)
            KaynakKiraId = rental.Id,
            KaynakKiraFarkSira = differenceCount + 1,
            Tarih = date,
            NetTutar = net, KdvTutar = vat, GenelToplam = net + vat,
            Currency = currency, Kur = rateValue
        };
        await _lock.EnsureOpenAsync(invoice.Tarih, ct);
        invoice.Lines.Add(new InvoiceLine
        {
            InvoiceId = invoice.Id,
            Aciklama = $"Kira {rental.SozlesmeNo} — Dönem {periodSequence} ({period.DonemBas:dd.MM.yyyy} – {period.DonemBit:dd.MM.yyyy})",
            Miktar = 1m, BirimNetFiyat = net, KdvOrani = rate,
            SatirNet = net, SatirKdv = vat, SatirToplam = net + vat
        });

        var eResult = await eInvoice.SendAsync(new EInvoiceRequest("", "", net, vat, invoice.Currency), ct);
        if (eResult.Success) { invoice.EFaturaEttn = eResult.Ettn; invoice.EFaturaGonderildi = true; }

        var issued = await repository.PostPeriodAsync(invoice, BuildEntries(invoice), period.Id, toIssue, invoiced, ct);
        // Yarışı kaybeden istek kilit içinde Kesildi dönemi gördü → mevcut fatura (aynı içerik kuralı).
        return issued == invoice.Id ? issued : await ExistingPeriodInvoiceAsync(issued, vatRate, ct);
    }

    /// <summary>
    /// F1.4 — dönem zaten kesilmiş: aynı istek → mevcut fatura id'si (sessiz). Dönemin hedefi (kira, sıra)
    /// anahtarın kendisidir; farklı olabilecek tek girdi AÇIK verilmiş KDV oranıdır. Açık oran mevcut
    /// faturanınkinden farklıysa ikinci istek o oranla KESİLMEZ → sessiz başarı yerine 409.
    /// </summary>
    private async Task<Guid> ExistingPeriodInvoiceAsync(Guid existingId, decimal? vatRate, CancellationToken ct)
    {
        if (vatRate is { } rate)
        {
            var existing = await repository.FindAsync(existingId, ct);
            var stored = decimal.Round(rate, 4, MidpointRounding.AwayFromZero); // KdvOrani numeric(9,4)
            if (existing is null || existing.Lines.Any(l => l.KdvOrani != stored))
                throw DuplicateOperationException.DifferentContent();
        }
        return existingId;
    }

    private async Task<Guid> PostDifferenceInvoiceAsync(
        RentalContract rental, decimal differenceGross, int order, decimal rate, InvoiceTaxInfo? tax, CancellationToken ct)
    {
        var (net, vat) = VatMath.FromGross(differenceGross, rate);
        var gross = net + vat;
        var date = DateTimeOffset.UtcNow;
        var currency = ExchangeRateService.NormalizeCode(rental.Doviz);
        var rateValue = currency == "TRY" ? 1m : await exchangeRate.GetRateAsync(currency, date, ct: ct);

        var invoice = new Invoice
        {
            Durum = InvoiceStatus.Kesildi,
            CariId = rental.MusteriId,
            RentalId = null,              // kira-fatura unique index'ine çarpmasın
            KaynakKiraId = rental.Id,     // kira bağı
            KaynakKiraFarkSira = order,    // idempotency doğal anahtarı (eşzamanlı çift fark → çakışır)
            Tarih = date,
            NetTutar = net,
            KdvTutar = vat,
            GenelToplam = gross,
            Currency = currency,
            Kur = rateValue
        };
        await _lock.EnsureOpenAsync(invoice.Tarih, ct);
        invoice.Lines.Add(new InvoiceLine
        {
            InvoiceId = invoice.Id,
            Aciklama = $"Fark faturası (kira {rental.SozlesmeNo}) — dönüş/ek bedel",
            Miktar = 1m,
            BirimNetFiyat = net,
            KdvOrani = rate,
            SatirNet = net,
            SatirKdv = vat,
            SatirToplam = gross
        });
        ApplyTax(invoice, tax);

        var result = await eInvoice.SendAsync(new EInvoiceRequest("", "", net, vat, invoice.Currency), ct);
        if (result.Success) { invoice.EFaturaEttn = result.Ettn; invoice.EFaturaGonderildi = true; }

        await repository.PostAsync(invoice, BuildEntries(invoice), ct);
        return invoice.Id;
    }

    /// <summary>Vergi/belge metadata uygular (bilgi amaçlı). Negatif tutar / aralık dışı tevkifat reddedilir.
    /// Postlamaya DOKUNMAZ — fatura net/kdv/brüt ve dengeli kayıt aynı kalır.</summary>
    private static void ApplyTax(Invoice inv, InvoiceTaxInfo? v)
    {
        if (v is null) return;
        if (v.Otv is < 0m) throw new ValidationException("ÖTV negatif olamaz.");
        if (v.TevkifatTutar is < 0m) throw new ValidationException("Tevkifat tutarı negatif olamaz.");
        if (v.DamgaVergisi is < 0m) throw new ValidationException("Damga vergisi negatif olamaz.");
        if (v.TevkifatOran is < 0m or > 100m) throw new ValidationException("Tevkifat oranı 0 ile 100 arasında olmalıdır (%).");
        inv.Otv = v.Otv;
        inv.TevkifatOran = v.TevkifatOran;
        inv.TevkifatTutar = v.TevkifatTutar;
        inv.DamgaVergisi = v.DamgaVergisi;
        inv.IadeMi = v.IadeMi;
        inv.ManuelMi = v.ManuelMi;
    }

    /// <summary>
    /// Manuel/serbest fatura (kiradan bağımsız, roadmap G2). DENGELİ defter: Borç Cari / Alacak Gelir + KDV
    /// (kira faturasıyla aynı yön/semantik). Dönem-kilidi guard + opsiyonel idempotency (IslemAnahtari →
    /// çift-submit aynı faturayı döner). NOT: iade faturası (ledger ters) gelir/KDV rapor netleştirmesi
    /// gerektirdiğinden ayrı dikkatli artışa ertelendi (B2'deki dokümante limitle aynı kapsam).
    /// </summary>
    public async Task<Guid> CreateManualAsync(ManualInvoiceInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (input.CariId == Guid.Empty) throw new ValidationException("Cari seçilmelidir.");
        if (input.NetTutar <= 0) throw new ValidationException("Net tutar pozitif olmalıdır.");
        if (input.KdvOrani is < 0m or > 1m) throw new ValidationException("KDV oranı kesir olmalı (0.20 = %20); 0-1 arası."); // adversarial Low: %500 footgun

        var (vat, gross) = VatMath.FromNet(input.NetTutar, input.KdvOrani);
        var net = VatMath.RoundGross(input.NetTutar);
        // #286 adversarial M2: kontrol YUVARLAMADAN SONRA — 0,001 net önce kabul edilip 0,00 tutarlı, seri
        // numaralı ve değiştirilemez fatura kesiliyordu (Blazor manuel fatura formu da bu yoldan geçer).
        if (net <= 0) throw new ValidationException("Net tutar kuruşa yuvarlandığında pozitif olmalıdır.");

        // İdempotency: anahtar verilmiş + zaten kesilmişse aynı faturayı döndür (çift-submit güvenli).
        // F1.4 (adversarial MEDIUM-1): YALNIZ aynı cari + aynı tutarlar için. Aynı anahtar başka cari/tutarla
        // gelirse ikinci fatura KESİLMEZ ama eskiden ilkinin id'si sessizce dönüyordu (kullanıcı "kesildi"
        // görüyordu) → artık 409.
        if (input.IslemAnahtari is { } key && key != Guid.Empty && await repository.FindAsync(key, ct) is { } existing)
        {
            if (existing.ManuelMi && !existing.IadeMi && existing.RentalId is null && existing.KaynakKiraId is null
                && existing.CariId == input.CariId && existing.NetTutar == net && existing.KdvTutar == vat
                && string.Equals(existing.Currency, "TRY", StringComparison.OrdinalIgnoreCase))
                return key;
            throw DuplicateOperationException.DifferentContent();
        }
        var invoice = new Invoice
        {
            Id = input.IslemAnahtari is { } k && k != Guid.Empty ? k : Guid.NewGuid(),
            Durum = InvoiceStatus.Kesildi,
            CariId = input.CariId,
            RentalId = null,
            Tarih = input.Tarih ?? DateTimeOffset.UtcNow,
            VadeTarihi = input.VadeTarihi,
            NetTutar = net,
            KdvTutar = vat,
            GenelToplam = net + vat,
            Currency = "TRY",
            Kur = 1m,
            ManuelMi = true,
            IslemSube = input.IslemSube,
            EvrakNo = input.EvrakNo,
            FaturaOzelKod = input.FaturaOzelKod,
            OdemeTuru = input.OdemeTuru,
            GonderimSekli = input.GonderimSekli,
            KdvSifirSebep = input.KdvSifirSebep
        };
        await _lock.EnsureOpenAsync(invoice.Tarih, ct); // dönem kilidi: kapalı döneme manuel fatura YOK
        invoice.Lines.Add(new InvoiceLine
        {
            InvoiceId = invoice.Id,
            Aciklama = input.Aciklama ?? "Manuel fatura",
            Miktar = 1m, BirimNetFiyat = net, KdvOrani = input.KdvOrani,
            SatirNet = net, SatirKdv = vat, SatirToplam = net + vat
        });

        // Vergi/belge metadata (bilgi amaçlı; defter postlamasına YANSIMAZ — BuildEntries dokunulmadı).
        // ApplyVergi IadeMi/ManuelMi'yi de v'den yazar; manuel uçta bu SERVİS invariant'ıdır (formdan
        // GELMEZ — ManualInvoiceInput.Vergi'de IadeMi/ManuelMi kullanılmıyor) → ApplyVergi SONRASI
        // yeniden zorlanır (olası override'a karşı savunma derinliği).
        ApplyTax(invoice, input.Vergi);
        invoice.ManuelMi = true;
        invoice.IadeMi = false;

        await repository.PostAsync(invoice, BuildEntries(invoice), ct);
        return invoice.Id;
    }

    /// <summary>
    /// İade faturası (tam-fatura, roadmap küçük borç). Kaynak faturayı TERS kayıtla geri alır:
    /// Alacak Cari (brüt) / Borç Gelir (net) / Borç KDV (kdv) — DENGELİ. Kaynak satırları
    /// KDV-oranı başına aynalanır (KDV raporu oran-bazında netleşsin). FinanceWrite + dönem-kilidi +
    /// kaynak başına TEK iade (app ön-kontrol + DB kısmi-unique index). iade.RentalId = null
    /// (kira-fatura index'ine çarpmasın); kira bağı KaynakFaturaId üzerinden.
    /// </summary>
    public async Task<Guid> CreateRefundAsync(Guid sourceInvoiceId, DateTimeOffset? date = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceReverse); // inceltme: iade defteri geri sarar

        var src = await repository.FindAsync(sourceInvoiceId, ct)
            ?? throw new ValidationException("Kaynak fatura bulunamadı.");
        if (src.IadeMi) throw new ValidationException("İade faturası tekrar iade edilemez.");
        if (src.Durum == InvoiceStatus.Iptal) throw new ValidationException("İptal fatura iade edilemez.");
        if (await repository.RefundExistsForAsync(sourceInvoiceId, ct))
            throw new ValidationException("Bu fatura zaten iade edilmiş.");

        var date2 = date ?? DateTimeOffset.UtcNow;
        await _lock.EnsureOpenAsync(date2, ct); // dönem kilidi: kapalı döneme iade YOK

        var refund = new Invoice
        {
            Id = Guid.NewGuid(),
            Durum = InvoiceStatus.Kesildi,
            CariId = src.CariId,
            RentalId = null, // kira-fatura unique index'ine çarpmasın; kira bağı KaynakFaturaId
            KaynakFaturaId = src.Id,
            IadeMi = true,
            ManuelMi = src.ManuelMi,
            Tarih = date2,
            NetTutar = src.NetTutar,
            KdvTutar = src.KdvTutar,
            GenelToplam = src.GenelToplam,
            Currency = src.Currency,
            Kur = src.Kur
        };
        // Kaynak satırlarını KDV-oranı koruyarak aynala (KDV raporu IadeMi ile bunları negatifler).
        foreach (var l in src.Lines)
            refund.Lines.Add(new InvoiceLine
            {
                InvoiceId = refund.Id, Aciklama = $"İade: {l.Aciklama}", Miktar = l.Miktar,
                BirimNetFiyat = l.BirimNetFiyat, KdvOrani = l.KdvOrani,
                SatirNet = l.SatirNet, SatirKdv = l.SatirKdv, SatirToplam = l.SatirToplam
            });

        await repository.PostAsync(refund, BuildRefundEntries(refund), ct);
        return refund.Id;
    }

    /// <summary>Alacak Cari (brüt) / Borç Gelir (net) / Borç KDV (kdv) — Fatura'nın TERSİ. DENGELİ.</summary>
    private static List<AccountLedgerEntry> BuildRefundEntries(Invoice inv)
    {
        AccountLedgerEntry Entry(LedgerAccountType type, Guid? reff, LedgerDirection dir, decimal amount) => new()
        {
            EntryDateUtc = inv.Tarih, AccountType = type, AccountRef = reff, Direction = dir,
            Amount = new Money(amount, inv.Currency, inv.Kur),
            SourceType = "FaturaIade", SourceId = inv.Id, Description = $"İade {inv.No}"
        };

        return
        [
            Entry(LedgerAccountType.Cari, inv.CariId, LedgerDirection.Credit, inv.GenelToplam),
            Entry(LedgerAccountType.Gelir, null, LedgerDirection.Debit, inv.NetTutar),
            Entry(LedgerAccountType.Kdv, null, LedgerDirection.Debit, inv.KdvTutar)
        ];
    }

    /// <summary>Borç Cari (brüt) / Alacak Gelir (net) / Alacak KDV (kdv). DENGELİ. FAZ 4.2-B4:
    /// DonemFaturaUretici (job) fatura defterini de BU kümeden üretir (tek kopya).</summary>
    public static List<AccountLedgerEntry> BuildEntries(Invoice inv)
    {
        AccountLedgerEntry Entry(LedgerAccountType type, Guid? reff, LedgerDirection dir, decimal amount) => new()
        {
            EntryDateUtc = inv.Tarih, AccountType = type, AccountRef = reff, Direction = dir,
            Amount = new Money(amount, inv.Currency, inv.Kur),
            SourceType = "Fatura", SourceId = inv.Id, Description = $"Fatura {inv.No}"
        };

        return
        [
            Entry(LedgerAccountType.Cari, inv.CariId, LedgerDirection.Debit, inv.GenelToplam),
            Entry(LedgerAccountType.Gelir, null, LedgerDirection.Credit, inv.NetTutar),
            Entry(LedgerAccountType.Kdv, null, LedgerDirection.Credit, inv.KdvTutar)
        ];
    }
}
