using System.Globalization;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Periods;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Hgs;

/// <summary>
/// HGS geçişlerini DOSYADAN içe aktarma + kiraya eşleştirme + müşteriye yansıtma (API'siz ilk aşama).
/// <list type="bullet">
/// <item><b>İçe aktarma</b> iki adımlıdır: <see cref="PreviewAsync"/> hiçbir şey yazmaz (kaç satır eşleşti / kira dışı
/// / mükerrer / hatalı); kullanıcı onaylayınca <see cref="ImportAsync"/> aynı boru hattını yazarak koşar. Mükerrer
/// koruması DB'dedir (doğal anahtar unique index'i) — önizleme yalnız bilgi verir.</item>
/// <item><b>Yetki:</b> içe aktarma ve yeniden eşleştirme OperationsWrite ∨ FinanceWrite + şube KISITSIZ kullanıcı (HGS
/// dökümü kiracı genelidir; şubeye bağlı kullanıcı başka şubelerin kiralarını önizlemede görmesin). Yansıtma (defter
/// yazar) FinanceWrite.</item>
/// <item><b>Yansıtma</b> mevcut HGS/ceza yansıtma desenini izler: Borç Cari / Alacak Gelir, TRY, <c>SourceType =
/// "Hgs"</c>; tutar = geçiş × hizmet oranı, SATIR başına 2 haneye yuvarlanır. <c>SourceId</c> GEÇİŞİN kimliğidir —
/// bir geçiş en fazla bir kez borçlandırılabilir (defterdeki kısmi unique index). Eski dönem-MD5'li
/// <c>HgsReflectionService</c> #391 adversarial L1 ile silindi (aynı geçişi ikinci yoldan borçlandırabilirdi).</item>
/// <item><b>Olası mükerrer</b> (#391 M1, R2 L1, #407 M1): aynı plaka + aynı tutar + 60 sn içindeki ikinci satır ya da
/// başka bir dökümde aynı istasyon + aynı kirayla 6 sa içindeki bire bir eşi Belirsiz kalır
/// (<see cref="HgsNearDuplicateIndex"/>); ayrı geçişse FinanceWrite <see cref="ConfirmAsync"/> ile onaylanır; içe aktarma, yeniden eşleştirme ve yansıtma (kilit altında) aynı kuralı uygular.</item>
/// </list>
/// </summary>
public sealed class HgsCrossingService(
    IHgsCrossingRepository repository, ICurrentUser currentUser, IPeriodLockGuard periodLock)
{
    /// <summary>Önizlemede dönen satır üst sınırı (sayılar dosyanın tamamı içindir).</summary>
    public const int MaxPreviewRows = 500;
    /// <summary>Tek yansıtma isteğindeki geçiş üst sınırı.</summary>
    public const int MaxReflectIds = 500;
    public const decimal MinServiceRate = 1m;
    public const decimal MaxServiceRate = 2m;

    private readonly IHgsCrossingRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IPeriodLockGuard _periodLock = periodLock;

    // ================================================================== okuma

    public Task<IReadOnlyList<HgsCrossing>> ListAsync(HgsCrossingFilter filter, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser, Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports);
        return _repository.ListAsync(filter, ct);
    }

    /// <summary>Bir kiraya bağlı geçişler — kira formu "Ceza/HGS" alt sekmesi (kapsam kapısı çağıranda, ceza deseni).</summary>
    public Task<IReadOnlyList<HgsCrossing>> ListByRentalAsync(Guid rentalId, CancellationToken ct = default)
        => _repository.ListAsync(new HgsCrossingFilter { RentalId = rentalId }, ct);

    public Task<IReadOnlyList<HgsCrossing>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
        => _repository.FindManyAsync(ids, ct);

    // ================================================================== içe aktarma

    public Task<HgsImportOutcome> PreviewAsync(IReadOnlyList<HgsRawRow> rows, CancellationToken ct = default)
        => RunAsync(rows, null, write: false, ct);

    public Task<HgsImportOutcome> ImportAsync(IReadOnlyList<HgsRawRow> rows, string? fileName, CancellationToken ct = default)
        => RunAsync(rows, fileName, write: true, ct);

    /// <summary>Yansıtılmamış geçişleri güncel kira verisiyle yeniden eşleştirir (kira sonradan girildi/düzeltildi).</summary>
    public Task<int> RematchAsync(CancellationToken ct = default)
    {
        RequireImport();
        return _repository.RematchAsync(ct);
    }

    /// <summary>
    /// Olası mükerrer satırı "ayrı geçiş" olarak onaylar (#407 M1-d): yalnız olası mükerrer notlu Belirsiz satır, kirası
    /// kilit altında hâlâ geçiş anını kapsıyorsa Eslesti olur ve iki mükerrer kuralından kalıcı olarak muaf tutulur
    /// (<c>OnayZamani</c>). Para yazmaz — borç ayrı yansıtma adımıdır. Değişiklik denetim kaydına düşer (IAuditable).
    /// </summary>
    public Task<HgsConfirmResult> ConfirmAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        var distinct = ids.Where(i => i != Guid.Empty).Distinct().ToList();
        if (distinct.Count == 0) throw new ValidationException("Onaylanacak geçiş seçilmedi.", "ids");
        if (distinct.Count > MaxReflectIds)
            throw new ValidationException($"Tek seferde en çok {MaxReflectIds} geçiş onaylanabilir.", "ids");
        return _repository.ConfirmAsync(distinct, BranchScope.EffectiveFilter(_currentUser), DateTimeOffset.UtcNow, ct);
    }

    private void RequireImport()
    {
        PermissionGuard.RequireAny(_currentUser, Permission.OperationsWrite, Permission.FinanceWrite);
        if (!BranchScope.EffectiveFilter(_currentUser).Unrestricted)
            throw new NoPermissionException("HGS dökümü kiracı genelidir; şubeye bağlı kullanıcı içe aktaramaz.");
    }

    private sealed record ParsedRow(
        int SatirNo, string Plate, string? Tag, string? VehicleClass, string? Entry, string? Exit,
        DateTimeOffset? EntryAt, DateTimeOffset At, decimal Amount, string StationKey, string Key);

    private async Task<HgsImportOutcome> RunAsync(
        IReadOnlyList<HgsRawRow> rows, string? fileName, bool write, CancellationToken ct)
    {
        RequireImport();
        var now = DateTimeOffset.UtcNow;
        var results = new List<HgsImportRow>(rows.Count);
        var parsed = new List<ParsedRow>(rows.Count);

        // (1) ayrıştır — hatalı satır diğerlerini durdurmaz
        foreach (var r in rows)
        {
            try { parsed.Add(Parse(r, now)); }
            catch (ValidationException ex)
            {
                results.Add(new HgsImportRow(r.SatirNo, r.Plaka?.Trim(), null, r.GirisIstasyonu?.Trim(), r.CikisIstasyonu?.Trim(),
                    null, HgsImportRowStatus.Hatali, null, null, false, ex.Message));
            }
        }

        // (1b) Saatsiz döküm ENGELLEYİCİ (#391 M2): saat sütunu yokken her satır 00:00 ise saat bilgisi kaybolmuştur;
        // gece yarısı varsaymak yanlış müşteriyi borçlandırır → satırların hepsi hatalı (yalnız uyarı yetmez).
        var warnings = new List<string>();
        if (parsed.Count >= 2 && rows.All(r => string.IsNullOrWhiteSpace(r.GecisSaati))
            && parsed.All(p => TimeZoneInfo.ConvertTime(p.At, PeriodLock.Zone).TimeOfDay == TimeSpan.Zero))
        {
            warnings.Add(MidnightWarning);
            foreach (var p in parsed) results.Add(Row(p, HgsImportRowStatus.Hatali, null, false, MidnightWarning));
            parsed.Clear();
        }

        // (2) dosya içi tam tekrar
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var fresh = new List<ParsedRow>(parsed.Count);
        foreach (var p in parsed)
        {
            if (seen.Add(p.Key)) fresh.Add(p);
            else results.Add(Row(p, HgsImportRowStatus.Mukerrer, null, false, "Dosyada aynı geçiş birden çok kez var."));
        }

        // (3) DB'de zaten olanlar + olası mükerrer + araç/kira çözümü. Satırlar öncelik sırasıyla (an, istasyon)
        // işlenir: kümenin ilki asıl, sonrakiler ikincil (Belirsiz) — yeniden eşleştirme aynı sırayı kullanır.
        var classified = new List<(ParsedRow Row, Guid Id, Guid? VehicleId, HgsMatch Match)>(fresh.Count);
        var batchId = Guid.NewGuid();
        if (fresh.Count > 0)
        {
            var from = fresh.Min(p => p.At);
            var to = fresh.Max(p => p.At);
            var existing = await _repository.ExistingAsync(fresh.Select(p => p.Plate).Distinct().ToList(),
                from - HgsResolution.LookAround, to + HgsResolution.LookAround, ct);
            var existingKeys = existing.Select(e => HgsResolution.NaturalKey(e.Plate, e.At, e.StationKey, e.Amount))
                .ToHashSet(StringComparer.Ordinal);
            var near = new HgsNearDuplicateIndex(existing);
            var vehicles = HgsResolution.VehicleIndex(await _repository.ListVehiclesAsync(ct));
            var vehicleIds = fresh.Select(p => vehicles.GetValueOrDefault(p.Plate)).OfType<Guid>().Distinct().ToList();
            var windows = (await _repository.ListRentalWindowsAsync(vehicleIds, from, to, ct)).ToLookup(w => w.VehicleId);
            // Önce dosyanın TÜM satırları dizine girer, sonra karar verilir (geniş kuralın bire bir eşleşmesi sıraya
            // bağlı olmasın).
            var pending = new List<(ParsedRow Row, HgsCrossingRef Me, Guid? VehicleId, HgsMatch Match)>(fresh.Count);
            foreach (var p in fresh.OrderBy(p => p.At).ThenBy(p => p.StationKey, StringComparer.Ordinal))
            {
                if (existingKeys.Contains(p.Key))
                {
                    results.Add(Row(p, HgsImportRowStatus.Mukerrer, null, false, "Bu geçiş daha önce içe aktarılmış."));
                    continue;
                }
                var (vehicleId, match) = HgsResolution.Resolve(p.Plate, p.At, vehicles, windows);
                var me = new HgsCrossingRef(Guid.NewGuid(), p.Plate, p.At, p.Amount, p.StationKey, now, match.Status,
                    batchId, match.Rental?.RentalId, HgsValueParser.NearKey(p.Entry), HgsValueParser.NearKey(p.Exit), false);
                near.Add(me);
                pending.Add((p, me, vehicleId, match));
            }
            foreach (var (p, me, vehicleId, match) in pending)
                classified.Add((p, me.Id, vehicleId, HgsResolution.WithNearDuplicate(match, near.IsSecondary(me))));
        }

        // (4) yaz (onaylı adım). Depo kilit altında tam mükerrer ve olası mükerrer kararını yeniden verir; varlığın
        // Durum/EslesmeNotu'su gerekirse yerinde güncellenir — sonuç satırları varlığın SON hâlinden kurulur.
        Guid? batch = null;
        var lateDuplicates = (IReadOnlySet<string>)new HashSet<string>(StringComparer.Ordinal);
        var entities = new Dictionary<Guid, HgsCrossing>();
        if (write && classified.Count > 0)
        {
            batch = batchId;
            var name = HgsValueParser.Text(fileName, 256, "Dosya adı");
            foreach (var x in classified)
            {
                entities[x.Id] = new HgsCrossing
                {
                    Id = x.Id, Plaka = x.Row.Plate, EtiketNo = x.Row.Tag, AracSinifi = x.Row.VehicleClass,
                    GirisIstasyonu = x.Row.Entry, CikisIstasyonu = x.Row.Exit, IstasyonAnahtari = x.Row.StationKey,
                    GirisZamani = x.Row.EntryAt, GecisZamani = x.Row.At, Tutar = x.Row.Amount,
                    Durum = x.Match.Status, EslesmeNotu = x.Match.Note, VehicleId = x.VehicleId,
                    RentalId = x.Match.Rental?.RentalId,
                    IceAktarimId = batch.Value, Kaynak = "Dosya", DosyaAdi = name,
                    CreatedAtUtc = now, UpdatedAtUtc = now,
                };
            }
            lateDuplicates = await _repository.InsertAsync(entities.Values.ToList(), ct);
        }

        foreach (var (p, id, vehicleId, match) in classified)
        {
            if (lateDuplicates.Contains(p.Key))
            {
                results.Add(Row(p, HgsImportRowStatus.Mukerrer, null, false, "Bu geçiş daha önce içe aktarılmış."));
                continue;
            }
            var (state, note) = entities.TryGetValue(id, out var e) ? (e.Durum, e.EslesmeNotu) : (match.Status, match.Note);
            var status = state switch
            {
                HgsCrossingStatus.Eslesti => HgsImportRowStatus.Eslesti,
                HgsCrossingStatus.Belirsiz => HgsImportRowStatus.Belirsiz,
                _ => HgsImportRowStatus.KiraDisi,
            };
            results.Add(Row(p, status, match.Rental, vehicleId is null, note));
        }

        var counted = results.Where(r => r.Durum != HgsImportRowStatus.Hatali && r.Durum != HgsImportRowStatus.Mukerrer).ToList();
        var shown = results
            .OrderBy(r => r.Durum == HgsImportRowStatus.Hatali ? 0 : 1)
            .ThenBy(r => r.SatirNo)
            .Take(MaxPreviewRows)
            .ToList();
        var added = write ? classified.Count - lateDuplicates.Count : 0;
        return new HgsImportOutcome(
            Toplam: rows.Count,
            Eslesen: counted.Count(r => r.Durum == HgsImportRowStatus.Eslesti),
            KiraDisi: counted.Count(r => r.Durum == HgsImportRowStatus.KiraDisi),
            Belirsiz: counted.Count(r => r.Durum == HgsImportRowStatus.Belirsiz),
            OlasiMukerrer: counted.Count(r => r.Durum == HgsImportRowStatus.Belirsiz && r.Hata == HgsResolution.NearDuplicateNote),
            AracBulunamadi: counted.Count(r => r.AracBulunamadi),
            Mukerrer: results.Count(r => r.Durum == HgsImportRowStatus.Mukerrer),
            Hatali: results.Count(r => r.Durum == HgsImportRowStatus.Hatali),
            Eklenen: added,
            EslesenTutar: counted.Where(r => r.Durum == HgsImportRowStatus.Eslesti).Sum(r => r.Tutar ?? 0m),
            KiraDisiTutar: counted.Where(r => r.Durum == HgsImportRowStatus.KiraDisi).Sum(r => r.Tutar ?? 0m),
            Uyarilar: warnings,
            Satirlar: shown,
            IceAktarimId: added > 0 ? batch : null);
    }

    public const string MidnightWarning =
        "Tüm geçiş zamanları 00:00 — dosyada saat bilgisi yok ya da ayrı bir sütunda; saat sütununu eşleyin. Satırlar içe aktarılmadı.";

    private static HgsImportRow Row(ParsedRow p, HgsImportRowStatus status, HgsRentalWindow? rental, bool noVehicle, string? note)
        => new(p.SatirNo, p.Plate, p.At, p.Entry, p.Exit, p.Amount, status, rental?.RentalId, rental?.SozlesmeNo, noVehicle, note);

    private static ParsedRow Parse(HgsRawRow r, DateTimeOffset now)
    {
        var plate = HgsValueParser.ParsePlate(r.Plaka);
        var at = HgsValueParser.ParseInstant(r.GecisZamani, r.GecisSaati, now);
        DateTimeOffset? entryAt = string.IsNullOrWhiteSpace(r.GirisZamani)
            ? null
            : HgsValueParser.ParseInstant(r.GirisZamani, null, now, "Giriş zamanı");
        if (entryAt > at) throw new ValidationException("Giriş zamanı geçiş (çıkış) zamanından sonra olamaz.");
        var amount = HgsValueParser.ParseAmount(r.Tutar);
        var entry = HgsValueParser.Text(r.GirisIstasyonu, 128, "Giriş istasyonu");
        var exit = HgsValueParser.Text(r.CikisIstasyonu, 128, "Çıkış istasyonu");
        var tag = HgsValueParser.Text(r.EtiketNo, 64, "Etiket no");
        var vehicleClass = HgsValueParser.Text(r.AracSinifi, 32, "Araç sınıfı");
        var station = HgsValueParser.StationKey(entry, exit);
        return new ParsedRow(r.SatirNo, plate, tag, vehicleClass, entry, exit, entryAt, at, amount, station,
            HgsResolution.NaturalKey(plate, at, station, amount));
    }

    // ================================================================== yansıtma (PARA)

    /// <summary>
    /// Seçili geçişleri kiranın müşterisine yansıtır. Yalnız kilit altında HÂLÂ <see cref="HgsCrossingStatus.Eslesti"/>
    /// olan, kirası (FOR SHARE ile kilitli GÜNCEL hâli) geçiş anını HÂLÂ kapsayan ve olası mükerrer kümesinin ikincil
    /// üyesi olmayan satırlar borçlandırılır; zaten yansıtılmış satır sessizce atlanır (tekrar gönderim yeni para
    /// yazmaz), diğerleri "uygunsuz" sayılır. Şube kapsamı kilit altında yeniden denetlenir (#391 M3).
    /// </summary>
    public async Task<HgsReflectResult> ReflectAsync(
        IReadOnlyCollection<Guid> ids, decimal serviceRate, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        var distinct = ids.Where(i => i != Guid.Empty).Distinct().ToList();
        if (distinct.Count == 0) throw new ValidationException("Yansıtılacak geçiş seçilmedi.", "ids");
        if (distinct.Count > MaxReflectIds)
            throw new ValidationException($"Tek seferde en çok {MaxReflectIds} geçiş yansıtılabilir.", "ids");
        if (serviceRate < MinServiceRate || serviceRate > MaxServiceRate)
            throw new ValidationException("Hizmet oranı 1,00 ile 2,00 arasında olmalı (1,03 = %3 hizmet bedeli).", "hizmetOrani");
        if (decimal.Round(serviceRate, 4) != serviceRate)
            throw new ValidationException("Hizmet oranı en çok 4 ondalık hane içerebilir.", "hizmetOrani");

        var now = DateTimeOffset.UtcNow;
        await _periodLock.EnsureOpenAsync(now, ct); // dönem kilidi: yansıtma bugün tarihli postlanır

        return await _repository.ReflectAsync(distinct, BranchScope.EffectiveFilter(_currentUser), (c, rental) =>
        {
            // SATIR bazında yuvarlama (para kalemi kuralı): toplamı çarpıp tek yuvarlamak satırların toplamıyla
            // kuruş farkı üretirdi — cari ekstresindeki satırlar borçla birebir toplanmalı.
            var amount = Math.Round(c.Tutar * serviceRate, 2, MidpointRounding.AwayFromZero);
            var money = new Money(amount, "TRY", 1m);
            var description = Describe(c);
            IReadOnlyList<AccountLedgerEntry> entries =
            [
                new AccountLedgerEntry
                {
                    EntryDateUtc = now, AccountType = LedgerAccountType.Cari, AccountRef = rental.CustomerId,
                    Direction = LedgerDirection.Debit, Amount = money,
                    SourceType = "Hgs", SourceId = c.Id, Description = description
                },
                new AccountLedgerEntry
                {
                    EntryDateUtc = now, AccountType = LedgerAccountType.Gelir, AccountRef = null,
                    Direction = LedgerDirection.Credit, Amount = money,
                    SourceType = "Hgs", SourceId = c.Id, Description = description
                }
            ];
            return new HgsReflectionPlan(entries, amount, serviceRate);
        }, ct);
    }

    private static string Describe(HgsCrossing c)
    {
        var local = TimeZoneInfo.ConvertTime(c.GecisZamani, PeriodLock.Zone);
        var station = (c.GirisIstasyonu, c.CikisIstasyonu) switch
        {
            (null, null) => "",
            ({ } a, null) => " " + a,
            (null, { } b) => " " + b,
            ({ } a, { } b) => $" {a} → {b}",
        };
        var text = string.Create(CultureInfo.InvariantCulture, $"HGS {c.Plaka} {local:dd.MM.yyyy HH:mm}{station}");
        return text.Length <= 512 ? text : text[..512];
    }
}
