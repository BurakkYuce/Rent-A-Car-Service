using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Application.Penalties;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Common;
using static RentACar.Web.Api.FinansBelge.FinanceDocumentCommon;

namespace RentACar.Web.Api.FinansBelge;

/// <summary>Ceza yazma uçlarının gövdeleri (<see cref="PenaltyUiApi"/>).</summary>
internal static class PenaltyWrites
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public const string PaymentAlreadySaved = "Bu ceza ödemesi zaten kaydedildi ({0} TL, kalem {1}); yeni ödeme yazılmadı.";
    public const string PaymentOtherSaved =
        "Bu işlem anahtarıyla başka bir ceza ödemesi yazılmış ({0} TL); girdiğiniz ödeme YAZILMADI. Kayıtları kontrol edin.";

    /// <summary>Tek kalem tavanı (servisle aynı: 10^12).</summary>
    private const decimal LineUpperLimit = 1_000_000_000_000m;

    public static async Task<Results<Ok<DocumentResult>, ProblemHttpResult>> CreateAsync(
        PenaltyCreateRequest req, PenaltyService penalties, ICurrentUser user, IDbContextFactory<AppDbContext> dbf,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.CezaTuru)) throw new ValidationException("Ceza türü zorunludur.", "cezaTuru");
        Text(req.CezaTuru, 128, "cezaTuru");
        var lines = req.Kalemler ?? [];
        if (lines.Count == 0) throw new ValidationException("En az bir ceza kalemi girilmelidir.", "kalemler");
        if (lines.Count > 20) throw new ValidationException("Bir cezaya en fazla 20 kalem girilebilir.", "kalemler");
        for (var i = 0; i < lines.Count; i++)
        {
            Amount(lines[i].Tutar, $"kalemler[{i}].tutar");
            if (lines[i].Tutar >= LineUpperLimit)
                throw new ValidationException("Ceza kalemi tutarı makul sınırların dışında.", $"kalemler[{i}].tutar");
            Text(lines[i].Sebep, 512, $"kalemler[{i}].sebep");
        }
        if (req.VadeGun is < 0 or > 3650) throw new ValidationException("Vade günü 0 ile 3650 arasında olmalıdır.", "vadeGun");
        Text(req.Sebep, 512, "sebep");
        Text(req.Saat, 8, "saat");
        Text(req.Yer, 256, "yer");
        Text(req.CepTel, 32, "cepTel");
        Text(req.MakbuzNo, 64, "makbuzNo");
        Text(req.IslemSube, 128, "islemSube");

        // Varlık + kapsam: kira kazanır (cezanın kapsamı), yoksa araç. Şubeli kullanıcı ikisinden birini vermeli
        // — aksi halde kendisinin de göremeyeceği kiracı-geneli ceza açardı.
        await using (var db = await dbf.CreateDbContextAsync(ct))
        {
            await RequireCustomerAsync(db, req.CariId, "cariId", ct);
            var vehicle = await RequireVehicleAsync(db, req.AracId, "aracId", ct);
            var rental = await RequireRentalAsync(db, req.KiraId, "kiraId", ct);
            if (vehicle is { } vb) RequireInScope(user, vb);
            if (rental is { } rb) RequireInScope(user, rb);
            if (vehicle is null && rental is null && IsRestricted(user))
                throw new ValidationException("Şubeye bağlı kullanıcı cezayı bir kira ya da araçla ilişkilendirmelidir.", "aracId");
        }

        var id = await penalties.CreateAsync(new PenaltyInput
        {
            CezaTuru = req.CezaTuru.Trim(),
            TebligTarihi = F5Ortak.Utc(req.TebligTarihi),
            VadeGun = req.VadeGun ?? 15,
            VehicleId = req.AracId, CariId = req.CariId, RentalId = req.KiraId,
            Sebep = Trimmed(req.Sebep),
            Satirlar = lines.Select(l => new PenaltySatirInput { Tutar = l.Tutar, Sebep = Trimmed(l.Sebep) }).ToList(),
            Saat = Trimmed(req.Saat), Yer = Trimmed(req.Yer), CepTel = Trimmed(req.CepTel),
            MakbuzNo = Trimmed(req.MakbuzNo), IslemSube = Trimmed(req.IslemSube),
        }, ct);
        return TypedResults.Ok(new DocumentResult(id, (await penalties.GetAsync(id, ct))?.No ?? ""));
    }

    public static async Task<Results<Ok<PenaltyStateResult>, ProblemHttpResult>> ReflectAsync(
        Guid id, PenaltyService penalties, ICurrentUser user, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        await using (var db = await dbf.CreateDbContextAsync(ct))
            if (await PenaltyUiApi.LoadInScopeAsync(db, penalties, user, id, ct) is null)
                return F5Ortak.Bulunamadi("Ceza bulunamadı.");
        await penalties.ReflectAsync(id, ct);
        return TypedResults.Ok(new PenaltyStateResult(id, PenaltyStatus.Yansitildi.ToString()));
    }

    public static async Task<Results<Ok<PenaltyStateResult>, ProblemHttpResult>> CancelAsync(
        Guid id, PenaltyService penalties, ICurrentUser user, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        await using (var db = await dbf.CreateDbContextAsync(ct))
            if (await PenaltyUiApi.LoadInScopeAsync(db, penalties, user, id, ct) is null)
                return F5Ortak.Bulunamadi("Ceza bulunamadı.");
        await penalties.CancelAsync(id, ct);
        return TypedResults.Ok(new PenaltyStateResult(id, PenaltyStatus.Iptal.ToString()));
    }

    /// <summary>
    /// Kalem ödemesi (E26). Sıra: başlık → giriş sınırları → kapsam (404/403) → ANAHTARLA YAZILMIŞ ÖDEME (409 +
    /// mevcut; kaybolan yanıttan sonraki doğru tekrar bu yolla "zaten kaydedildi" alır — tarih/dönem kilidi gibi
    /// sonradan değişebilen kurallar ondan ÖNCE çalışmaz, envanter LOW-1 bu uçta kapalı) → servis.
    /// </summary>
    public static async Task<Results<Ok<PenaltyPaymentResult>, ProblemHttpResult>> PayAsync(
        Guid id, PenaltyPaymentRequest req, HttpContext http, PenaltyService penalties, ICurrentUser user,
        IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var key = IdempotencyBasligi.ZorunluAnahtar(http);
        if (req.SatirId == Guid.Empty) throw new ValidationException("Ödenecek ceza kalemi seçilmelidir.", "satirId");
        var account = Account(req.Hesap);
        OptionalAmount(req.Tutar, "tutar");
        Text(req.MakbuzNo, 64, "makbuzNo");
        Text(req.KasaKodu, 64, "kasaKodu");
        Text(req.HesapNo, 64, "hesapNo");
        Text(req.IslemYapan, 128, "islemYapan");
        Text(req.Aciklama, 512, "aciklama");

        await using (var db = await dbf.CreateDbContextAsync(ct))
        {
            if (await PenaltyUiApi.LoadInScopeAsync(db, penalties, user, id, ct) is not { } penalty)
                return F5Ortak.Bulunamadi("Ceza bulunamadı.");
            if (await db.PenaltyOdemeleri.AsNoTracking().FirstOrDefaultAsync(o => o.IslemAnahtari == key, ct) is { } o)
            {
                var same = o.PenaltyId == id && o.SatirId == req.SatirId && o.Hesap == account
                           && (req.Tutar is not { } t || Math.Round(t, 4, MidpointRounding.AwayFromZero) == o.Tutar);
                var amount = o.Tutar.ToString("N2", Tr);
                // belgeNo: ceza no + ödeme sırası (gider ödemesiyle aynı biçim "No/Sıra"); yalnız sıra ("1") hangi cezanın
                // ödemesi olduğunu söylemiyordu (#300 L2 eski). Yanıt alanı; yazma yolu değişmedi.
                throw new DuplicateOperationException(
                    same ? string.Format(Tr, PaymentAlreadySaved, amount, o.Sira) : string.Format(Tr, PaymentOtherSaved, amount),
                    o.PenaltyId == id ? new MevcutIslem(o.Id, $"{penalty.No}/{o.Sira}", o.Tutar, "TRY", same) : null);
            }
        }
        WithField("tarih", () => DatePolicy.MoneyDate(req.Tarih, "Ceza ödeme"));

        var r = await penalties.PayPartialAsync(id, new CezaOdemeInput
        {
            SatirId = req.SatirId, Tutar = req.Tutar, Hesap = account, Tarih = F5Ortak.Utc(req.Tarih),
            MakbuzNo = Trimmed(req.MakbuzNo), KasaKodu = Trimmed(req.KasaKodu), HesapNo = Trimmed(req.HesapNo),
            IslemYapan = Trimmed(req.IslemYapan), Aciklama = Trimmed(req.Aciklama), IslemAnahtari = key,
        }, ct);
        return TypedResults.Ok(new PenaltyPaymentResult(r.OdemeId, r.SatirId, r.Sira, r.Tutar, r.SatirKalan, r.CezaKalan,
            r.Durum.ToString()));
    }

    /// <summary>"Kasa" | "Banka" — başka her değer (boş dahil) alan hatası; sessizce Kasa'ya DÜŞMEZ (Blazor'un
    /// <c>HesapCoz</c>'u tanınmayanı Kasa'ya düşürüyordu).</summary>
    internal static LedgerAccountType Account(string? value) => value?.Trim() switch
    {
        { } h when string.Equals(h, "Kasa", StringComparison.OrdinalIgnoreCase) => LedgerAccountType.Kasa,
        { } h when string.Equals(h, "Banka", StringComparison.OrdinalIgnoreCase) => LedgerAccountType.Banka,
        _ => throw new ValidationException("Hesap Kasa ya da Banka olmalıdır.", "hesap"),
    };
}
