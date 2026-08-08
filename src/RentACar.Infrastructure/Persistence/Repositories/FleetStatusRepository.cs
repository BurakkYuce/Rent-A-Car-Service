using Microsoft.EntityFrameworkCore;
using RentACar.Application.Fleet;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Araç Güncel Durum birleşik sorgusu. Araç filtreleri SQL'de uygulanır; aktif kira (Durum=Kirada)
/// + müşteri adı bellek içinde birleştirilir (tenant başına filo boyutu ölçülü). Tenant izolasyonu
/// RLS + query filter ile otomatik.
///
/// <para>FAZ-11: kiraya ek olarak açık servis, açık BAF tahsisi, sıradaki rezervasyon ve aktif
/// filo kiralama dosyası da satıra taşınır. Hepsi <b>yalnız listelenen araçlar</b> için ayrı ve
/// dar sorgularla çekilir (tek dev join yerine) — filo büyüdükçe kartezyen patlama olmasın.</para>
/// </summary>
public sealed class FleetStatusRepository(IDbContextFactory<AppDbContext> factory) : IFleetStatusRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<FleetStatusRow>> QueryAsync(FleetStatusFilter filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.Vehicles.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.Sube)) q = q.Where(v => v.Sube == filter.Sube);
        // C3 ŞABLON (BranchScope.InScope ile birebir): FK-eşit VEYA metin-eşit (Ordinal).
        if (!filter.Kapsam.Unrestricted)
        {
            var kid = filter.Kapsam.SubeId; var kad = filter.Kapsam.SubeAd;
            q = q.Where(v => (kid != null && v.SubeId == kid)
                          || ((kid == null || v.SubeId == null) && kad != null && v.Sube != null && v.Sube.Trim() == kad)); // C5
        }
        if (filter.Durum is { } d) q = q.Where(v => v.Durum == d);
        if (filter.FiloDurum is { } f) q = q.Where(v => v.FiloDurum == f);
        if (filter.Vites is { } vt) q = q.Where(v => v.Vites == vt);
        if (filter.Yakit is { } y) q = q.Where(v => v.Yakit == y);
        if (!string.IsNullOrWhiteSpace(filter.Grup)) q = q.Where(v => v.Grup == filter.Grup);
        if (!string.IsNullOrWhiteSpace(filter.Marka)) q = q.Where(v => v.Marka == filter.Marka);
        // FAZ-11 araç künyesi filtreleri (hepsi Vehicle'ın kendi kolonları → SQL'de).
        if (!string.IsNullOrWhiteSpace(filter.PasifSebep))
        {
            var ps = $"%{filter.PasifSebep.Trim()}%";
            q = q.Where(v => v.PasifSebep != null && EF.Functions.ILike(v.PasifSebep, ps));
        }
        if (!string.IsNullOrWhiteSpace(filter.HgsNo))
        {
            var hn = $"%{filter.HgsNo.Trim()}%";
            q = q.Where(v => (v.HgsNo != null && EF.Functions.ILike(v.HgsNo, hn))
                          || (v.OgsNo != null && EF.Functions.ILike(v.OgsNo, hn))); // etiket elde: HGS mi OGS mi bilinmez
        }
        if (!string.IsNullOrWhiteSpace(filter.TakipNo))
        {
            var tn = $"%{filter.TakipNo.Trim()}%";
            q = q.Where(v => v.TakipNo != null && EF.Functions.ILike(v.TakipNo, tn));
        }
        if (filter.KarLastigi is { } kl) q = q.Where(v => v.KarLastigi == kl);
        if (filter.WebRezKapat is { } wr) q = q.Where(v => v.WebRezKapat == wr);
        if (filter.OfisRezKapat is { } or) q = q.Where(v => v.OfisRezKapat == or);
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var term = $"%{filter.Query.Trim()}%";
            q = q.Where(v => EF.Functions.ILike(v.Plaka, term)
                || (v.Marka != null && EF.Functions.ILike(v.Marka, term)));
        }

        var vehicles = await q.OrderBy(v => v.Plaka).ToListAsync(ct);
        if (vehicles.Count == 0) return [];

        // Aktif kiralar (yalnız bu araçlar için) + müşteri adı.
        var vehicleIds = vehicles.Select(v => v.Id).ToList();
        var activeRentals = await db.Rentals.AsNoTracking()
            .Where(r => r.Durum == RentalStatus.Kirada && vehicleIds.Contains(r.VehicleId))
            .ToListAsync(ct);

        // Araç başına tek aktif kira (en geç başlayan — normalde tek olur).
        var rentalByVehicle = activeRentals
            .GroupBy(r => r.VehicleId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.BasTar).First());

        var now = DateTimeOffset.UtcNow;

        // Sıradaki rezervasyon: HENÜZ BİTMEMİŞ ve iptal/kiraya-çevrilmemiş olanlardan en erken başlayan.
        // (Devam eden bir rezervasyon da "sıradaki"dir — bitişi geçmişte olan artık gündemde değil.)
        var rezervler = await db.Reservations.AsNoTracking()
            .Where(r => vehicleIds.Contains(r.VehicleId)
                     && r.BitTar >= now
                     && (r.Durum == ReservationStatus.Rezerv || r.Durum == ReservationStatus.Onayli))
            .Select(r => new { r.VehicleId, r.MusteriId, r.BasTar })
            .ToListAsync(ct);
        var rezByVehicle = rezervler
            .GroupBy(r => r.VehicleId)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.BasTar).First());

        // Müşteri adı/telefonu TEK sorguda (kira + rezervasyon müşterileri birlikte).
        var custIds = activeRentals.Select(r => r.MusteriId)
            .Concat(rezervler.Select(r => r.MusteriId)).Distinct().ToList();
        // DisplayName hesaplanan bir property (SQL'e çevrilemez) → entity çekilir ve TEK kural
        // (Customer.DisplayName) kullanılır. Burada "Unvan varsa unvan" gibi bir kopya kural yazmak,
        // listede müşteri adının cari ekranındakinden farklı görünmesine yol açardı.
        var custs = await db.Customers.AsNoTracking()
            .Where(c => custIds.Contains(c.Id))
            .ToListAsync(ct);
        var custById = custs.ToDictionary(
            c => c.Id, c => (Ad: (string?)c.DisplayName, Tel: c.CepTel));

        // Açık servis kaydı (Acik veya Serviste) — araç başına en yenisi.
        var servisler = (await db.ServiceRecords.AsNoTracking()
                .Where(s => vehicleIds.Contains(s.VehicleId)
                         && (s.Durum == ServisDurum.Acik || s.Durum == ServisDurum.Serviste))
                .Select(s => new { s.VehicleId, s.No, s.AtolyeAdi, s.GirisTarihi })
                .ToListAsync(ct))
            .GroupBy(s => s.VehicleId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.GirisTarihi).First());

        // Açık BAF tahsisi — araç başına en yenisi + personel adı.
        var baflar = (await db.Baflar.AsNoTracking()
                .Where(b => vehicleIds.Contains(b.VehicleId) && b.Durum == BafDurum.Acik)
                .Select(b => new { b.VehicleId, b.No, b.PersonelId, b.CikisTarihi })
                .ToListAsync(ct))
            .GroupBy(b => b.VehicleId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(b => b.CikisTarihi).First());
        var persIds = baflar.Values.Select(b => b.PersonelId).Distinct().ToList();
        // PII notu: yalnız Ad/Soyad projekte ediliyor — TcKimlikEnc/MaasEnc hiç OKUNMUYOR.
        var persById = new Dictionary<Guid, string>();
        if (persIds.Count > 0)
        {
            persById = (await db.Personeller.AsNoTracking()
                    .Where(p => persIds.Contains(p.Id))
                    .Select(p => new { p.Id, p.Ad, p.Soyad })
                    .ToListAsync(ct))
                .ToDictionary(p => p.Id, p => $"{p.Ad} {p.Soyad}".Trim());
        }

        // Aktif uzun-dönem filo kiralama dosyası (araç başına en yenisi).
        var filoKira = (await db.FiloKiralamalar.AsNoTracking()
                .Where(k => vehicleIds.Contains(k.VehicleId) && k.Durum == FiloKiraDurum.Aktif)
                .Select(k => new { k.VehicleId, k.DosyaNo, k.BasTar })
                .ToListAsync(ct))
            .GroupBy(k => k.VehicleId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(k => k.BasTar).First().DosyaNo);

        var rows = vehicles.Select(v =>
        {
            rentalByVehicle.TryGetValue(v.Id, out var rental);
            rezByVehicle.TryGetValue(v.Id, out var rez);
            servisler.TryGetValue(v.Id, out var servis);
            baflar.TryGetValue(v.Id, out var baf);

            var musteri = rental is null ? default : custById.GetValueOrDefault(rental.MusteriId);
            var rezMusteri = rez is null ? default : custById.GetValueOrDefault(rez.MusteriId);

            return new FleetStatusRow
            {
                VehicleId = v.Id,
                Plaka = v.Plaka,
                Marka = v.Marka,
                Tip = v.Tip,
                Grup = v.Grup,
                Segment = v.Segment,
                Sipp = v.Sipp,
                Vites = v.Vites,
                Yakit = v.Yakit,
                Km = v.Km,
                Sube = v.Sube,
                Durum = v.Durum,
                FiloDurum = v.FiloDurum,
                PasifSebep = v.PasifSebep,
                Konum = v.Konum,
                TakipNo = v.TakipNo,
                HgsNo = string.IsNullOrWhiteSpace(v.HgsNo) ? v.OgsNo : v.HgsNo,
                KarLastigi = v.KarLastigi,
                WebRezKapat = v.WebRezKapat,
                OfisRezKapat = v.OfisRezKapat,
                AktifKiraId = rental?.Id,
                KiraSozlesmeNo = rental?.SozlesmeNo,
                MusteriAd = musteri.Ad,
                MusteriTel = musteri.Tel,
                KiraBitTar = rental?.BitTar,
                KiraBakiye = rental?.Bakiye,
                KiraKalanGun = rental is null ? null : FleetStatusRow.KalanGun(rental.BitTar, now),
                RezMusteriAd = rezMusteri.Ad,
                RezBasTar = rez?.BasTar,
                AcikServisNo = servis?.No,
                ServisAtolye = servis?.AtolyeAdi,
                AktifBafNo = baf?.No,
                BafPersonelAd = baf is null ? null : persById.GetValueOrDefault(baf.PersonelId),
                DosyaNo = filoKira.GetValueOrDefault(v.Id)
            };
        });

        // Kirada filtresi (rental varlığına bağlı → projeksiyon sonrası).
        if (filter.KiradaMi is { } kirada)
            rows = rows.Where(r => r.Kirada == kirada);

        return rows.ToList();
    }
}
