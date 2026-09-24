using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using RentACar.Application.FaturaDonemleri;
using RentACar.Application.Finance;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Dönemsel fatura JOB üreticisi (FAZ 4.2-B4; VadeBildirimUretici kimlik deseni — servis/PermissionGuard
/// yüzeyi genişletilmez, doğrudan-context + açık TenantId damgası). Kesim koşulu: dönem Planlandi ∧
/// DonemBit ≤ now ∧ kira Kirada ∧ RentalContract.DonemselFaturalama ∧ tenant ayarı DonemselFaturalamaJob.
/// PARA MATEMATİĞİ MANUEL YOLLA ÖZDEŞ (tek kopya): tahakkuk FaturaDonemPlanService.ProRataAccrual,
/// fatura defteri InvoiceService.BuildEntries, tahsilat defteri CashService.Natural + CashRepository
/// .ApplyRentalDeltaAsync, idempotency anahtarı CashService.RowKey(rentalId, donemSira). Kesim fark
/// formatında (KaynakKiraId + sıra unique) + advisory kira-fatura kilidi + TX-içi faturalanan yeniden
/// doğrulaması (B2 ile aynı savunmalar). FX kirası ATLANIR + loglanır (kur çözümü servis işi — manuel
/// kesilir); kilitli muhasebe dönemi tenant'ı atlatır (log). Oto-tahsilat yalnız DonemselOtomatikTahsilat
/// açıkken (default kapalı — kasa gerçekliği).
/// </summary>
public static class DonemFaturaUretici
{
    public sealed record Sonuc(int Kesilen, int Tahsilat, IReadOnlyList<string> Atlananlar);

    public static async Task<Sonuc> RunAsync(AppDbContext db, Guid tenantId, DateTimeOffset now, CancellationToken ct = default)
    {
        var atlananlar = new List<string>();
        var ayar = await db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (ayar is not { DonemselFaturalamaJob: true }) return new Sonuc(0, 0, atlananlar);

        // Muhasebe dönem kilidi: fatura Tarih=now — kapalıysa TÜM tenant atlanır (oracle).
        var kilit = await db.DonemKilitleri.AsNoTracking()
            .OrderByDescending(k => k.KapanisTarihi).FirstOrDefaultAsync(ct);
        // F8.1a R2-M1: manuel yollarla AYNI kural (PeriodLock, İstanbul takvim günü). UTC günüyle karşılaştırma,
        // İstanbul gece yarısı (önceki gün 21:00Z) olarak yazılan kilidi bir gün erken okuyup kapalı güne fatura yazdırıyordu.
        if (kilit?.KapanisTarihi is { } kapanis && Application.Periods.PeriodLock.IsClosed(now, kapanis))
        {
            atlananlar.Add($"tenant {tenantId}: muhasebe dönemi {Application.Periods.PeriodLock.LocalDay(kapanis):yyyy-MM-dd} tarihine dek kilitli");
            return new Sonuc(0, 0, atlananlar);
        }

        var tenantKdv = ayar.VarsayilanKdvOrani is >= 0m and <= 1m ? ayar.VarsayilanKdvOrani.Value : KdvMath.VarsayilanOran;

        // Adaylar: vadesi gelmiş Planlandi dönemler × job'a açık Kirada kiralar.
        var adaylar = await (
            from d in db.FaturaDonemleri.AsNoTracking()
            join r in db.Rentals.AsNoTracking() on d.RentalId equals r.Id
            where d.Durum == FaturaDonemDurum.Planlandi && d.DonemBit <= now
                  && r.Durum == RentalStatus.Kirada && r.DonemselFaturalama
            orderby d.RentalId, d.DonemSira
            select new { Donem = d, Rental = r }).ToListAsync(ct);

        var kesilen = 0; var tahsilatSayisi = 0;
        foreach (var a in adaylar)
        {
            // FX kirası: kur çözümü servis katmanının işi (KurCozucu) — job atlar, manuel kesilir.
            if (RentACar.Application.Kur.KurService.NormalizeKod(a.Rental.Doviz) != "TRY")
            {
                atlananlar.Add($"kira {a.Rental.SozlesmeNo} dönem {a.Donem.DonemSira}: FX ({a.Rental.Doviz}) — manuel kesim");
                continue;
            }
            try
            {
                var ok = await DonemKesAsync(db, tenantId, a.Rental.Id, a.Donem.Id, a.Donem.DonemSira, tenantKdv, now,
                    ayar.DonemselOtomatikTahsilat, ct);
                if (ok.kesildi) kesilen++;
                if (ok.tahsilat) tahsilatSayisi++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                atlananlar.Add($"kira {a.Rental.SozlesmeNo} dönem {a.Donem.DonemSira}: {ex.Message}");
            }
        }
        return new Sonuc(kesilen, tahsilatSayisi, atlananlar);
    }

    private static async Task<(bool kesildi, bool tahsilat)> DonemKesAsync(
        AppDbContext db, Guid tenantId, Guid rentalId, Guid donemId, int donemSira,
        decimal tenantKdv, DateTimeOffset now, bool otomatikTahsilat, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await KilitAsync(db, tenantId, rentalId, ct); // B2 advisory kilidiyle AYNI anahtar

            // Adversarial B4-Yüksek-1: RunAsync tüm adaylarda AYNI context'i kullanır — önceki aday
            // TX'inde track edilen FaturaDonemi, EF identity-resolution yüzünden BAYAT Durum döndürüp
            // Planlandi çitini deliyordu (kesilmiş dönem Atlandi'ye yazılıyordu). Kilit alındıktan
            // sonra tracker temizlenir → tüm okumalar taze DB durumundan.
            db.ChangeTracker.Clear();

            var rental = await db.Rentals.AsNoTracking().FirstAsync(r => r.Id == rentalId, ct);
            // N4 (Low temizliği B): aday listesi kilitsiz okundu. Arada kira İPTAL edildiyse (iptal AYNI advisory
            // kilidi alır — KiraKilitleri.FaturaAsync — ve açık fatura varken reddeder), dönüş yapıldıysa, dönemsel
            // faturalama kapatıldıysa ya da döviz değiştiyse, kilit ALTINDA okunan taze satır aday koşulunu artık
            // sağlamaz → kesim YOK (tx dispose'da geri alınır; RunAsync atlananlara yazar). Önceden job iptal
            // kiraya değişmez dönem faturası (+ ayar açıksa tahsilat) yazabiliyordu.
            if (AdayDegilSebebi(rental) is { } sebep)
                throw new RentACar.Application.Common.ValidationException(sebep);
            var donemler = await db.FaturaDonemleri
                .Where(d => d.RentalId == rentalId).OrderBy(d => d.DonemSira).ToListAsync(ct);
            var donem = donemler.First(d => d.Id == donemId);
            if (donem.Durum != FaturaDonemDurum.Planlandi) { await tx.RollbackAsync(ct); return (false, false); }
            if (donemler.Any(d => d.DonemSira < donemSira && d.Durum == FaturaDonemDurum.Planlandi))
            { await tx.RollbackAsync(ct); return (false, false); } // sıralı kesim — önceki dönem gelecekte

            // KDV zinciri manuel yolla (CreateDonemFaturasiAsync) BİREBİR.
            var netMod = string.Equals(rental.FiyatTuru?.Trim(), "Günlük", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(rental.FiyatTuru?.Trim(), "Toplam", StringComparison.OrdinalIgnoreCase);
            var rate = rental.OzelKdvOran ?? (netMod ? rental.KdvOranSnapshot ?? KdvMath.VarsayilanOran : tenantKdv);

            // Kümülatif tahakkuk + cap — B1/B2 SAF matematiği (tek kopya).
            var gunler = donemler
                .Select(d => Math.Max(1, (d.DonemBit.UtcDateTime.Date - d.DonemBas.UtcDateTime.Date).Days)).ToList();
            var tahakkuklar = FaturaDonemPlanService.ProRataAccrual(rental.Tutar, gunler);
            var kumulatif = donemler.Select((d, i) => (d.DonemSira, T: tahakkuklar[i]))
                .Where(x => x.DonemSira <= donemSira).Sum(x => x.T);
            var baseGross = KdvMath.RoundGross(RentACar.Application.Bookings.RentalTotals.BaseGross(rental));
            var (faturalanan, farkSayisi) = await OrtakSorgular.FarkStateAsync(db, rentalId, ct);
            var kesilecek = KdvMath.RoundGross(Math.Min(kumulatif, baseGross) - faturalanan);
            if (kesilecek <= 0m)
            {
                // Savunma derinliği (B4-1): faturası olan dönem ASLA Atlandi'ye yazılmaz (çelişkili iz).
                if (donem.InvoiceId is not null) { await tx.RollbackAsync(ct); return (false, false); }
                donem.Durum = FaturaDonemDurum.Atlandi; // cap — kalıcı iz (manuel yolla aynı)
                donem.UpdatedAtUtc = now;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return (false, false);
            }

            var (net, kdv) = KdvMath.FromGross(kesilecek, rate);
            var invoice = new Invoice
            {
                TenantId = tenantId, // interceptor'sız job yolu → açık damga
                Durum = InvoiceStatus.Kesildi,
                CariId = rental.MusteriId,
                RentalId = null, KaynakKiraId = rentalId, KaynakKiraFarkSira = farkSayisi + 1,
                Tarih = now, NetTutar = net, KdvTutar = kdv, GenelToplam = net + kdv,
                Currency = "TRY", Kur = 1m
            };
            // NOT: e-Fatura stub'ı job yolunda çağrılmaz (kimliksiz bağlam; stub zaten no-op) —
            // gerçek GİB entegrasyonu geldiğinde job faturaları ayrı gönderim kuyruğuna alınmalı.
            invoice.No = await BelgeNoUretici.FaturaAsync(db, tenantId, ct, simdi: now);
            invoice.Lines.Add(new InvoiceLine
            {
                TenantId = tenantId, InvoiceId = invoice.Id,
                Aciklama = $"Kira {rental.SozlesmeNo} — Dönem {donemSira} ({donem.DonemBas:dd.MM.yyyy} – {donem.DonemBit:dd.MM.yyyy}) [job]",
                Miktar = 1m, BirimNetFiyat = net, KdvOrani = rate, SatirNet = net, SatirKdv = kdv, SatirToplam = net + kdv
            });
            var entries = InvoiceService.BuildEntries(invoice); // manuel yolla AYNI defter kümesi
            foreach (var e in entries) { e.TenantId = tenantId; e.Description = $"Fatura {invoice.No}"; }

            donem.Durum = FaturaDonemDurum.Kesildi;
            donem.InvoiceId = invoice.Id;
            donem.KesilenTutar = kesilecek;
            donem.UpdatedAtUtc = now;

            db.Invoices.Add(invoice);
            db.AccountLedgerEntries.AddRange(entries);

            // Oto-tahsilat (ayar açıksa): manuel B3 ile AYNI deterministik anahtar + AYNI defter kümesi.
            var tahsilatYazildi = false;
            if (otomatikTahsilat)
            {
                var ctx = new CashTransaction
                {
                    TenantId = tenantId,
                    Tip = CashTransactionType.Tahsilat,
                    CariId = rental.MusteriId, RentalId = rentalId,
                    Tarih = now, Amount = new Money(kesilecek, "TRY", 1m),
                    KarsiHesap = LedgerAccountType.Kasa,
                    Aciklama = $"Dönem {donemSira} tahsilatı ({invoice.No}) [job]",
                    IslemAnahtari = CashService.RowKey(rentalId, donemSira),
                    Kanal = RentACar.Domain.Entities.CashKanal.Masaustu // FAZ-84: otomatik iş — kanal seçilemez
                };
                // Job'un kendi "now"ı geçilir: belge günü ile job günü ayrışmasın.
                ctx.No = await BelgeNoUretici.UretAsync(db, tenantId, BelgeNoTuru.Tahsilat, ct, simdi: now);
                var cashEntries = CashService.Natural(ctx);
                foreach (var e in cashEntries) e.TenantId = tenantId;
                db.CashTransactions.Add(ctx);
                db.AccountLedgerEntries.AddRange(cashEntries);
                await Repositories.CashRepository.ApplyRentalDeltaAsync(db, ctx, ct);
                tahsilatYazildi = true;
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return (true, tahsilatYazildi);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Eşzamanlı manuel kesim / replika yarışı — sıra veya tahsilat anahtarı çakıştı → idempotent no-op.
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return (false, false);
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    /// <summary>N4: aday koşulunun kilit altındaki yeniden denetimi — RunAsync aday sorgusu ve FX çitiyle AYNI kural
    /// (Kirada ∧ DonemselFaturalama ∧ TRY). null = hâlâ aday.</summary>
    internal static string? AdayDegilSebebi(RentalContract r)
    {
        if (r.Durum == RentalStatus.Iptal) return "kira bu sırada iptal edildi — dönem faturası kesilmedi";
        if (r.Durum != RentalStatus.Kirada) return $"kira artık Kirada değil ({r.Durum}) — dönem faturası kesilmedi";
        if (!r.DonemselFaturalama) return "kirada dönemsel faturalama kapatıldı — dönem faturası kesilmedi";
        if (RentACar.Application.Kur.KurService.NormalizeKod(r.Doviz) != "TRY")
            return $"kira dövizi {r.Doviz} — manuel kesim";
        return null;
    }

    private static async Task KilitAsync(AppDbContext db, Guid tenantId, Guid rentalId, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@k, 42))";
        var p = cmd.CreateParameter();
        p.ParameterName = "k";
        p.Value = $"fatura:{tenantId}:{rentalId}"; // B2 kilidiyle AYNI anahtar biçimi
        cmd.Parameters.Add(p);
        await cmd.ExecuteScalarAsync(ct);
    }
}
