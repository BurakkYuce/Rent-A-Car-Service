using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Web.Identity;

namespace RentACar.Web.Bookings;

/// <summary>Rezervasyon + kira form post uçları. Tenant HttpContext claim'inden (RLS).</summary>
public static class BookingEndpoints
{
    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var rez = app.MapGroup("/rezervasyonlar").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        // gunlukUcret string? + FormParse.Dec: "Otomatik" fiyat türünde alan boş bırakılır — boş string
        // decimal parametrede 400 verirdi (CLAUDE.md §5 tuzağı). Boş → 0 → tarife çözümü.
        rez.MapPost("/create", async (ReservationService svc, HttpRequest req,
            [FromForm] Guid musteriId, [FromForm] Guid vehicleId,
            [FromForm] DateTimeOffset basTar, [FromForm] DateTimeOffset bitTar,
            [FromForm] string? gunlukUcret, [FromForm] string? cikisOfisi, [FromForm] string? donusOfisi,
            [FromForm] string? aciklama, [FromForm] string? kaynak) =>
        {
            try
            {
                var input = new BookingInput
                {
                    MusteriId = musteriId, VehicleId = vehicleId, BasTar = basTar, BitTar = bitTar,
                    GunlukUcret = FormParse.Dec(gunlukUcret) ?? 0m, CikisOfisi = cikisOfisi, DonusOfisi = donusOfisi, Aciklama = aciklama,
                    Kaynak = kaynak,
                    KampanyaKodu = FormParse.Str(req.Form, "kampanyaKodu") // FAZ 3.A5
                };
                ApplyOdemeDerinlik(input, req.Form);
                await svc.CreateAsync(input);
                return Results.Redirect("/rezervasyonlar");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/rezervasyonlar?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        rez.MapPost("/update", async (ReservationService svc, HttpRequest req,
            [FromForm] Guid id, [FromForm] Guid musteriId, [FromForm] Guid vehicleId,
            [FromForm] DateTimeOffset basTar, [FromForm] DateTimeOffset bitTar,
            [FromForm] string? gunlukUcret, [FromForm] string? cikisOfisi, [FromForm] string? donusOfisi,
            [FromForm] string? aciklama, [FromForm] string? kaynak) =>
        {
            try
            {
                var input = new BookingInput
                {
                    MusteriId = musteriId, VehicleId = vehicleId, BasTar = basTar, BitTar = bitTar,
                    GunlukUcret = FormParse.Dec(gunlukUcret) ?? 0m, CikisOfisi = cikisOfisi, DonusOfisi = donusOfisi, Aciklama = aciklama,
                    Kaynak = kaynak,
                    KampanyaKodu = FormParse.Str(req.Form, "kampanyaKodu") // FAZ 3.A5
                };
                ApplyOdemeDerinlik(input, req.Form);
                await svc.UpdateAsync(id, input);
                return Results.Redirect("/rezervasyonlar?ok=1");
            }
            catch (AvailabilityConflictException) { return Results.Redirect($"/rezervasyonlar?hata={Uri.EscapeDataString("Seçilen tarihte araç müsait değil.")}"); }
            catch (ValidationException ex) { return Results.Redirect($"/rezervasyonlar?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        rez.MapPost("/confirm", async (ReservationService svc, [FromForm] Guid id) =>
        {
            try { await svc.ConfirmAsync(id); return Results.Redirect("/rezervasyonlar"); }
            catch (ValidationException ex) { return Results.Redirect($"/rezervasyonlar?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        rez.MapPost("/cancel", async (ReservationService svc, [FromForm] Guid id) =>
        {
            try { await svc.CancelAsync(id); return Results.Redirect("/rezervasyonlar"); }
            catch (ValidationException ex) { return Results.Redirect($"/rezervasyonlar?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        rez.MapPost("/convert", async (ReservationService svc, [FromForm] Guid id) =>
        {
            try
            {
                await svc.ConvertToRentalAsync(id);
                return Results.Redirect("/kiralar");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/rezervasyonlar?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        var kira = app.MapGroup("/kiralar").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv(); // adversarial H1

        kira.MapPost("/create", async (RentalService svc, CustomerService customers,
            RentACar.Application.RentalAddOns.RentalAddOnService addOns,
            RentACar.Application.EkHizmetler.EkHizmetTanimService ekTanimlar, HttpRequest req,
            [FromForm] string? musteriId, [FromForm] Guid vehicleId,
            [FromForm] DateTimeOffset basTar, [FromForm] DateTimeOffset bitTar,
            [FromForm] string? gunlukUcret, [FromForm] string? cikisOfisi, [FromForm] string? donusOfisi,
            [FromForm] string? aciklama, [FromForm] string? ikinciSurucuId) =>
        {
            Guid id;
            List<(Guid TanimId, decimal Miktar)> ekSecim;
            try
            {
                // Müşteri: mevcut cari seçildi mi (musteriId), yoksa kira ekranından yeni müşteri mi girildi?
                // Tek akış — önce cari oluştur, sonra kira ona bağlanır (ayrı ekranda cari açma zorunluluğu kalktı).
                var musteriGuid = FormParse.Id(musteriId) ?? await OlusturYeniCariAsync(customers, req.Form);

                var input = new BookingInput
                {
                    MusteriId = musteriGuid, VehicleId = vehicleId, BasTar = basTar, BitTar = bitTar,
                    IkinciSurucuId = FormParse.Id(ikinciSurucuId),
                    GunlukUcret = FormParse.Dec(gunlukUcret) ?? 0m, CikisOfisi = cikisOfisi, DonusOfisi = donusOfisi, Aciklama = aciklama
                };
                ApplyOdemeDerinlik(input, req.Form);
                ApplyKiraDetay(input, req.Form);

                // Ek hizmet seçimleri (mega-form matrisi): tanımlar create ÖNCESİ doğrulanır — kira, geçersiz
                // seçimle yarım kalmasın. Fiyat DAİMA tanım snapshot'ı (serbest-fiyat override bilinçli YOK —
                // canlı önizleme /kiralar/hesapla ile bit-eş kalsın; adversarial PR-B notu).
                ekSecim = ParseEkForm(req.Form);
                if (ekSecim.Count > 0)
                {
                    var tanimlar = (await ekTanimlar.ListActiveAsync()).Select(t => t.Id).ToHashSet();
                    if (ekSecim.Any(e => !tanimlar.Contains(e.TanimId)))
                        throw new ValidationException("Seçilen ek hizmet tanımı bulunamadı (silinmiş/pasif olabilir).");
                }

                id = await svc.CreateDirectAsync(input);
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/kiralar/yeni?hata={Uri.EscapeDataString(ex.Message)}");
            }
            // Kira oluştu — ek hizmet kalemleri (aynı AddAsync yolu: tanım snapshot + RentalTotals.Recompute).
            // Yarıda hata (ön-doğrulamayla dar yarış penceresi) → kira DURUR, detayda hata gösterilir;
            // kalemler detay ekranından eklenebilir.
            try
            {
                foreach (var (tanimId, miktar) in ekSecim)
                    await addOns.AddAsync(id, tanimId, miktar);
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/kiralar/{id}?hata={Uri.EscapeDataString($"Kira açıldı ancak ek hizmet eklenemedi: {ex.Message}")}");
            }
            // Kira açılınca DETAY ekranına git → tahsilat formu (Bakiye>0) hemen görünür (direkt tahsilat).
            return Results.Redirect($"/kiralar/{id}");
        });

        // ANINDA YENİ MÜŞTERİ (kira formu "Müşteriyi Kaydet" — JS fetch, sayfa yenilenmez → formdaki diğer
        // alanlar kaybolmaz). Aynı OlusturYeniCariAsync yolu (PII şifreleme + TC checksum/benzersizlik
        // CustomerService'te). JSON döner; hata nazik (ok:false) — inline gösterilir, form durumu korunur.
        kira.MapPost("/musteri-olustur", async (CustomerService customers, HttpRequest req) =>
        {
            try
            {
                var id = await OlusturYeniCariAsync(customers, req.Form);
                var ad = ((FormParse.Str(req.Form, "yeniUnvan")
                          ?? $"{FormParse.Str(req.Form, "yeniAd")} {FormParse.Str(req.Form, "yeniSoyad")}").Trim());
                return Results.Json(new { ok = true, id, ad });
            }
            catch (ValidationException ex)
            {
                return Results.Json(new { ok = false, hata = ex.Message });
            }
        });

        // CANLI HESAP (kira formu önizleme paneli; JS fetch). SALT-OKUNUR JSON — persist sıfır; gerçek motor
        // (PricingService/RentalQuoteEngine) tek hesap kaynağı → önizleme == kayıt. GET → antiforgery'ye
        // takılmaz (middleware yalnız unsafe metodları doğrular); RequirePermission grup mirasıyla korunur.
        // ek formatı: "tanimId:miktar,tanimId:miktar".
        kira.MapGet("/hesapla", async (KiraHesapService svc,
            string? vehicleId, DateTimeOffset basTar, DateTimeOffset bitTar,
            string? gunlukUcret, string? fiyatTuru, string? doviz, string? cikisOfisi,
            string? ek, string? rentalId, string? musteriId, string? kampanyaKodu, string? ikinciSurucuId,
            string? donusOfisi, string? dropUcreti) =>
        {
            try
            {
                var sonuc = await svc.HesaplaAsync(new KiraHesapIstek(
                    VehicleId: FormParse.Id(vehicleId),
                    BasTar: basTar, BitTar: bitTar,
                    GunlukUcret: FormParse.Dec(gunlukUcret),
                    FiyatTuru: fiyatTuru, Doviz: doviz, CikisOfisi: cikisOfisi,
                    // Adversarial A5-B5: önizleme == kayıt — segment (müşteri) + kampanya kodu canlı hesapta da.
                    MusteriId: FormParse.Id(musteriId), KampanyaKodu: kampanyaKodu,
                    IkinciSurucuId: FormParse.Id(ikinciSurucuId), // FAZ 3.A3a ek sürücü ücreti önizlemesi
                    DonusOfisi: donusOfisi, DropUcreti: FormParse.Dec(dropUcreti), // FAZ 3.A3b drop önizlemesi
                    EkHizmetler: ParseEkSecim(ek),
                    RentalId: FormParse.Id(rentalId)));
                return Results.Json(sonuc);
            }
            catch (ValidationException ex)
            {
                return Results.Json(new { ok = false, hata = ex.Message }); // örn. şube kapsamı dışı rentalId
            }
            catch (OverflowException)
            {
                // Emniyet kemeri (adversarial PR-B): servis guard'larını aşan uç değer 500 yerine nazik hata.
                return Results.Json(new { ok = false, hata = "Girilen değerler hesaplanamayacak kadar büyük." });
            }
        });

        // MÜSAİT ARAÇ (kira formu Araç sekmesi; JS fetch — SAYFA YENİLENMEZ → operatörün girdiği müşteri/
        // tarih/fiyat/2.sürücü vb. KAYBOLMAZ). Eskiden #musait-form GET ile /kiralar/yeni'ye gidip tüm sayfayı
        // yeniliyordu (yalnız musteriId query'yle korunuyordu). Artık müsait araç listesi JSON döner; JS
        // vehicleId select'ini + dl-kf-arac datalist'ini yerinde günceller. No-JS için GET fallback korunur.
        // GET → antiforgery'ye takılmaz; RequirePermission grup mirasıyla korunur (AvailabilityService yetkisiz).
        kira.MapGet("/musait-arac", async (RentACar.Application.Availability.AvailabilityService availability,
            string? vfrom, string? vto, string? vgrup) =>
        {
            try
            {
                var vf = DateTimeOffset.TryParse(vfrom, out var a) ? new DateTimeOffset(a.Date, TimeSpan.Zero) : (DateTimeOffset?)null;
                var vt = DateTimeOffset.TryParse(vto, out var b) ? new DateTimeOffset(b.Date, TimeSpan.Zero) : (DateTimeOffset?)null;
                if (vf is not { } vff || vt is not { } vtt || vtt <= vff)
                    return Results.Json(new { ok = false, hata = "Geçerli bir müsaitlik aralığı girin (bitiş > başlangıç)." });

                var araclar = await availability.FindAvailableAsync(vff, vtt, string.IsNullOrWhiteSpace(vgrup) ? null : vgrup);
                return Results.Json(new
                {
                    ok = true,
                    sayi = araclar.Count,
                    araclar = araclar.Select(v => new
                    {
                        id = v.Id,
                        // datalist görüntüsü (KiraFormVm.AracGoruntu ile birebir — id-çözümü eşleşmesi bozulmasın)
                        goruntu = Components.Pages.Bookings.KiraFormPaneller.KiraFormVm.AracGoruntu(v),
                        secim = $"{v.Plaka} — {v.Marka} {v.Tip}", // vehicleId select option metni (SekmeArac ile aynı)
                        marka = v.Marka ?? "", tip = v.Tip ?? "", yil = v.ModelYili?.ToString() ?? "",
                        vites = v.Vites?.ToString() ?? "", yakit = v.Yakit.ToString(),
                        grup = v.Grup ?? "", segment = v.Segment ?? "", km = v.Km.ToString(), sube = v.Sube ?? ""
                    })
                });
            }
            catch (ValidationException ex) { return Results.Json(new { ok = false, hata = ex.Message }); }
        });

        // Açık kira alan güncelleme (mega-form "Kaydet" — edit modu). Whitelist RentalUpdateInput tipiyle
        // zorlanır (para/tarih alanları tipte YOK). Redirect'te #sekme fragment'i korunur (tab kaybolmaz).
        kira.MapPost("/update", async (RentalService svc, HttpRequest req, [FromForm] Guid id) =>
        {
            var f = req.Form;
            var sekme = SekmeFragment(FormParse.Str(f, "sekme"));
            try
            {
                var input = new RentalUpdateInput
                {
                    CikisOfisi = FormParse.Str(f, "cikisOfisi"),
                    DonusOfisi = FormParse.Str(f, "donusOfisi"),
                    IkinciSurucuId = FormParse.Id(f["ikinciSurucuId"].ToString()),
                    Aciklama = FormParse.Str(f, "aciklama"),
                    Kaynak = FormParse.Str(f, "kaynak"),
                    KiralamaTuru = FormParse.Str(f, "kiralamaTuru"),
                    DonemselFaturalama = f["donemselFaturalama"].ToString() is "true" or "on", // FAZ 4.2-B4
                    FaturalamaTipi = FormParse.Str(f, "faturalamaTipi"),
                    KmLimit = FormParse.Int(f["kmLimit"].ToString()) ?? 0,
                    FazlaKmUcret = FormParse.Dec(f["fazlaKmUcret"].ToString()) ?? 0m,
                    YakitBirimUcret = FormParse.Dec(f["yakitBirimUcret"].ToString()) ?? 0m,
                    Provizyon = FormParse.Dec(f["provizyon"].ToString()),
                    Depozito = FormParse.Dec(f["depozito"].ToString()),
                    KomisyonOran = FormParse.Dec(f["komisyonOran"].ToString()),
                    KomisyonTutar = FormParse.Dec(f["komisyonTutar"].ToString()),
                    DropUcreti = FormParse.Dec(f["dropUcreti"].ToString()),
                    SonraOdeOran = FormParse.Dec(f["sonraOdeOran"].ToString()),
                    UyariAciklama = FormParse.Str(f, "uyariAciklama"),
                    OzelFaturaAciklama = FormParse.Str(f, "ozelFaturaAciklama"),
                    FaturaListesindeGizle = FormBool(f, "faturaListesindeGizle"),
                    UcusNo = FormParse.Str(f, "ucusNo"),
                    ProvizyonNo = FormParse.Str(f, "provizyonNo"),
                    ProvizyonTarih = FormParse.Date(f["provizyonTarih"].ToString()),
                    OnayKodu = FormParse.Str(f, "onayKodu"),
                    FirmaKodu = FormParse.Str(f, "firmaKodu"),
                    ProjeAdi = FormParse.Str(f, "projeAdi"),
                    OzelKod = FormParse.Str(f, "ozelKod"),
                    OzelKdvOran = FormParse.Dec(f["ozelKdvOran"].ToString()),   // FAZ 1.4 (kesir 0..1)
                    DamgaVergisi = FormParse.Dec(f["damgaVergisi"].ToString()), // FAZ 1.4
                    TalepTuru = FormParse.Str(f, "talepTuru"),
                    GeldigiBirim = FormParse.Str(f, "geldigiBirim"),
                    KefilBilgisi = FormParse.Str(f, "kefilBilgisi"),
                    AssistFirma = FormParse.Str(f, "assistFirma"),
                    OzelSoforBilgisi = FormParse.Str(f, "ozelSoforBilgisi"),
                    EkKosullar = FormParse.Str(f, "ekKosullar"),
                    ManuelFindexPuan = FormParse.Int(f["manuelFindexPuan"].ToString()),
                    OpsiyonNet = FormParse.Dec(f["opsiyonNet"].ToString()),   // FAZ 4.4
                    OpsiyonGun = FormParse.Int(f["opsiyonGun"].ToString()),   // FAZ 4.4
                    KabisCikis = FormBool(f, "kabisCikis"),
                    KabisDonus = FormBool(f, "kabisDonus"),
                    OtomatikUzat = FormBool(f, "otomatikUzat"),
                    AksYedekAnahtarCikis = FormBool(f, "aksYedekAnahtarCikis"),
                    AksYedekAnahtarDonus = FormBool(f, "aksYedekAnahtarDonus"),
                    AksStepneCikis = FormBool(f, "aksStepneCikis"),
                    AksStepneDonus = FormBool(f, "aksStepneDonus"),
                    AksZincirCikis = FormBool(f, "aksZincirCikis"),
                    AksZincirDonus = FormBool(f, "aksZincirDonus"),
                    AksIlkYardimCikis = FormBool(f, "aksIlkYardimCikis"),
                    AksIlkYardimDonus = FormBool(f, "aksIlkYardimDonus"),
                    AksLastikCikis = FormParse.Str(f, "aksLastikCikis"),
                    AksLastikDonus = FormParse.Str(f, "aksLastikDonus")
                };
                var ok = await svc.UpdateOpenAsync(id, input);
                return Results.Redirect(ok
                    ? $"/kiralar/{id}?ok=1{sekme}"
                    : $"/kiralar?hata={Uri.EscapeDataString("Kira bulunamadı.")}");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/kiralar/{id}?hata={Uri.EscapeDataString(ex.Message)}{sekme}");
            }
        });

        kira.MapPost("/cancel", async (RentalService svc, [FromForm] Guid id) =>
        {
            try { await svc.CancelAsync(id); return Results.Redirect("/kiralar"); }
            catch (ValidationException ex) { return Results.Redirect($"/kiralar?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        kira.MapPost("/teslim", async (RentalService svc,
            [FromForm] Guid id, [FromForm] int cikisKm, [FromForm] int cikisYakit) =>
        {
            // #sekme fragment'i: mega-formda işlem sonrası aynı sekme açık kalır (kira = Kira Bilgisi/Teslimat)
            try { await svc.DeliverAsync(id, cikisKm, cikisYakit); return Results.Redirect($"/kiralar/{id}#sekme=kira"); }
            catch (ValidationException ex) { return Results.Redirect($"/kiralar/{id}?hata={Uri.EscapeDataString(ex.Message)}#sekme=kira"); }
        });

        kira.MapPost("/donus", async (RentalService svc,
            [FromForm] Guid id, [FromForm] int donusKm, [FromForm] int donusYakit, [FromForm] DateTimeOffset gercekDonus,
            [FromForm] string? kmHediye, [FromForm] string? bitisSebebi, [FromForm] string? teslimAlanPersonelId) =>
        {
            try
            {
                await svc.ReturnAsync(id, donusKm, donusYakit, gercekDonus,
                    FormParse.Int(kmHediye) ?? 0, bitisSebebi, FormParse.Id(teslimAlanPersonelId)); // servis boş→null normalize eder
                return Results.Redirect($"/kiralar/{id}#sekme=donus");
            }
            catch (ValidationException ex) { return Results.Redirect($"/kiralar/{id}?hata={Uri.EscapeDataString(ex.Message)}#sekme=donus"); }
        });

        // Dönüş CANLI önizlemesi (mega-form Dönüş sekmesi; JS fetch). Salt-okunur JSON — motor ReturnMath;
        // persist yok. Nazik hata sözleşmesi (ok:false) — hesapla ucuyla aynı desen.
        kira.MapGet("/donus-hesapla", async (RentalService svc,
            Guid id, int donusKm, int donusYakit, DateTimeOffset gercekDonus, string? kmHediye) =>
        {
            try
            {
                return Results.Json(await svc.PreviewReturnAsync(id, donusKm, donusYakit, gercekDonus, FormParse.Int(kmHediye) ?? 0));
            }
            catch (ValidationException ex)
            {
                return Results.Json(new { ok = false, hata = ex.Message }); // örn. şube kapsamı dışı
            }
        });

        kira.MapPost("/uzat", async (RentalService svc, [FromForm] Guid id, [FromForm] DateTimeOffset yeniBitTar) =>
        {
            try { await svc.ExtendAsync(id, yeniBitTar); return Results.Redirect($"/kiralar/{id}#sekme=donus"); }
            catch (RentACar.Application.Bookings.AvailabilityConflictException) { return Results.Redirect($"/kiralar/{id}?hata={Uri.EscapeDataString("Uzatılan tarihte araç müsait değil.")}#sekme=donus"); }
            catch (ValidationException ex) { return Results.Redirect($"/kiralar/{id}?hata={Uri.EscapeDataString(ex.Message)}#sekme=donus"); }
        });

        // FAZ 4.1: MANUEL provizyon yaşam döngüsü — POS'suz kayıt (IPosService çağrılmaz; kart verisi
        // sisteme girmez); deftere yazmaz. Yok→Alindi→(Kapandi|IadeEdildi) guard'ları serviste.
        kira.MapPost("/provizyon-al", async (RentalService svc, [FromForm] Guid id) =>
        {
            try { await svc.ProvizyonAlAsync(id); return Results.Redirect($"/kiralar/{id}?ok=1#sekme=ayrintilar"); }
            catch (ValidationException ex) { return Results.Redirect($"/kiralar/{id}?hata={Uri.EscapeDataString(ex.Message)}#sekme=ayrintilar"); }
        });

        kira.MapPost("/provizyon-kapat", async (RentalService svc,
            [FromForm] Guid id, [FromForm] string? kapamaTutar, [FromForm] string? iade) =>
        {
            try
            {
                await svc.ProvizyonKapatAsync(id, FormParse.Dec(kapamaTutar), iade is "true" or "on");
                return Results.Redirect($"/kiralar/{id}?ok=1#sekme=ayrintilar");
            }
            catch (ValidationException ex) { return Results.Redirect($"/kiralar/{id}?hata={Uri.EscapeDataString(ex.Message)}#sekme=ayrintilar"); }
        });

        return app;
    }

    /// <summary>Opsiyonel ödeme-derinlik alanlarını forma göre doldurur (roadmap A2; bilgi amaçlı,
    /// deftere yansımaz). Boş → null (FormParse.Dec).</summary>
    /// <summary>Kira ekranından inline yeni müşteri oluşturur (mevcut cari seçilmediyse). CustomerService PII'ı
    /// şifreler + TC checksum/benzersizlik doğrular. En az Ad (bireysel) ya da Ünvan (kurumsal) gerekir.</summary>
    private static async Task<Guid> OlusturYeniCariAsync(CustomerService customers, IFormCollection form)
    {
        var ad = FormParse.Str(form, "yeniAd");
        var unvan = FormParse.Str(form, "yeniUnvan");
        if (string.IsNullOrWhiteSpace(ad) && string.IsNullOrWhiteSpace(unvan))
            throw new ValidationException("Müşteri seçin ya da yeni müşteri bilgilerini girin (en az Ad veya Ünvan).");
        return await customers.CreateAsync(new CustomerInput
        {
            Tip = string.IsNullOrWhiteSpace(unvan) ? RentACar.Domain.Enums.CariType.Bireysel : RentACar.Domain.Enums.CariType.Kurumsal,
            Ad = ad,
            Soyad = FormParse.Str(form, "yeniSoyad"),
            Unvan = unvan,
            TcKimlik = FormParse.Str(form, "yeniTc"),
            CepTel = FormParse.Str(form, "yeniGsm"),
            Email = FormParse.Str(form, "yeniMail"),
            Il = FormParse.Str(form, "yeniIl"),
            Ilce = FormParse.Str(form, "yeniIlce"),
            // Mega-form ek alanları (sürücü kimliği — CustomerInput'ta zaten vardı; ehliyet PII şifreli saklanır)
            DogumTarihi = FormParse.Date(form["yeniDogum"].ToString()),
            EhliyetNo = FormParse.Str(form, "yeniEhliyetNo"),
            EhliyetSinifi = FormParse.Str(form, "yeniEhliyetSinifi"),
            EhliyetTarihi = FormParse.Date(form["yeniEhliyetTarihi"].ToString()),
            EhliyetYeri = FormParse.Str(form, "yeniEhliyetYeri")
        });
    }

    /// <summary>Mega-form ek hizmet matrisi: ekSecim checkbox'ları (value=tanimId) + ekMiktar_{id} alanları.</summary>
    private static List<(Guid TanimId, decimal Miktar)> ParseEkForm(IFormCollection f)
    {
        var list = new List<(Guid, decimal)>();
        foreach (var s in f["ekSecim"])
        {
            if (!Guid.TryParse(s, out var id)) continue;
            var miktar = FormParse.Dec(f[$"ekMiktar_{id}"].ToString()) ?? 1m;
            list.Add((id, miktar <= 0 ? 1m : miktar));
        }
        return list;
    }

    /// <summary>Kira formu detay alanlarını (bilgi amaçlı; mega-form) BookingInput'a doldurur. Eski/eksik
    /// formlarda alanlar yoktur → null kalır (geriye uyumlu).</summary>
    private static void ApplyKiraDetay(BookingInput input, IFormCollection f)
    {
        input.Kaynak = FormParse.Str(f, "kaynak"); // parite fix: kira create artık kaynağı da taşır
        input.KampanyaKodu = FormParse.Str(f, "kampanyaKodu"); // FAZ 3.A5 (yalnız Otomatik'te geçerli)
        input.UyariAciklama = FormParse.Str(f, "uyariAciklama");
        input.OzelFaturaAciklama = FormParse.Str(f, "ozelFaturaAciklama");
        input.FaturaListesindeGizle = FormBool(f, "faturaListesindeGizle");
        input.UcusNo = FormParse.Str(f, "ucusNo");
        input.ProvizyonNo = FormParse.Str(f, "provizyonNo");
        input.ProvizyonTarih = FormParse.Date(f["provizyonTarih"].ToString());
        input.OnayKodu = FormParse.Str(f, "onayKodu");
        input.FirmaKodu = FormParse.Str(f, "firmaKodu");
        input.ProjeAdi = FormParse.Str(f, "projeAdi");
        input.OzelKod = FormParse.Str(f, "ozelKod");
        input.OzelKdvOran = FormParse.Dec(f["ozelKdvOran"].ToString());   // FAZ 1.4
        input.DamgaVergisi = FormParse.Dec(f["damgaVergisi"].ToString()); // FAZ 1.4
        input.TalepTuru = FormParse.Str(f, "talepTuru");
        input.GeldigiBirim = FormParse.Str(f, "geldigiBirim");
        input.KefilBilgisi = FormParse.Str(f, "kefilBilgisi");
        input.AssistFirma = FormParse.Str(f, "assistFirma");
        input.OzelSoforBilgisi = FormParse.Str(f, "ozelSoforBilgisi");
        input.EkKosullar = FormParse.Str(f, "ekKosullar");
        input.ManuelFindexPuan = FormParse.Int(f["manuelFindexPuan"].ToString());
        input.OpsiyonNet = FormParse.Dec(f["opsiyonNet"].ToString());   // FAZ 4.4
        input.OpsiyonGun = FormParse.Int(f["opsiyonGun"].ToString());   // FAZ 4.4
        input.RiskOnay = FormBool(f, "riskOnay") == true;               // FAZ 4.4 (rol doğrulaması serviste)
        input.KabisCikis = FormBool(f, "kabisCikis");
        input.KabisDonus = FormBool(f, "kabisDonus");
        input.OtomatikUzat = FormBool(f, "otomatikUzat");
        input.AksYedekAnahtarCikis = FormBool(f, "aksYedekAnahtarCikis");
        input.AksStepneCikis = FormBool(f, "aksStepneCikis");
        input.AksZincirCikis = FormBool(f, "aksZincirCikis");
        input.AksIlkYardimCikis = FormBool(f, "aksIlkYardimCikis");
        input.AksLastikCikis = FormParse.Str(f, "aksLastikCikis");
    }

    /// <summary>Canlı hesap "ek" parametresi: "tanimId:miktar,..." — hatalı çift sessiz atlanır (önizleme).</summary>
    private static IReadOnlyList<KiraHesapEkHizmet> ParseEkSecim(string? ek)
    {
        if (string.IsNullOrWhiteSpace(ek)) return [];
        var list = new List<KiraHesapEkHizmet>();
        foreach (var parca in ek.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var i = parca.IndexOf(':');
            if (i <= 0) continue;
            if (Guid.TryParse(parca[..i], out var id) &&
                decimal.TryParse(parca[(i + 1)..], System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var miktar))
                list.Add(new KiraHesapEkHizmet(id, miktar));
        }
        return list;
    }

    /// <summary>Üçlü checkbox: alan formda hiç yok → null (dokunulmadı); hidden-false + checkbox-true çifti
    /// gönderildiyse herhangi biri "true" ise true, aksi false.</summary>
    private static bool? FormBool(IFormCollection f, string key)
    {
        var v = f[key];
        if (v.Count == 0) return null;
        foreach (var s in v)
            if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "on") return true;
        return false;
    }

    /// <summary>Redirect fragment'i: yalnız [a-z0-9-] geçirir (header-injection/karmaşa koruması).</summary>
    private static string SekmeFragment(string? sekme)
    {
        if (string.IsNullOrWhiteSpace(sekme)) return string.Empty;
        var t = sekme.Trim().ToLowerInvariant();
        return t.Length <= 32 && t.All(ch => ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
            ? $"#sekme={t}"
            : string.Empty;
    }

    private static void ApplyOdemeDerinlik(BookingInput input, IFormCollection f)
    {
        input.Provizyon = FormParse.Dec(f["provizyon"].ToString());
        input.Depozito = FormParse.Dec(f["depozito"].ToString());
        input.KomisyonOran = FormParse.Dec(f["komisyonOran"].ToString());
        input.KomisyonTutar = FormParse.Dec(f["komisyonTutar"].ToString());
        input.DropUcreti = FormParse.Dec(f["dropUcreti"].ToString());
        input.SonraOdeOran = FormParse.Dec(f["sonraOdeOran"].ToString());
        // referans sistem parite metadata (additive)
        input.KiralamaTuru = Nz(f["kiralamaTuru"].ToString());
        input.DonemselFaturalama = f["donemselFaturalama"].ToString() is "true" or "on"; // FAZ 4.2-B4
        input.FaturalamaTipi = Nz(f["faturalamaTipi"].ToString());
        input.FiyatTuru = Nz(f["fiyatTuru"].ToString());
        input.Doviz = Nz(f["doviz"].ToString());
    }

    private static string? Nz(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
