using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Finance;

/// <summary>Nakit tahsilat/ödeme + virman + ters kayıt form post uçları.</summary>
public static class FinanceEndpoints
{
    /// <summary>Form "Kasa"/"Banka" metnini hesap tipine çevirir (varsayılan Kasa).</summary>
    private static LedgerAccountType ParseAccount(string? s)
        => string.Equals(s, "Banka", StringComparison.OrdinalIgnoreCase)
            ? LedgerAccountType.Banka : LedgerAccountType.Kasa;

    /// <summary>Açık-yönlendirme savunması (adversarial): donus formdan gelir; yalnız site-içi GÖRELİ yol kabul
    /// edilir, harici/protokol-göreli (//evil, /\evil) → güvenli fallback (aksi halde phishing yönlendirmesi).</summary>
    internal static string SafeReturn(string? returnInfo, string fallback) // Depozito uçlarıyla paylaşılır
        => !string.IsNullOrEmpty(returnInfo) && returnInfo[0] == '/'
           && !(returnInfo.Length > 1 && (returnInfo[1] == '/' || returnInfo[1] == '\\'))
            ? returnInfo : fallback;

    /// <summary>Hata mesajını dönüş URL'ine doğru ayırıcıyla ekler: donus zaten querystring içeriyorsa
    /// (ör. pano "/?df=gec") '?hata=' İKİNCİ '?' üretip mesajı önceki parametreye yutturuyordu → '&'.</summary>
    internal static string ErrorUrl(string url, string message) => Result.Url(url, "hata", message, null);

    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/finans").RequirePermission(Permission.FinanceWrite).AntiforgeryByEnv();

