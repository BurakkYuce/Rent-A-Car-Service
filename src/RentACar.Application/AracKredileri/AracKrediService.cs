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
    RentACar.Application.Periods.IPeriodLockGuard periodLock, RentACar.Application.Kur.KurCozucu kurCozucu)
{
    private readonly IAracKrediRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly RentACar.Application.Periods.IPeriodLockGuard _lock = periodLock;
    private readonly RentACar.Application.Kur.KurCozucu _kurCozucu = kurCozucu;

    public Task<IReadOnlyList<AracKredi>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

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
        DateTimeOffset? odemeTarih = null, Guid? islemAnahtari = null, CancellationToken ct = default)
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
                Aciklama = desc,
                IslemAnahtari = anahtar
            };
            IReadOnlyList<AccountLedgerEntry> entries =
            [
                new AccountLedgerEntry { EntryDateUtc = tarih, AccountType = LedgerAccountType.Gider, AccountRef = kredi.VehicleId,
                    Direction = LedgerDirection.Debit, Amount = money, SourceType = "Gider", SourceId = expense.Id, Description = desc },
                new AccountLedgerEntry { EntryDateUtc = tarih, AccountType = hesap, AccountRef = null,
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

    /// <summary>Taksit planı + kalan bakiye (salt-hesap). Basit faiz: toplamFaiz = tutar × faiz × taksit/12;
    /// aylık taksit = toplam geri ödeme / taksit sayısı; kalan = toplam − ödenen.</summary>
    public static AracKrediOzet Hesapla(AracKredi k)
    {
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

        return new AracKrediOzet(toplamFaiz, toplamGeriOdeme, aylikTaksit, odenenTutar, kalanBakiye, taksitler);
    }

    private static decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
