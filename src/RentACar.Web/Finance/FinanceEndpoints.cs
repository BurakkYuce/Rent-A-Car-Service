using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.Finance;

/// <summary>Nakit tahsilat/ödeme + virman + ters kayıt form post uçları.</summary>
public static class FinanceEndpoints
{
    /// <summary>Form "Kasa"/"Banka" metnini hesap tipine çevirir (varsayılan Kasa).</summary>
    private static LedgerAccountType ParseHesap(string? s)
        => string.Equals(s, "Banka", StringComparison.OrdinalIgnoreCase)
            ? LedgerAccountType.Banka : LedgerAccountType.Kasa;

    /// <summary>Açık-yönlendirme savunması (adversarial): donus formdan gelir; yalnız site-içi GÖRELİ yol kabul
    /// edilir, harici/protokol-göreli (//evil, /\evil) → güvenli fallback (aksi halde phishing yönlendirmesi).</summary>
    internal static string SafeDonus(string? donus, string fallback) // Depozito uçlarıyla paylaşılır
        => !string.IsNullOrEmpty(donus) && donus[0] == '/'
           && !(donus.Length > 1 && (donus[1] == '/' || donus[1] == '\\'))
            ? donus : fallback;

    /// <summary>Hata mesajını dönüş URL'ine doğru ayırıcıyla ekler: donus zaten querystring içeriyorsa
    /// (ör. pano "/?df=gec") '?hata=' İKİNCİ '?' üretip mesajı önceki parametreye yutturuyordu → '&'.</summary>
    internal static string HataUrl(string url, string mesaj)
        => $"{url}{(url.Contains('?') ? '&' : '?')}hata={Uri.EscapeDataString(mesaj)}";

    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/finans").RequirePermission(Permission.FinanceWrite).AntiforgeryByEnv();

