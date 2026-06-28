using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Integrations;
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
    ICurrentUser currentUser)
{
    private const decimal DefaultKdvRate = 0.20m;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<Invoice>> ListAsync(CancellationToken ct = default)
        => repository.ListAsync(ct);

    public Task<Invoice?> GetAsync(Guid id, CancellationToken ct = default)
        => repository.FindAsync(id, ct);

    public async Task<Guid> CreateFromRentalAsync(
        Guid rentalId, decimal? kdvRate = null, InvoiceTaxInfo? vergi = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        var rental = await bookingRepository.FindRentalAsync(rentalId, ct)
            ?? throw new ValidationException("Kira sözleşmesi bulunamadı.");
        if (rental.GenelToplam <= 0)
            throw new ValidationException("Faturalanacak tutar yok.");
        // İdempotency: aynı kira iki kez faturalanıp cari ÇİFT borçlanmasın (DB kısmi unique index
        // son güvence; bu ön-kontrol kullanıcı dostu hata verir).
        if (await addOnRepository.IsRentalInvoicedAsync(rental.Id, ct))
            throw new ValidationException("Kira zaten faturalanmış.");

        var rate = kdvRate ?? DefaultKdvRate;

        // Ek hizmet kalemleri: her biri KENDİ KDV oranını korur (farklı oranlar karışmaz).
        var addOns = await addOnRepository.ListForRentalAsync(rental.Id, ct);
        var addOnGross = addOns.Sum(a => a.Toplam);

        // Baz kira (ek hizmet hariç) brütü: GenelToplam'dan ÇIKARMA yerine doğrudan baz formülünden
        // (Tutar + dönüş bedelleri) hesaplanır → GenelToplam (SUM-türevi) bayatsa/yanlışsa bile fatura
        // doğru kalır (savunma derinliği). baseGross + Σ addon.Toplam = gerçek tam tutar.
        var baseGross = KdvMath.RoundGross(RentACar.Application.Bookings.RentalTotals.BaseGross(rental));
        if (baseGross < 0)
            throw new ValidationException("Baz kira tutarı negatif olamaz.");
        var (baseNet, baseKdv) = KdvMath.FromGross(baseGross, rate);

        var net = baseNet + addOns.Sum(a => a.NetTutar);
        var kdv = baseKdv + addOns.Sum(a => a.KdvTutar);
        var gross = net + kdv; // denge: NetTutar + KdvTutar = GenelToplam (her zaman)

        var invoice = new Invoice
        {
            Durum = InvoiceStatus.Kesildi,
            CariId = rental.MusteriId,
            RentalId = rental.Id,
            Tarih = DateTimeOffset.UtcNow,
            NetTutar = net,
            KdvTutar = kdv,
            GenelToplam = gross,
            Currency = "TRY",
            Kur = 1m
        };
        invoice.Lines.Add(new InvoiceLine
        {
            InvoiceId = invoice.Id,
            Aciklama = $"Kira sözleşmesi {rental.SozlesmeNo}",
            Miktar = 1m,
            BirimNetFiyat = baseNet,
            KdvOrani = rate,
            SatirNet = baseNet,
            SatirKdv = baseKdv,
            SatirToplam = baseNet + baseKdv
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
        ApplyVergi(invoice, vergi);

        // e-Fatura stub (Faz 2'de gerçek): ETTN al.
        var result = await eInvoice.SendAsync(
            new EInvoiceRequest("", "", net, kdv, "TRY"), ct);
        if (result.Success)
        {
            invoice.EFaturaEttn = result.Ettn;
            invoice.EFaturaGonderildi = true;
        }

        var entries = BuildEntries(invoice);
        await repository.PostAsync(invoice, entries, ct);
        return invoice.Id;
    }

    /// <summary>Vergi/belge metadata uygular (bilgi amaçlı). Negatif tutar / aralık dışı tevkifat reddedilir.
    /// Postlamaya DOKUNMAZ — fatura net/kdv/brüt ve dengeli kayıt aynı kalır.</summary>
    private static void ApplyVergi(Invoice inv, InvoiceTaxInfo? v)
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

    /// <summary>Borç Cari (brüt) / Alacak Gelir (net) / Alacak KDV (kdv). DENGELİ.</summary>
    private static List<AccountLedgerEntry> BuildEntries(Invoice inv)
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
