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
                    Kaynak = kaynak
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
                    Kaynak = kaynak
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

        kira.MapPost("/create", async (RentalService svc, CustomerService customers, HttpRequest req,
            [FromForm] string? musteriId, [FromForm] Guid vehicleId,
            [FromForm] DateTimeOffset basTar, [FromForm] DateTimeOffset bitTar,
            [FromForm] string? gunlukUcret, [FromForm] string? cikisOfisi, [FromForm] string? donusOfisi,
            [FromForm] string? aciklama, [FromForm] string? ikinciSurucuId) =>
        {
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
                var id = await svc.CreateDirectAsync(input);
                // Kira açılınca DETAY ekranına git → oradaki tahsilat formu (Bakiye>0) hemen görünür (direkt tahsilat).
                return Results.Redirect($"/kiralar/{id}");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/kiralar?hata={Uri.EscapeDataString(ex.Message)}");
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
            try { await svc.DeliverAsync(id, cikisKm, cikisYakit); return Results.Redirect($"/kiralar/{id}"); }
            catch (ValidationException ex) { return Results.Redirect($"/kiralar/{id}?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        kira.MapPost("/donus", async (RentalService svc,
            [FromForm] Guid id, [FromForm] int donusKm, [FromForm] int donusYakit, [FromForm] DateTimeOffset gercekDonus,
            [FromForm] string? kmHediye, [FromForm] string? bitisSebebi, [FromForm] string? teslimAlanPersonelId) =>
        {
            try
            {
                await svc.ReturnAsync(id, donusKm, donusYakit, gercekDonus,
                    FormParse.Int(kmHediye) ?? 0, bitisSebebi, FormParse.Id(teslimAlanPersonelId)); // servis boş→null normalize eder
                return Results.Redirect($"/kiralar/{id}");
            }
            catch (ValidationException ex) { return Results.Redirect($"/kiralar/{id}?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        kira.MapPost("/uzat", async (RentalService svc, [FromForm] Guid id, [FromForm] DateTimeOffset yeniBitTar) =>
        {
            try { await svc.ExtendAsync(id, yeniBitTar); return Results.Redirect($"/kiralar/{id}"); }
            catch (RentACar.Application.Bookings.AvailabilityConflictException) { return Results.Redirect($"/kiralar/{id}?hata={Uri.EscapeDataString("Uzatılan tarihte araç müsait değil.")}"); }
            catch (ValidationException ex) { return Results.Redirect($"/kiralar/{id}?hata={Uri.EscapeDataString(ex.Message)}"); }
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
            Ilce = FormParse.Str(form, "yeniIlce")
        });
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
        input.FaturalamaTipi = Nz(f["faturalamaTipi"].ToString());
        input.FiyatTuru = Nz(f["fiyatTuru"].ToString());
        input.Doviz = Nz(f["doviz"].ToString());
    }

    private static string? Nz(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