        // FAZ 4.3: B2B dış hizmet alımı — TAM DEFTERLİ (gider araçta + komisyon geliri); ters kayıtla iptal.
        grp.MapPost("/dis-hizmet", async (RentACar.Application.DisHizmetler.OutsourcedServiceService svc, HttpRequest req,
            [FromForm] Guid rentalId, [FromForm] Guid cariId, [FromForm] string alinanHizmet,
            [FromForm] string? hizmetBedeli, [FromForm] string? komisyonOran, [FromForm] string? doviz,
            [FromForm] string? kur, [FromForm] string? aciklama, [FromForm] Guid islemAnahtari,
            [FromForm] string? donus) =>
        {
            var back = SafeReturn(donus, "/kiralar/" + rentalId);
            try
            {
                await svc.CreateAsync(new RentACar.Application.DisHizmetler.DisHizmetInput
                {
                    RentalId = rentalId, FaturaKesilecekCariId = cariId,
                    AlinanHizmet = alinanHizmet,
                    HizmetBedeli = FormParse.Dec(hizmetBedeli) ?? 0m,
                    TedarikciKomisyonOran = FormParse.Dec(komisyonOran) ?? 0m,
                    HizmetAlinanFirma = FormParse.Str(req.Form, "hizmetAlinanFirma"),
                    KomisyonFaturaNo = FormParse.Str(req.Form, "komisyonFaturaNo"),
                    Doviz = doviz, Kur = FormParse.Dec(kur), Aciklama = aciklama,
                    IslemAnahtari = islemAnahtari
                });
                return Result.Ok(back, "Dış hizmet alımı kaydedildi.");
            }
            catch (ValidationException ex)
            { return Results.Redirect($"{back}?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/dis-hizmet-iptal", async (RentACar.Application.DisHizmetler.OutsourcedServiceService svc,
            [FromForm] Guid id, [FromForm] string? donus) =>
        {
            var back = SafeReturn(donus, "/kiralar");
            try { await svc.CancelAsync(id); return Result.Ok(back, "Dış hizmet alımı iptal edildi (ters kayıt)."); }
            catch (ValidationException ex)
            { return Results.Redirect($"{back}?hata={Uri.EscapeDataString(ex.Message)}"); }
        }).RequirePermission(Permission.FinanceReverse);

        // FAZ 4.2-B3: dönem faturası kes (+opsiyonel tahsilat kaydı). Çift-submit güvenli: fatura
        // idempotent (Kesildi→mevcut), tahsilat deterministik RowKey(rentalId, donemSira) anahtarlı.
        grp.MapPost("/donem-fatura", async (RentACar.Application.FaturaDonemleri.PeriodCollectionService svc,
            [FromForm] Guid rentalId, [FromForm] int donemSira, [FromForm] string? tahsilat,
            [FromForm] string? hesap, [FromForm] string? donus) =>
        {
            var back = SafeReturn(donus, "/kiralar/" + rentalId);
            try
            {
                await svc.IssueAndCollectAsync(rentalId, donemSira,
                    tahsilat is "true" or "on",
                    string.Equals(hesap, "Banka", StringComparison.OrdinalIgnoreCase)
                        ? LedgerAccountType.Banka : LedgerAccountType.Kasa);
                return Result.Ok(back, "Dönem faturası kesildi.");
            }
            catch (ValidationException ex)
            { return Results.Redirect($"{back}?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/tahsilat", async (CashService svc,
            [FromForm] Guid cariId, [FromForm] string? rentalId, [FromForm] decimal tutar,
            [FromForm] string? doviz, [FromForm] string? kur, [FromForm] string? aciklama,
            [FromForm] string? hesap, [FromForm] string? donus, [FromForm] string? islemAnahtari,
            [FromForm] string? hesapId, [FromForm] string? kanal, // FAZ-50 hesapId + FAZ-84 kanal
            [FromForm] string? tarih) =>                          // FAZ-67: işlem tarihi formdan
        {
            try
            {
                await svc.CollectAsync(new CashInput
                {
                    CariId = cariId, RentalId = FormParse.Id(rentalId), Tutar = tutar,
                    Doviz = string.IsNullOrWhiteSpace(doviz) ? "TRY" : doviz, Kur = FormParse.Dec(kur), // boş → otomatik (1.1b)
                    Aciklama = aciklama, Hesap = ParseAccount(hesap), IslemAnahtari = FormParse.Id(islemAnahtari), // M5
                    HesapId = FormParse.Id(hesapId),  // FAZ-50: hangi spesifik kasa/banka
                    Kanal = kanal, // FAZ-84: boş → CashService "Masaüstü" varsayılanı
                    Tarih = FormParse.Date(tarih)  // FAZ-67: boş → sunucu "şimdi" (eski davranış)
                });
                return Result.Ok(SafeReturn(donus, $"/cariler/{cariId}/ekstre"), "Tahsilat kaydedildi.");
            }
            catch (ValidationException ex)
            {
                RentACar.Application.Observability.RacarMetrics.CollectionFail(); // metrik: tahsilat reddi (döviz/kilit/idempotent…)
                var url = SafeReturn(donus, $"/cariler/{cariId}/ekstre");
                return Results.Redirect(ErrorUrl(url, ex.Message)); // donus querystring'liyse '&' (çift-? düzeltmesi)
            }
        });

        grp.MapPost("/odeme", async (CashService svc,
            [FromForm] Guid cariId, [FromForm] string? rentalId, [FromForm] decimal tutar,
            [FromForm] string? doviz, [FromForm] string? kur, [FromForm] string? aciklama,
            [FromForm] string? hesap, [FromForm] string? donus, [FromForm] string? islemAnahtari,
            [FromForm] string? hesapId, [FromForm] string? kanal, // FAZ-50 hesapId + FAZ-84 kanal
            [FromForm] string? tarih) =>                          // FAZ-67: işlem tarihi formdan
        {
            try
            {
                await svc.PayAsync(new CashInput
                {
                    CariId = cariId, RentalId = FormParse.Id(rentalId), Tutar = tutar,
                    Doviz = string.IsNullOrWhiteSpace(doviz) ? "TRY" : doviz, Kur = FormParse.Dec(kur), // boş → otomatik (1.1b)
                    Aciklama = aciklama, Hesap = ParseAccount(hesap), IslemAnahtari = FormParse.Id(islemAnahtari), // M5
                    HesapId = FormParse.Id(hesapId),  // FAZ-50: hangi spesifik kasa/banka
                    Kanal = kanal, // FAZ-84
                    Tarih = FormParse.Date(tarih)  // FAZ-67
                });
                return Result.Ok(SafeReturn(donus, $"/cariler/{cariId}/ekstre"), "Ödeme kaydedildi.");
            }
            catch (ValidationException ex)
            {
                var url = SafeReturn(donus, $"/cariler/{cariId}/ekstre");
                return Results.Redirect(ErrorUrl(url, ex.Message)); // donus querystring'liyse '&' (çift-? düzeltmesi)
            }
        });

        grp.MapPost("/virman", async (CashService svc,
            [FromForm] string? kaynak, [FromForm] string? hedef, [FromForm] decimal tutar,
            [FromForm] string? aciklama, [FromForm] string? islemAnahtari,
            // FAZ-50: spesifik hesaplar + künye (makbuz no / işlem şubesi).
            [FromForm] string? kaynakHesapId, [FromForm] string? hedefHesapId,
            [FromForm] string? doviz, [FromForm] string? kur,
            [FromForm] string? makbuzNo, [FromForm] string? sube) =>
        {
            try
            {
                await svc.TransferAsync(ParseAccount(kaynak), ParseAccount(hedef), tutar,
                    currency: string.IsNullOrWhiteSpace(doviz) ? "TRY" : doviz, exchangeRate: FormParse.Dec(kur),
                    description: aciklama,
                    operationKey: FormParse.Id(islemAnahtari), // M5-takip: çift-submit idempotency
                    sourceAccountId: FormParse.Id(kaynakHesapId), targetAccountId: FormParse.Id(hedefHesapId),
                    receiptNo: makbuzNo, branch: sube);
                return Result.Ok("/kasa", "Virman kaydedildi.");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/kasa?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        // FAZ-56 — bakiye düzeltme: Kasa/Banka'ya DOKUNMAZ, karşı bacak "Muhasebe Düzeltmesi".
        grp.MapPost("/bakiye-duzeltme", async (BalanceAdjustmentService svc, HttpRequest req) =>
        {
            var f = req.Form;
            var returnInfo = FormParse.Str(f, "donus");
            try
            {
                await svc.AdjustAsync(new BakiyeDuzeltmeInput
                {
                    CariId = FormParse.Id(FormParse.Str(f, "cariId")) ?? Guid.Empty,
                    Tutar = FormParse.Dec(FormParse.Str(f, "tutar")) ?? 0m,
                    Yon = Enum.TryParse<BalanceAdjustmentDirection>(FormParse.Str(f, "yon"), out var y)
                        ? y : BalanceAdjustmentDirection.Alacaklandir,
                    Doviz = FormParse.Str(f, "doviz") ?? "TRY",
                    Kur = FormParse.Dec(FormParse.Str(f, "kur")),
                    Tarih = FormParse.Date(FormParse.Str(f, "tarih")),
                    Vade = FormParse.Date(FormParse.Str(f, "vade")),
                    MakbuzNo = FormParse.Str(f, "makbuzNo"),
                    Aciklama = FormParse.Str(f, "aciklama"),
                    IslemAnahtari = FormParse.Id(FormParse.Str(f, "islemAnahtari"))
                });
                return Result.Ok(SafeReturn(returnInfo, "/finans/bakiye-duzeltme"), "Bakiye düzeltmesi kaydedildi.");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect(ErrorUrl(SafeReturn(returnInfo, "/finans/bakiye-duzeltme"), ex.Message));
            }
        });

        // FAZ-54 — toplu faturalama. Her kira mevcut tekil kesim yolundan geçer (tek mantık).
        grp.MapPost("/fatura-toplu", async (InvoiceService svc, HttpRequest req) =>
        {
            var f = req.Form;
            var selection = f["secili"].ToArray()
                .Select(x => Guid.TryParse(x, out var g) ? g : Guid.Empty)
                .Where(x => x != Guid.Empty).Distinct().ToList();
            try
            {
                var result = await svc.BatchCreateFromRentalsAsync(selection, FormParse.Dec(FormParse.Str(f, "kdvOrani")));
                var url = $"/faturalar?ok=1";
                if (result.Atlananlar.Count > 0)
                {
                    // URL uzunluk sınırı: ilk 10 satır + gizlenenin SAYISI (FAZ-30 M3 dersi —
                    // sadece kesmek "atlanan yok" gibi okunuyordu).
                    var show = result.Atlananlar.Take(10).ToList();
                    if (result.Atlananlar.Count > 10)
                        show.Add($"… ve {result.Atlananlar.Count - 10} kayıt daha (toplam {result.Atlananlar.Count}).");
                    url += $"&atlanan={Uri.EscapeDataString(string.Join('|', show))}";
                }
                return Results.Redirect(url);
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/faturalar?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        grp.MapPost("/tahsilat/ters", async (CashService svc, [FromForm] Guid id, [FromForm] Guid cariId) =>
        {
            try { await svc.ReverseAsync(id); return Result.Ok($"/cariler/{cariId}/ekstre", "Tahsilat ters kayıtla iptal edildi."); }
            catch (ValidationException ex) { return Results.Redirect($"/cariler/{cariId}/ekstre?hata={Uri.EscapeDataString(ex.Message)}"); }
        }).RequirePermission(Permission.FinanceReverse);

        grp.MapPost("/fatura", async (InvoiceService svc, HttpRequest req) =>
        {
            var f = req.Form;
            var rentalId = FormParse.Id(f["rentalId"].ToString()) ?? Guid.Empty;
            string? S(string k) { var v = f[k].ToString(); return string.IsNullOrWhiteSpace(v) ? null : v; }
            bool B(string k) => S(k) is "true" or "True" or "on";
            // Opsiyonel vergi/belge metadata (bilgi amaçlı; defter postlamasına yansımaz).
            var tax = new InvoiceTaxInfo(
                Otv: FormParse.Dec(S("otv")),
                TevkifatOran: FormParse.Dec(S("tevkifatOran")),
                TevkifatTutar: FormParse.Dec(S("tevkifatTutar")),
                DamgaVergisi: FormParse.Dec(S("damgaVergisi")),
                IadeMi: B("iadeMi"),
                ManuelMi: B("manuelMi"));
            try { await svc.CreateFromRentalAsync(rentalId, tax: tax); return Result.Ok($"/kiralar/{rentalId}", "Fatura kesildi."); }
            catch (ValidationException ex) { return Results.Redirect($"/kiralar/{rentalId}?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/fatura-manuel", async (InvoiceService svc, HttpRequest req) =>
            await ManualInvoiceAsync(svc, req));

        // İade faturası (tam-fatura): kaynağa karşı ters kayıt. Kaynak başına tek iade.
        grp.MapPost("/fatura-iade", async (InvoiceService svc, HttpRequest req) =>
        {
            var source = FormParse.Id(req.Form["kaynakFaturaId"].ToString()) ?? Guid.Empty;
            try { await svc.CreateRefundAsync(source); return Result.Ok("/faturalar", "İade faturası kesildi."); }
            catch (ValidationException ex) { return Results.Redirect($"/faturalar?hata={Uri.EscapeDataString(ex.Message)}"); }
        }).RequirePermission(Permission.FinanceReverse);

        grp.MapPost("/cari-virman", async (CashService svc, HttpRequest req) =>
        {
            var f = req.Form;
            var source = FormParse.Id(f["kaynakCariId"].ToString()) ?? Guid.Empty;
            var target = FormParse.Id(f["hedefCariId"].ToString()) ?? Guid.Empty;
            var amount = FormParse.Dec(f["tutar"].ToString()) ?? 0m;
            var currency = f["doviz"].ToString();
            var exchangeRate = FormParse.Dec(f["kur"].ToString()); // boş → otomatik (1.1b)
            var description = f["aciklama"].ToString();
            var key = FormParse.Id(f["islemAnahtari"].ToString()); // çift-submit idempotency token
            try
            {
                await svc.TransferBetweenAccountsAsync(source, target, amount,
                    string.IsNullOrWhiteSpace(currency) ? "TRY" : currency, exchangeRate,
                    string.IsNullOrWhiteSpace(description) ? null : description, key,
                    // FAZ-59 künye alanları. "İşlem Yapan" formdan ALINMAZ — oturumdan yazılır.
                    date: FormParse.Date(f["tarih"].ToString()),
                    due: FormParse.Date(f["vade"].ToString()),
                    receiptNo: f["makbuzNo"].ToString(),
                    branch: f["sube"].ToString());
                return Result.Ok("/cari-virman", "Cari virman kaydedildi.");
            }
            catch (ValidationException ex) { return Results.Redirect($"/cari-virman?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/toplu-tahsilat", async (CashService svc, HttpRequest req) =>
        {
            var f = req.Form;
            var key = FormParse.Id(f["islemAnahtari"].ToString()); // çift-submit idempotency token
            var account = string.Equals(f["hesap"].ToString(), "Banka", StringComparison.OrdinalIgnoreCase)
                ? LedgerAccountType.Banka : LedgerAccountType.Kasa;
            var channel = FormParse.Str(f, "kanal"); // FAZ-84: tek seçim, batch'in TÜM satırlarına uygulanır
            // Her satır: "cariId;tutar[;açıklama]" (boş satırlar atlanır).
            var rows = new List<CashInput>();
            foreach (var line in (f["satirlar"].ToString() ?? string.Empty)
                         .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var p = line.Split(';', StringSplitOptions.TrimEntries);
                rows.Add(new CashInput
                {
                    CariId = FormParse.Id(p.Length > 0 ? p[0] : null) ?? Guid.Empty,
                    Tutar = FormParse.Dec(p.Length > 1 ? p[1] : null) ?? 0m,
                    Hesap = account,
                    Doviz = "TRY",
                    Kur = 1m,
                    Aciklama = p.Length > 2 && !string.IsNullOrWhiteSpace(p[2]) ? p[2] : "Toplu tahsilat",
                    Kanal = channel
                });
            }
            try
            {
                await svc.BatchCollectAsync(rows, key);
                return Results.Redirect($"/toplu-tahsilat?ok={rows.Count}");
            }
            catch (ValidationException ex) { return Results.Redirect($"/toplu-tahsilat?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/toplu-gider", async (ExpenseService svc, VehicleService araclar, HttpRequest req) =>
        {
            var f = req.Form;
            var key = FormParse.Id(f["islemAnahtari"].ToString()); // çift-submit idempotency token
            var tip = Enum.TryParse<ExpenseType>(f["tip"].ToString(), out var t) ? t : ExpenseType.Genel;
            var payment = Enum.TryParse<PaymentMethod>(f["odemeYontemi"].ToString(), out var o) ? o : PaymentMethod.Nakit;
            var account = payment == PaymentMethod.Banka ? LedgerAccountType.Banka : LedgerAccountType.Kasa;
            var vatRate = FormParse.Dec(f["kdvOrani"].ToString()) ?? 0m;
            var customerId = FormParse.Id(FormParse.Str(f, "cariId"));
            var due = FormParse.Date(FormParse.Str(f, "vade"));
            var financialAccountId = FormParse.Id(FormParse.Str(f, "finansalHesapId"));

            // FAZ-29: plaka satır bazlı. Plakayı ARAÇ ID'sine çözmek için tek liste okunur;
            // eşleşme plaka normalizasyonuyla (boşluk/harf duyarsız) yapılır — kullanıcı
            // "34 abc 34" yazdığında da tutsun.
            var plateIndex = (await araclar.ListAsync())
                .GroupBy(v => VehicleService.PlateKey(v.Plaka))
                .ToDictionary(g => g.Key, g => g.First().Id);

            try
            {
                // Her satır: "netTutar[;açıklama][;plaka]" (boş satırlar atlanır).
                var items = new List<ExpenseInput>();
                var rowNo = 0;
                foreach (var line in (f["satirlar"].ToString() ?? string.Empty)
                             .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    rowNo++;
                    var p = line.Split(';', StringSplitOptions.TrimEntries);
                    Guid? vehicleId = null;
                    if (p.Length > 2 && !string.IsNullOrWhiteSpace(p[2]))
                    {
                        // TANINMAYAN PLAKA GÜRÜLTÜLÜ REDDEDİLİR: sessizce null bırakmak gideri
                        // "(Atanmamış)"a yazar ve araç karnesi eksik kalırdı.
                        if (!plateIndex.TryGetValue(VehicleService.PlateKey(p[2]), out var vid))
                            throw new ValidationException($"Satır {rowNo}: '{p[2]}' plakalı araç bulunamadı.");
                        vehicleId = vid;
                    }
                    items.Add(new ExpenseInput
                    {
                        Tip = tip,
                        NetTutar = FormParse.Dec(p.Length > 0 ? p[0] : null) ?? 0m,
                        KdvOrani = vatRate,
                        Doviz = "TRY",
                        Kur = 1m,
                        OdemeYontemi = payment,
                        KasaBankaHesap = account,
                        VehicleId = vehicleId,
                        CariId = customerId,
                        Vade = due,
                        FinansalHesapId = financialAccountId,
                        Aciklama = p.Length > 1 && !string.IsNullOrWhiteSpace(p[1]) ? p[1] : "Toplu gider"
                    });
                }

                await svc.BatchCreateAsync(items, key);
                return Results.Redirect($"/toplu-gider?ok={items.Count}");
            }
            catch (ValidationException ex) { return Results.Redirect($"/toplu-gider?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        // FAZ-30 — dönem faturası/tahsilatı ELLE tetikleme (seçili dönemler).
        grp.MapPost("/otomatik-tahsilat/calistir", async (
            RentACar.Application.FaturaDonemleri.AutoCollectionService svc, HttpRequest req) =>
        {
            if (!req.HasFormContentType) return Results.BadRequest();
            var f = req.Form;
            var back = "/otomatik-tahsilat";
            try
            {
                // "rentalId:donemSira" çiftleri. Bozuk değer SESSİZCE ATLANMAZ — sessiz eleme
                // kullanıcının çalıştırdığını sandığı dönemin atlanması demekti.
                var selection = new List<(Guid, int)>();
                foreach (var raw in f["secili"])
                {
                    var p = (raw ?? string.Empty).Split(':');
                    if (p.Length != 2 || FormParse.Id(p[0]) is not { } rid || FormParse.Int(p[1]) is not { } order)
                        throw new ValidationException("Seçim okunamadı; listeyi yenileyip tekrar deneyin.");
                    selection.Add((rid, order));
                }
                // Hesap SESSİZCE Kasa'ya düşmez (adversarial L2): servisteki Kasa/Banka çiti
                // web yolundan erişilemez hâle geliyordu.
                var accountText = f["hesap"].ToString();
                if (!string.Equals(accountText, "Kasa", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(accountText, "Banka", StringComparison.OrdinalIgnoreCase))
                    throw new ValidationException("Hesap Kasa ya da Banka olmalıdır.");
                var account = ParseAccount(accountText);

                var result = await svc.RunAsync(selection, f["tahsilat"].ToString() is "true" or "on", account);

                var q = $"{back}?ok={Uri.EscapeDataString($"{result.Kesilen} dönem kesildi, {result.Tahsilat} tahsilat yazıldı.")}";
                if (result.Atlananlar.Count > 0)
                {
                    // URL sınırı yüzünden ilk 10 gösterilir ama KALANI SAYILIR (adversarial M3):
                    // sessizce yutmak "atlanan yok" gibi okunuyordu.
                    var show = RentACar.Application.FaturaDonemleri.AutoCollectionService
                        .ShowSkipped(result.Atlananlar);
                    q += "&atlanan=" + Uri.EscapeDataString(string.Join("|", show));
                }
                return Results.Redirect(q);
            }
            catch (ValidationException ex)
            { return Results.Redirect(ErrorUrl(back, ex.Message)); }
        });

        // FAZ-29 — tek cari, ekstresinden seçilen BORÇ kalemlerini toplu kapatma.
        grp.MapPost("/tek-cari-kapat", async (CashService svc, HttpRequest req) =>
        {
            var f = req.Form;
            var customerId = FormParse.Id(FormParse.Str(f, "cariId")) ?? Guid.Empty;
            var back = $"/tek-cari-toplu?cariId={customerId}";
            try
            {
                // Seçim: her işaretli kalem için "secili" (id) + isteğe bağlı "tutar_{id}" (kısmi
                // kapatma). Bozuk Guid SESSİZCE ATLANMAZ (adversarial L1) — sessiz eleme,
                // kullanıcının seçtiğinden farklı bir tutar tahsil edilmesi demekti.
                var selection = new Dictionary<Guid, decimal?>();
                foreach (var raw in f["secili"])
                {
                    var id = FormParse.Id(raw)
                        ?? throw new ValidationException("Seçilen kalemlerden biri okunamadı; listeyi yenileyin.");
                    selection[id] = FormParse.Dec(FormParse.Str(f, $"tutar_{id}"));
                }
                var account = Enum.TryParse<LedgerAccountType>(f["hesap"].ToString(), out var h)
                    ? h : LedgerAccountType.Kasa;
                var amount = await svc.CloseSingleAccountBulkAsync(
                    customerId, selection, account,
                    date: FormParse.Date(FormParse.Str(f, "tarih")),
                    description: FormParse.Str(f, "aciklama"),
                    operationKey: FormParse.Id(f["islemAnahtari"].ToString()),
                    channel: FormParse.Str(f, "kanal")); // FAZ-84
                return Results.Redirect($"{back}&ok={Uri.EscapeDataString(amount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture))}");
            }
            catch (ValidationException ex) { return Results.Redirect($"{back}&hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        return app;
    }

    /// <summary>Manuel/serbest fatura (roadmap G2) — kiradan bağımsız fatura kesimi.</summary>
    private static async Task<IResult> ManualInvoiceAsync(InvoiceService svc, HttpRequest req)
    {
        var f = req.Form;
        string? S(string k) { var v = f[k].ToString(); return string.IsNullOrWhiteSpace(v) ? null : v; }
        var input = new ManualInvoiceInput
        {
            CariId = FormParse.Id(S("cariId")) ?? Guid.Empty,
            Aciklama = S("aciklama"),
            NetTutar = FormParse.Dec(S("netTutar")) ?? 0m,
            KdvOrani = FormParse.Dec(S("kdvOrani")) ?? 0.20m,
            Tarih = FormParse.Date(S("tarih")),
            VadeTarihi = FormParse.Date(S("vadeTarihi")),
            IslemAnahtari = FormParse.Id(S("islemAnahtari")), // çift-submit idempotency (adversarial M#1)
            // FAZ-51 — bilgi alanları (defter/bakiyeye YANSIMAZ).
            IslemSube = S("islemSube"),
            EvrakNo = S("evrakNo"),
            FaturaOzelKod = S("faturaOzelKod"),
            OdemeTuru = S("odemeTuru"),
            GonderimSekli = S("gonderimSekli"),
            KdvSifirSebep = S("kdvSifirSebep"),
            // ÖTV/tevkifat/damga — mevcut InvoiceTaxInfo/ApplyVergi (kira-fatura yoluyla AYNI doğrulama).
            // IadeMi/ManuelMi bilinçli OLARAK verilmiyor (default false) — servis bunları kendi
            // invariant'ı olarak sonradan zorlar (ApplyVergi sonrası invoice.ManuelMi=true).
            Vergi = new InvoiceTaxInfo(
                Otv: FormParse.Dec(S("otv")),
                TevkifatOran: FormParse.Dec(S("tevkifatOran")),
                TevkifatTutar: FormParse.Dec(S("tevkifatTutar")),
                DamgaVergisi: FormParse.Dec(S("damgaVergisi")))
        };
        try
        {
            await svc.CreateManualAsync(input);
            return Results.Redirect("/faturalar?ok=1");
        }
        catch (ValidationException ex) { return Results.Redirect($"/faturalar?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