        // FAZ 4.3: B2B dış hizmet alımı — TAM DEFTERLİ (gider araçta + komisyon geliri); ters kayıtla iptal.
        grp.MapPost("/dis-hizmet", async (RentACar.Application.DisHizmetler.DisHizmetService svc, HttpRequest req,
            [FromForm] Guid rentalId, [FromForm] Guid cariId, [FromForm] string alinanHizmet,
            [FromForm] string? hizmetBedeli, [FromForm] string? komisyonOran, [FromForm] string? doviz,
            [FromForm] string? kur, [FromForm] string? aciklama, [FromForm] Guid islemAnahtari,
            [FromForm] string? donus) =>
        {
            var geri = SafeDonus(donus, "/kiralar/" + rentalId);
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
                return Results.Redirect($"{geri}?ok=1");
            }
            catch (ValidationException ex)
            { return Results.Redirect($"{geri}?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/dis-hizmet-iptal", async (RentACar.Application.DisHizmetler.DisHizmetService svc,
            [FromForm] Guid id, [FromForm] string? donus) =>
        {
            var geri = SafeDonus(donus, "/kiralar");
            try { await svc.IptalEtAsync(id); return Results.Redirect($"{geri}?ok=1"); }
            catch (ValidationException ex)
            { return Results.Redirect($"{geri}?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        // FAZ 4.2-B3: dönem faturası kes (+opsiyonel tahsilat kaydı). Çift-submit güvenli: fatura
        // idempotent (Kesildi→mevcut), tahsilat deterministik RowKey(rentalId, donemSira) anahtarlı.
        grp.MapPost("/donem-fatura", async (RentACar.Application.FaturaDonemleri.DonemTahsilatService svc,
            [FromForm] Guid rentalId, [FromForm] int donemSira, [FromForm] string? tahsilat,
            [FromForm] string? hesap, [FromForm] string? donus) =>
        {
            var geri = SafeDonus(donus, "/kiralar/" + rentalId);
            try
            {
                await svc.KesVeTahsilEtAsync(rentalId, donemSira,
                    tahsilat is "true" or "on",
                    string.Equals(hesap, "Banka", StringComparison.OrdinalIgnoreCase)
                        ? LedgerAccountType.Banka : LedgerAccountType.Kasa);
                return Results.Redirect($"{geri}?ok=1");
            }
            catch (ValidationException ex)
            { return Results.Redirect($"{geri}?hata={Uri.EscapeDataString(ex.Message)}"); }
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
                    Aciklama = aciklama, Hesap = ParseHesap(hesap), IslemAnahtari = FormParse.Id(islemAnahtari), // M5
                    HesapId = FormParse.Id(hesapId),  // FAZ-50: hangi spesifik kasa/banka
                    Kanal = kanal, // FAZ-84: boş → CashService "Masaüstü" varsayılanı
                    Tarih = FormParse.Date(tarih)  // FAZ-67: boş → sunucu "şimdi" (eski davranış)
                });
                return Results.Redirect(SafeDonus(donus, $"/cariler/{cariId}/ekstre"));
            }
            catch (ValidationException ex)
            {
                RentACar.Application.Observability.RacarMetrics.TahsilatFail(); // metrik: tahsilat reddi (döviz/kilit/idempotent…)
                var url = SafeDonus(donus, $"/cariler/{cariId}/ekstre");
                return Results.Redirect(HataUrl(url, ex.Message)); // donus querystring'liyse '&' (çift-? düzeltmesi)
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
                    Aciklama = aciklama, Hesap = ParseHesap(hesap), IslemAnahtari = FormParse.Id(islemAnahtari), // M5
                    HesapId = FormParse.Id(hesapId),  // FAZ-50: hangi spesifik kasa/banka
                    Kanal = kanal, // FAZ-84
                    Tarih = FormParse.Date(tarih)  // FAZ-67
                });
                return Results.Redirect(SafeDonus(donus, $"/cariler/{cariId}/ekstre"));
            }
            catch (ValidationException ex)
            {
                var url = SafeDonus(donus, $"/cariler/{cariId}/ekstre");
                return Results.Redirect(HataUrl(url, ex.Message)); // donus querystring'liyse '&' (çift-? düzeltmesi)
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
                await svc.TransferAsync(ParseHesap(kaynak), ParseHesap(hedef), tutar,
                    doviz: string.IsNullOrWhiteSpace(doviz) ? "TRY" : doviz, kur: FormParse.Dec(kur),
                    aciklama: aciklama,
                    islemAnahtari: FormParse.Id(islemAnahtari), // M5-takip: çift-submit idempotency
                    kaynakHesapId: FormParse.Id(kaynakHesapId), hedefHesapId: FormParse.Id(hedefHesapId),
                    makbuzNo: makbuzNo, sube: sube);
                return Results.Redirect("/kasa");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/kasa?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        grp.MapPost("/tahsilat/ters", async (CashService svc, [FromForm] Guid id, [FromForm] Guid cariId) =>
        {
            try { await svc.ReverseAsync(id); return Results.Redirect($"/cariler/{cariId}/ekstre"); }
            catch (ValidationException ex) { return Results.Redirect($"/cariler/{cariId}/ekstre?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/fatura", async (InvoiceService svc, HttpRequest req) =>
        {
            var f = req.Form;
            var rentalId = FormParse.Id(f["rentalId"].ToString()) ?? Guid.Empty;
            string? S(string k) { var v = f[k].ToString(); return string.IsNullOrWhiteSpace(v) ? null : v; }
            bool B(string k) => S(k) is "true" or "True" or "on";
            // Opsiyonel vergi/belge metadata (bilgi amaçlı; defter postlamasına yansımaz).
            var vergi = new InvoiceTaxInfo(
                Otv: FormParse.Dec(S("otv")),
                TevkifatOran: FormParse.Dec(S("tevkifatOran")),
                TevkifatTutar: FormParse.Dec(S("tevkifatTutar")),
                DamgaVergisi: FormParse.Dec(S("damgaVergisi")),
                IadeMi: B("iadeMi"),
                ManuelMi: B("manuelMi"));
            try { await svc.CreateFromRentalAsync(rentalId, vergi: vergi); return Results.Redirect($"/kiralar/{rentalId}"); }
            catch (ValidationException ex) { return Results.Redirect($"/kiralar/{rentalId}?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/fatura-manuel", async (InvoiceService svc, HttpRequest req) =>
            await ManuelFaturaAsync(svc, req));

        // İade faturası (tam-fatura): kaynağa karşı ters kayıt. Kaynak başına tek iade.
        grp.MapPost("/fatura-iade", async (InvoiceService svc, HttpRequest req) =>
        {
            var kaynak = FormParse.Id(req.Form["kaynakFaturaId"].ToString()) ?? Guid.Empty;
            try { await svc.CreateIadeAsync(kaynak); return Results.Redirect("/faturalar?ok=1"); }
            catch (ValidationException ex) { return Results.Redirect($"/faturalar?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/cari-virman", async (CashService svc, HttpRequest req) =>
        {
            var f = req.Form;
            var kaynak = FormParse.Id(f["kaynakCariId"].ToString()) ?? Guid.Empty;
            var hedef = FormParse.Id(f["hedefCariId"].ToString()) ?? Guid.Empty;
            var tutar = FormParse.Dec(f["tutar"].ToString()) ?? 0m;
            var doviz = f["doviz"].ToString();
            var kur = FormParse.Dec(f["kur"].ToString()); // boş → otomatik (1.1b)
            var aciklama = f["aciklama"].ToString();
            var anahtar = FormParse.Id(f["islemAnahtari"].ToString()); // çift-submit idempotency token
            try
            {
                await svc.TransferBetweenCariAsync(kaynak, hedef, tutar,
                    string.IsNullOrWhiteSpace(doviz) ? "TRY" : doviz, kur,
                    string.IsNullOrWhiteSpace(aciklama) ? null : aciklama, anahtar,
                    // FAZ-59 künye alanları. "İşlem Yapan" formdan ALINMAZ — oturumdan yazılır.
                    tarih: FormParse.Date(f["tarih"].ToString()),
                    vade: FormParse.Date(f["vade"].ToString()),
                    makbuzNo: f["makbuzNo"].ToString(),
                    sube: f["sube"].ToString());
                return Results.Redirect("/cari-virman?ok=1");
            }
            catch (ValidationException ex) { return Results.Redirect($"/cari-virman?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/toplu-tahsilat", async (CashService svc, HttpRequest req) =>
        {
            var f = req.Form;
            var anahtar = FormParse.Id(f["islemAnahtari"].ToString()); // çift-submit idempotency token
            var hesap = string.Equals(f["hesap"].ToString(), "Banka", StringComparison.OrdinalIgnoreCase)
                ? LedgerAccountType.Banka : LedgerAccountType.Kasa;
            var kanal = FormParse.Str(f, "kanal"); // FAZ-84: tek seçim, batch'in TÜM satırlarına uygulanır
            // Her satır: "cariId;tutar[;açıklama]" (boş satırlar atlanır).
            var satirlar = new List<CashInput>();
            foreach (var line in (f["satirlar"].ToString() ?? string.Empty)
                         .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var p = line.Split(';', StringSplitOptions.TrimEntries);
                satirlar.Add(new CashInput
                {
                    CariId = FormParse.Id(p.Length > 0 ? p[0] : null) ?? Guid.Empty,
                    Tutar = FormParse.Dec(p.Length > 1 ? p[1] : null) ?? 0m,
                    Hesap = hesap,
                    Doviz = "TRY",
                    Kur = 1m,
                    Aciklama = p.Length > 2 && !string.IsNullOrWhiteSpace(p[2]) ? p[2] : "Toplu tahsilat",
                    Kanal = kanal
                });
            }
            try
            {
                await svc.BatchCollectAsync(satirlar, anahtar);
                return Results.Redirect($"/toplu-tahsilat?ok={satirlar.Count}");
            }
            catch (ValidationException ex) { return Results.Redirect($"/toplu-tahsilat?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/toplu-gider", async (ExpenseService svc, VehicleService araclar, HttpRequest req) =>
        {
            var f = req.Form;
            var anahtar = FormParse.Id(f["islemAnahtari"].ToString()); // çift-submit idempotency token
            var tip = Enum.TryParse<ExpenseType>(f["tip"].ToString(), out var t) ? t : ExpenseType.Genel;
            var odeme = Enum.TryParse<OdemeYontemi>(f["odemeYontemi"].ToString(), out var o) ? o : OdemeYontemi.Nakit;
            var hesap = odeme == OdemeYontemi.Banka ? LedgerAccountType.Banka : LedgerAccountType.Kasa;
            var kdvOrani = FormParse.Dec(f["kdvOrani"].ToString()) ?? 0m;
            var cariId = FormParse.Id(FormParse.Str(f, "cariId"));
            var vade = FormParse.Date(FormParse.Str(f, "vade"));
            var finansalHesapId = FormParse.Id(FormParse.Str(f, "finansalHesapId"));

            // FAZ-29: plaka satır bazlı. Plakayı ARAÇ ID'sine çözmek için tek liste okunur;
            // eşleşme plaka normalizasyonuyla (boşluk/harf duyarsız) yapılır — kullanıcı
            // "34 abc 34" yazdığında da tutsun.
            var plakaIndex = (await araclar.ListAsync())
                .GroupBy(v => VehicleService.PlakaAnahtar(v.Plaka))
                .ToDictionary(g => g.Key, g => g.First().Id);

            try
            {
                // Her satır: "netTutar[;açıklama][;plaka]" (boş satırlar atlanır).
                var kalemler = new List<ExpenseInput>();
                var satirNo = 0;
                foreach (var line in (f["satirlar"].ToString() ?? string.Empty)
                             .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    satirNo++;
                    var p = line.Split(';', StringSplitOptions.TrimEntries);
                    Guid? vehicleId = null;
                    if (p.Length > 2 && !string.IsNullOrWhiteSpace(p[2]))
                    {
                        // TANINMAYAN PLAKA GÜRÜLTÜLÜ REDDEDİLİR: sessizce null bırakmak gideri
                        // "(Atanmamış)"a yazar ve araç karnesi eksik kalırdı.
                        if (!plakaIndex.TryGetValue(VehicleService.PlakaAnahtar(p[2]), out var vid))
                            throw new ValidationException($"Satır {satirNo}: '{p[2]}' plakalı araç bulunamadı.");
                        vehicleId = vid;
                    }
                    kalemler.Add(new ExpenseInput
                    {
                        Tip = tip,
                        NetTutar = FormParse.Dec(p.Length > 0 ? p[0] : null) ?? 0m,
                        KdvOrani = kdvOrani,
                        Doviz = "TRY",
                        Kur = 1m,
                        OdemeYontemi = odeme,
                        KasaBankaHesap = hesap,
                        VehicleId = vehicleId,
                        CariId = cariId,
                        Vade = vade,
                        FinansalHesapId = finansalHesapId,
                        Aciklama = p.Length > 1 && !string.IsNullOrWhiteSpace(p[1]) ? p[1] : "Toplu gider"
                    });
                }

                await svc.BatchCreateAsync(kalemler, anahtar);
                return Results.Redirect($"/toplu-gider?ok={kalemler.Count}");
            }
            catch (ValidationException ex) { return Results.Redirect($"/toplu-gider?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        // FAZ-30 — dönem faturası/tahsilatı ELLE tetikleme (seçili dönemler).
        grp.MapPost("/otomatik-tahsilat/calistir", async (
            RentACar.Application.FaturaDonemleri.OtomatikTahsilatService svc, HttpRequest req) =>
        {
            if (!req.HasFormContentType) return Results.BadRequest();
            var f = req.Form;
            var geri = "/otomatik-tahsilat";
            try
            {
                // "rentalId:donemSira" çiftleri. Bozuk değer SESSİZCE ATLANMAZ — sessiz eleme
                // kullanıcının çalıştırdığını sandığı dönemin atlanması demekti.
                var secim = new List<(Guid, int)>();
                foreach (var ham in f["secili"])
                {
                    var p = (ham ?? string.Empty).Split(':');
                    if (p.Length != 2 || FormParse.Id(p[0]) is not { } rid || FormParse.Int(p[1]) is not { } sira)
                        throw new ValidationException("Seçim okunamadı; listeyi yenileyip tekrar deneyin.");
                    secim.Add((rid, sira));
                }
                // Hesap SESSİZCE Kasa'ya düşmez (adversarial L2): servisteki Kasa/Banka çiti
                // web yolundan erişilemez hâle geliyordu.
                var hesapMetin = f["hesap"].ToString();
                if (!string.Equals(hesapMetin, "Kasa", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(hesapMetin, "Banka", StringComparison.OrdinalIgnoreCase))
                    throw new ValidationException("Hesap Kasa ya da Banka olmalıdır.");
                var hesap = ParseHesap(hesapMetin);

                var sonuc = await svc.CalistirAsync(secim, f["tahsilat"].ToString() is "true" or "on", hesap);

                var q = $"{geri}?ok={Uri.EscapeDataString($"{sonuc.Kesilen} dönem kesildi, {sonuc.Tahsilat} tahsilat yazıldı.")}";
                if (sonuc.Atlananlar.Count > 0)
                {
                    // URL sınırı yüzünden ilk 10 gösterilir ama KALANI SAYILIR (adversarial M3):
                    // sessizce yutmak "atlanan yok" gibi okunuyordu.
                    var goster = RentACar.Application.FaturaDonemleri.OtomatikTahsilatService
                        .AtlananGoster(sonuc.Atlananlar);
                    q += "&atlanan=" + Uri.EscapeDataString(string.Join("|", goster));
                }
                return Results.Redirect(q);
            }
            catch (ValidationException ex)
            { return Results.Redirect(HataUrl(geri, ex.Message)); }
        });

        // FAZ-29 — tek cari, ekstresinden seçilen BORÇ kalemlerini toplu kapatma.
        grp.MapPost("/tek-cari-kapat", async (CashService svc, HttpRequest req) =>
        {
            var f = req.Form;
            var cariId = FormParse.Id(FormParse.Str(f, "cariId")) ?? Guid.Empty;
            var geri = $"/tek-cari-toplu?cariId={cariId}";
            try
            {
                // Seçim: her işaretli kalem için "secili" (id) + isteğe bağlı "tutar_{id}" (kısmi
                // kapatma). Bozuk Guid SESSİZCE ATLANMAZ (adversarial L1) — sessiz eleme,
                // kullanıcının seçtiğinden farklı bir tutar tahsil edilmesi demekti.
                var secim = new Dictionary<Guid, decimal?>();
                foreach (var ham in f["secili"])
                {
                    var id = FormParse.Id(ham)
                        ?? throw new ValidationException("Seçilen kalemlerden biri okunamadı; listeyi yenileyin.");
                    secim[id] = FormParse.Dec(FormParse.Str(f, $"tutar_{id}"));
                }
                var hesap = Enum.TryParse<LedgerAccountType>(f["hesap"].ToString(), out var h)
                    ? h : LedgerAccountType.Kasa;
                var tutar = await svc.TekCariTopluKapatAsync(
                    cariId, secim, hesap,
                    tarih: FormParse.Date(FormParse.Str(f, "tarih")),
                    aciklama: FormParse.Str(f, "aciklama"),
                    islemAnahtari: FormParse.Id(f["islemAnahtari"].ToString()),
                    kanal: FormParse.Str(f, "kanal")); // FAZ-84
                return Results.Redirect($"{geri}&ok={Uri.EscapeDataString(tutar.ToString("N2", System.Globalization.CultureInfo.InvariantCulture))}");
            }
            catch (ValidationException ex) { return Results.Redirect($"{geri}&hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        return app;
    }

    /// <summary>Manuel/serbest fatura (roadmap G2) — kiradan bağımsız fatura kesimi.</summary>
    private static async Task<IResult> ManuelFaturaAsync(InvoiceService svc, HttpRequest req)
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
