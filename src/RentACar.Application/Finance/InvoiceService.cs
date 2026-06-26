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
    IEInvoiceService eInvoice)
{
    private const decimal DefaultKdvRate = 0.20m;

    public Task<IReadOnlyList<Invoice>> ListAsync(CancellationToken ct = default)
        => repository.ListAsync(ct);

    public Task<Invoice?> GetAsync(Guid id, CancellationToken ct = default)
        => repository.FindAsync(id, ct);

    public async Task<Guid> CreateFromRentalAsync(Guid rentalId, decimal? kdvRate = null, CancellationToken ct = default)
    {
        var rental = await bookingRepository.FindRentalAsync(rentalId, ct)
            ?? throw new ValidationException("Kira sözleşmesi bulunamadı.");
        if (rental.GenelToplam <= 0)
            throw new ValidationException("Faturalanacak tutar yok.");

        var rate = kdvRate ?? DefaultKdvRate;
        var gross = rental.GenelToplam; // KDV-dahil
        var (net, kdv) = KdvMath.FromGross(gross, rate);

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
            BirimNetFiyat = net,
            KdvOrani = rate,
            SatirNet = net,
            SatirKdv = kdv,
            SatirToplam = gross
        });

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
