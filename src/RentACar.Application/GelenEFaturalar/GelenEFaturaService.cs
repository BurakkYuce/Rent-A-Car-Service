using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Expenses;
using RentACar.Application.Integrations;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.GelenEFaturalar;

/// <summary>
/// Gelen e-Fatura triage iş mantığı: liste/filtre + elle giriş + GİB-sync (stub) + durum akışı
/// (Beklemede→Onaylandı/Reddedildi; Onaylandı→İşlendi) + FAZ-55 KDV oran kırılımı/bağlama
/// (<see cref="BaglaAsync"/>) + giderleştirme (<see cref="GiderlestirAsync"/>).
/// Yazma → <see cref="Permission.FinanceWrite"/>. ETTN tenant içinde benzersiz (elle + sync idempotent).
///
/// <para><b>DEFTER SÖZLEŞMESİ:</b> bu servis KENDİ defter kaydı YAZMAZ ve yeni bir defter şekli
/// icat ETMEZ. Tek para yolu <see cref="GiderlestirAsync"/>'tir ve o da işi olduğu gibi
/// <see cref="ExpenseService.BatchCreateAsync"/>'e devreder → yazılan küme mevcut, denetlenmiş
/// gider kümesidir: <c>Borç Gider(net) + Borç KDV(indirilecek) / Alacak Kasa·Banka·Cari(gross)</c>.
/// Dolayısıyla dönem kilidi, kur çözümü (KurCozucu), şube-FK, denge kontrolü ve idempotency
/// çiti TEK yerde kalır — kopyalanmaz.</para>
/// </summary>
public sealed class GelenEFaturaService(
    IGelenEFaturaRepository repository, IEInvoiceService einvoice, ICurrentUser currentUser,
    ExpenseService expenses)
{
    private readonly IGelenEFaturaRepository _repository = repository;
    private readonly IEInvoiceService _einvoice = einvoice;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly ExpenseService _expenses = expenses;

    public Task<IReadOnlyList<GelenEFatura>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(null, ct);

    /// <summary>FAZ-55: filtreli liste (firma / ETTN aralığı / plaka / durum / tarih).</summary>
    public Task<IReadOnlyList<GelenEFatura>> ListAsync(GelenEFaturaFilter? filter, CancellationToken ct = default)
        => _repository.ListAsync(filter, ct);

    public Task<GelenEFatura?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateManualAsync(GelenEFaturaInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        var ettn = (input.Ettn ?? string.Empty).Trim();
        var vkn = (input.GonderenVkn ?? string.Empty).Trim();
        var unvan = (input.GonderenUnvan ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(ettn)) throw new ValidationException("ETTN zorunludur.");
        if (string.IsNullOrWhiteSpace(vkn)) throw new ValidationException("Gönderen VKN zorunludur.");
        if (string.IsNullOrWhiteSpace(unvan)) throw new ValidationException("Gönderen ünvanı zorunludur.");
        // FAZ-55: belge kendi içinde tutarlı olmalı (net + KDV == genel toplam). Aksi halde hiçbir
        // KDV kırılımı toplamı tutturamaz ve giderleştirmede defter belgeden kopardı.
        GelenEFaturaKdvKirilim.ToplamlariDogrula(input.NetTutar, input.KdvTutar, input.GenelToplam);
        if (await _repository.EttnExistsAsync(ettn, ct))
            throw new ValidationException($"'{ettn}' ETTN'li gelen fatura zaten kayıtlı.");

        var row = new GelenEFatura
        {
            Ettn = ettn,
            GonderenVkn = vkn,
            GonderenUnvan = unvan,
            Tarih = input.Tarih ?? DateTimeOffset.UtcNow,
            NetTutar = input.NetTutar,
            KdvTutar = input.KdvTutar,
            GenelToplam = input.GenelToplam,
            Currency = string.IsNullOrWhiteSpace(input.Currency) ? "TRY" : input.Currency!.Trim().ToUpperInvariant(),
            Aciklama = string.IsNullOrWhiteSpace(input.Aciklama) ? null : input.Aciklama!.Trim(),
            Durum = GelenEFaturaDurum.Beklemede
        };
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    /// <summary>GİB gelen kutusunu çekip ETTN'e göre upsert eder (mevcut ETTN atlanır → idempotent).
    /// Stub boş döndüğünden kimlik yapılandırılana dek 0 ekler. Eklenen adet döner.</summary>
    public async Task<int> SyncFromGibAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        var items = await _einvoice.FetchInboxAsync(from, to, ct);
        int added = 0;
        foreach (var it in items)
        {
            if (string.IsNullOrWhiteSpace(it.Ettn) || await _repository.EttnExistsAsync(it.Ettn.Trim(), ct)) continue;
            // FAZ-55: entegratörden gelen belge de kendi içinde tutarlı olmalı. SESSİZ ATLAMA YOK —
            // tutarsız belgeyi görmezden gelmek "eksik gider" üretir; ETTN'li gürültülü red daha dürüst.
            try { GelenEFaturaKdvKirilim.ToplamlariDogrula(it.NetTutar, it.KdvTutar, it.GenelToplam); }
            catch (ValidationException ex) { throw new ValidationException($"ETTN {it.Ettn.Trim()}: {ex.Message}"); }
            await _repository.CreateAsync(new GelenEFatura
            {
                Ettn = it.Ettn.Trim(),
                GonderenVkn = it.GonderenVkn,
                GonderenUnvan = it.GonderenUnvan,
                Tarih = it.Tarih,
                NetTutar = it.NetTutar,
                KdvTutar = it.KdvTutar,
                GenelToplam = it.GenelToplam,
                Currency = string.IsNullOrWhiteSpace(it.Currency) ? "TRY" : it.Currency,
                Durum = GelenEFaturaDurum.Beklemede
            }, ct);
            added++;
        }
        return added;
    }

    public Task<bool> OnaylaAsync(Guid id, CancellationToken ct = default)
        => TransitionAsync(id, GelenEFaturaDurum.Beklemede, GelenEFaturaDurum.Onaylandi,
            "Yalnız beklemedeki fatura onaylanabilir.", null, ct);

    public Task<bool> ReddetAsync(Guid id, string? neden, CancellationToken ct = default)
        => TransitionAsync(id, GelenEFaturaDurum.Beklemede, GelenEFaturaDurum.Reddedildi,
            "Yalnız beklemedeki fatura reddedilebilir.", string.IsNullOrWhiteSpace(neden) ? null : neden.Trim(), ct);

    public Task<bool> IsleAsync(Guid id, CancellationToken ct = default)
        => TransitionAsync(id, GelenEFaturaDurum.Onaylandi, GelenEFaturaDurum.Islendi,
            "Yalnız onaylanmış fatura işlenebilir.", null, ct);

    private async Task<bool> TransitionAsync(
        Guid id, GelenEFaturaDurum from, GelenEFaturaDurum to, string hata, string? redNedeni, CancellationToken ct)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        // #286 Low-9: satır kilidi altında — giderleştirmenin "talep" adımıyla serileşir (ikisi birden geçemez).
        return await _repository.UpdateLockedAsync(id, null, row =>
        {
            if (row.Durum != from) throw new ValidationException(hata);
            row.Durum = to;
            if (redNedeni is not null) row.RedNedeni = redNedeni;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    // ================= FAZ-55 (a): KDV oran kırılımı + araç/kategori/cari bağlama =================

    /// <summary>
    /// KDV oran kırılımını (%20/%10/%1/%0 matrah+KDV) ve araç / gider kategorisi / tedarikçi cari
    /// bağını kaydeder. Hepsi BİLGİDİR — deftere YAZMAZ; yalnız <see cref="GiderlestirAsync"/>'e girdi olur.
    ///
    /// <para><b>Kilit:</b> fatura bir kez giderleştirildiyse bu alanlar DEĞİŞTİRİLEMEZ. Aksi halde
    /// belge (kırılım) ile defter (yazılmış gider satırları) sessizce diverge ederdi — mali kayıtta
    /// "sonradan düzeltme" yasağının bu ekrandaki karşılığı.</para>
    /// </summary>
    public async Task<bool> BaglaAsync(
        GelenEFaturaBaglamaInput input, CancellationToken ct = default, string? expectedVersion = null)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        // #286 M3: tam değiştirme — satır kilidi + (verilirse) sürüm karşılaştırması; bayat form 409 cakisma.
        return await _repository.UpdateLockedAsync(input.Id, expectedVersion, row =>
        {
            // Giderleştirme talep edildiyse (defter yazımı sürüyor ya da bitti) kırılım artık kilitli.
            if (row.GiderlestirilmeUtc is not null || row.GiderIslemAnahtari is not null)
                throw new ValidationException(
                    "Bu fatura giderleştirilmiş; KDV kırılımı ve bağlama alanları artık değiştirilemez " +
                    "(düzeltme, gider tarafında ters kayıtla yapılır).");
            if (row.Durum == GelenEFaturaDurum.Reddedildi)
                throw new ValidationException("Reddedilmiş faturaya kırılım/bağlama girilemez.");

            row.Kdv20Matrah = input.Kdv20Matrah; row.Kdv20 = input.Kdv20;
            row.Kdv10Matrah = input.Kdv10Matrah; row.Kdv10 = input.Kdv10;
            row.Kdv1Matrah = input.Kdv1Matrah; row.Kdv1 = input.Kdv1;
            row.Kdv0Matrah = input.Kdv0Matrah;
            row.VehicleId = input.VehicleId;
            row.ExpenseCategoryId = input.ExpenseCategoryId;
            row.CariId = input.CariId;
            row.GiderTipi = input.GiderTipi;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;

            // Doğrulama SET'ten SONRA, kaydetmeden ÖNCE: tutarsız kırılım hiç yazılmaz (Update
            // izleyici bağlamında SaveChanges'ten önce fırlar → transaction yok, değişiklik atılır).
            GelenEFaturaKdvKirilim.ToplamlariDogrula(row.NetTutar, row.KdvTutar, row.GenelToplam);
            GelenEFaturaKdvKirilim.Dogrula(row);
        }, ct);
    }

    // ================= FAZ-55 (b): giderleştirme (TEK para yolu) =================

    /// <summary>
    /// Gelen faturayı gidere dönüştürür: her KDV oran kademesi AYRI bir <c>Expense</c> satırı olur ve
    /// tümü TEK transaction'da (<see cref="ExpenseService.BatchCreateAsync"/>) yazılır. Dönen değer,
    /// oluşan gider satırı sayısıdır.
    ///
    /// <para><b>Defter kümesi (satır başına, DEĞİŞMEDİ):</b>
    /// <c>Borç Gider(matrah) + Borç KDV(indirilecek) / Alacak Cari|Kasa|Banka(matrah+KDV)</c>.
    /// Σ Borç(baz) == Σ Alacak(baz) her satırda ayrı ayrı sağlanır.</para>
    ///
    /// <para><b>Kuruş:</b> yuvarlama SATIR BAZINDA yapılır; <see cref="GelenEFaturaKdvKirilim.Coz"/>
    /// Σ matrah == NetTutar ve Σ KDV == KdvTutar eşitliğini KURUŞ-BİREBİR zorlar → defter toplamı
    /// belgenin toplamına eşittir, bir kuruş bile uydurulamaz.</para>
    ///
    /// <para><b>İdempotency:</b> batch anahtarı = faturanın kendi <c>Id</c>'si (deterministik).
    /// Her gider satırı <c>CashService.RowKey(Id, i)</c> alır; <c>Expenses</c> üzerindeki kısmi
    /// unique index <c>(TenantId, IslemAnahtari)</c> ikinci yazımı DB seviyesinde reddeder. Yani
    /// eşzamanlı iki çağrı (TOCTOU) durumunda bile ikinci küme YAZILAMAZ — bellek-içi bayrak
    /// kontrolü yalnız kullanıcıya anlaşılır mesaj vermek içindir, güvenlik çiti DB'dedir.</para>
    ///
    /// <para><b>Bilinen dar yarış (Low, bilinçli):</b> <see cref="BaglaAsync"/> tam olarak
    /// "kırılım okundu"–"damga atıldı" aralığında çalışırsa, belgenin oran DAĞILIMI defterdekinden
    /// farklı görünebilir. Defter TOPLAMLARI etkilenmez: Bagla yalnız kırılım/bağlama alanlarını
    /// yazar ve her kırılım zaten değişmeyen Net/KDV toplamlarına eşit olmak ZORUNDADIR; ayrıca
    /// oluşan her gider satırı kendi oranını (<c>KdvOrani</c>) ve ETTN'i taşır. Kilit
    /// (<c>GiderlestirilmeUtc</c>) bu aralıktan sonraki tüm değişiklikleri kapatır.</para>
    /// </summary>
    public async Task<int> GiderlestirAsync(GelenEFaturaGiderInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);

        var row = await _repository.FindAsync(input.Id, ct)
            ?? throw new ValidationException("Gelen fatura bulunamadı.");

        // F1.4: ikinci giderleştirme MÜKERRER gönderimdir — yarışta DB kısıtı (Expenses IslemAnahtari,
        // RowKey(Id, i)) zaten MukerrerIslemException (409) veriyor; sıralı ikinci istek de AYNI tipi alır.
        if (row.GiderlestirilmeUtc is not null)
            throw new MukerrerIslemException($"'{row.Ettn}' ETTN'li fatura zaten giderleştirilmiş.");
        // Triage kapısı: yalnız ONAYLANMIŞ fatura deftere girer. "İşlendi" = dışarıda ele alınmış
        // (elle muhasebeleştirilmiş) demektir → tekrar giderleştirmek çift kayıt üretirdi.
        var resumable = row.GiderIslemAnahtari == row.Id; // talep edilmiş ama damgalanmamış (yarıda kalmış) deneme
        if (row.Durum != GelenEFaturaDurum.Onaylandi && !resumable)
            throw new ValidationException("Yalnız onaylanmış gelen fatura giderleştirilebilir.");
        _ = BuildExpenseLines(row, input); // hızlı doğrulama (kırılım, cari, araç) — talepten ÖNCE

        // #286 Low-9: TALEP adımı satır kilidi altında (Onaylandi → Islendi + GiderIslemAnahtari). "İşle" geçişi de aynı
        // kilitle Onaylandi ister → ikisi birden geçemez; bağlama da talepten sonra reddedilir (kırılım sabitlenir).
        GelenEFatura? claimed = null;
        var claimedNow = false;
        await _repository.UpdateLockedAsync(row.Id, null, r =>
        {
            if (r.GiderlestirilmeUtc is not null)
                throw new MukerrerIslemException($"'{r.Ettn}' ETTN'li fatura zaten giderleştirilmiş.");
            if (r.GiderIslemAnahtari != r.Id)
            {
                if (r.Durum != GelenEFaturaDurum.Onaylandi)
                    throw new ValidationException("Yalnız onaylanmış gelen fatura giderleştirilebilir.");
                r.Durum = GelenEFaturaDurum.Islendi;
                r.GiderIslemAnahtari = r.Id;
                r.UpdatedAtUtc = DateTimeOffset.UtcNow;
                claimedNow = true;
            }
            claimed = r;
        }, ct);

        var kalemler = BuildExpenseLines(claimed!, input);
        try
        {
            // TEK transaction + deterministik idempotency anahtarı (batch = faturanın Id'si).
            await _expenses.BatchCreateAsync(kalemler, batchAnahtari: row.Id, ct);
        }
        catch (MukerrerIslemException)
        {
            // Defter zaten yazılmış (eşzamanlı istek ya da damgası eksik kalmış önceki deneme): damga tamamlanır, 409.
            await StampAsync(row.Id, ct);
            throw;
        }
        catch (ValidationException) when (claimedNow)
        {
            // Hiçbir şey yazılmadı (toplu gider atomik): talep geri alınır, fatura yeniden giderleştirilebilir.
            await _repository.UpdateLockedAsync(row.Id, null, r =>
            {
                if (r.GiderlestirilmeUtc is not null || r.GiderIslemAnahtari != r.Id) return;
                r.Durum = GelenEFaturaDurum.Onaylandi;
                r.GiderIslemAnahtari = null;
                r.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }, ct);
            throw;
        }

        // Defter yazıldıktan SONRA belge damgalanır. Damga eksik kalırsa talep satırda durur; yeniden deneme
        // DB unique index'ine çarpar ("zaten kaydedilmiş") ve damgayı tamamlar → çift defter YİNE imkânsız.
        await StampAsync(row.Id, ct);
        return kalemler.Count;
    }

    private Task<bool> StampAsync(Guid id, CancellationToken ct)
        => _repository.UpdateLockedAsync(id, null, r =>
        {
            if (r.GiderlestirilmeUtc is not null) return;
            r.GiderlestirilmeUtc = DateTimeOffset.UtcNow;
            r.GiderIslemAnahtari = r.Id;
            r.Durum = GelenEFaturaDurum.Islendi;
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);

    /// <summary>Faturanın KDV kademelerinden gider kalemleri (kırılım + cari + araç doğrulamasıyla).</summary>
    private static List<ExpenseInput> BuildExpenseLines(GelenEFatura row, GelenEFaturaGiderInput input)
    {
        var satirlar = GelenEFaturaKdvKirilim.Coz(row); // kuruş-birebir doğrulama + oran çözümü

        var cariId = input.CariId ?? row.CariId;
        if (input.OdemeYontemi == OdemeYontemi.AcikHesap && (cariId is null || cariId == Guid.Empty))
            throw new ValidationException("Açık hesap giderleştirmesi için tedarikçi cari seçilmelidir.");

        var tip = row.GiderTipi ?? (row.VehicleId is { } v && v != Guid.Empty ? ExpenseType.Arac : ExpenseType.Genel);
        if (tip == ExpenseType.Arac && (row.VehicleId is null || row.VehicleId == Guid.Empty))
            throw new ValidationException("Araç gideri için faturaya önce araç bağlanmalıdır.");

        return satirlar.Select(s => new ExpenseInput
        {
            Tip = tip,
            Tarih = row.Tarih,
            VehicleId = row.VehicleId,
            CariId = input.OdemeYontemi == OdemeYontemi.AcikHesap ? cariId : null,
            Sube = input.Sube,
            EvrakNo = Kirp(row.Ettn, 64),
            NetTutar = s.Matrah,
            KdvOrani = s.Oran,
            Doviz = row.Currency,
            Kur = null, // açık kur YOK → KurCozucu belge tarihinden çözer (tüm kalemler aynı gün → aynı kur)
            OdemeYontemi = input.OdemeYontemi, // karşı hesabı TEK BAŞINA belirler (Nakit→Kasa/Banka→Banka/Açık→Cari)
            // Oran etiketi InvariantCulture: belge izi makine-okunur ve kültürden bağımsız kalsın.
            Aciklama = Kirp(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Gelen e-Fatura {0} — {1} (%{2:0.##})", row.Ettn, row.GonderenUnvan, s.Oran * 100m), 512)
        }).ToList();
    }

    private static string Kirp(string s, int max) => s.Length <= max ? s : s[..max];
}
