using System.Net;
using System.Text.Json;
using RentACar.Domain.Entities;

namespace RentACar.IntegrationTests;

/// <summary>
/// #285 gerileme çiti — #278 tarih aralığı ve negatif tutar kuralları yalnız DEĞİŞEN alana uygulanır. Kurallardan
/// önce kayda girmiş (DB'ye doğrudan yazılmış) 1940 tescilli, negatif ÖTV'li araç, başka bir alanı düzenlenirken
/// kilitlenmez; ama o alan başka bir geçersiz değere çevrilirse reddedilir. Beklenenler elle kurulmuş senaryodan.
/// </summary>
public sealed partial class UiAracTests
{
    [Fact]
    public async Task Legacy_out_of_range_values_do_not_block_unrelated_edit_but_changed_invalid_value_is_400()
    {
        var o = await OrtamKurAsync();
        var admin = await GirisAsync(o, Kim.Admin);
        var legacy = new Vehicle
        {
            Plaka = Plaka("34E"), Marka = "Murat", Tip = "124", Grup = "C", Sube = "SubeA", Km = 1000,
            TescilTarihi = new DateTimeOffset(1940, 6, 15, 0, 0, 0, TimeSpan.Zero), AlisOtv = -5m,
        };
        await VeriYazAsync(o.TenantId, db => db.Vehicles.Add(legacy)); // servis doğrulamasını atlar (eski veri)

        async Task<Dictionary<string, JsonElement>> CardAsync()
            => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                (await Json(await Gonder(admin, HttpMethod.Get, $"{Arac}/{legacy.Id}"))).GetRawText())!;
        static JsonElement J(object v) => JsonSerializer.SerializeToElement(v);

        // 1) Yalnız açıklama değişiyor → 200 (eski tarih ve negatif ÖTV aynen gider).
        var card = await CardAsync();
        card["aciklama"] = J("yalnız not");
        var ok = await Json(await Gonder(admin, HttpMethod.Put, $"{Arac}/{legacy.Id}", card));
        Assert.Equal("yalnız not", ok.GetProperty("aciklama").GetString());
        Assert.Equal(-5m, ok.GetProperty("alisOtv").GetDecimal());

        // 2) Aynı takvim günü, farklı saat (Blazor gün formu) = değişmedi → 200.
        card = await CardAsync();
        card["tescilTarihi"] = J("1940-06-15T12:00:00Z");
        card["aciklama"] = J("ikinci not");
        await Json(await Gonder(admin, HttpMethod.Put, $"{Arac}/{legacy.Id}", card));

        // 3) Alan başka bir geçersiz değere çevrilirse → 400 alanlı.
        card = await CardAsync();
        card["tescilTarihi"] = J("1939-01-01T00:00:00Z");
        await ProblemBekle(await Gonder(admin, HttpMethod.Put, $"{Arac}/{legacy.Id}", card),
            HttpStatusCode.BadRequest, "dogrulama", "tescilTarihi");
        card = await CardAsync();
        card["alisOtv"] = J(-10m);
        await ProblemBekle(await Gonder(admin, HttpMethod.Put, $"{Arac}/{legacy.Id}", card),
            HttpStatusCode.BadRequest, "dogrulama", "alisOtv");

        // 4) Geçerli değere düzeltmek her zaman serbest.
        card = await CardAsync();
        card["tescilTarihi"] = J("1995-03-10T00:00:00Z");
        card["alisOtv"] = J(0m);
        var fixedCard = await Json(await Gonder(admin, HttpMethod.Put, $"{Arac}/{legacy.Id}", card));
        Assert.Equal(0m, fixedCard.GetProperty("alisOtv").GetDecimal());
    }
}
