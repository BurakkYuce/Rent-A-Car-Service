using System.Net;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests;

public sealed partial class UiAracTests
{
    [Fact]
    public async Task Liste_kapsam_siralama_sayfalama_ve_ozet()
    {
        var o = await OrtamKurAsync();
        // Elle kurulmuş filo: A şubesinde 3 araç (km 3000/1000/2000; biri serviste), B şubesinde 1 araç.
        var p1 = Plaka("34A1"); var p2 = Plaka("34A2"); var p3 = Plaka("34A3"); var pb = Plaka("06B1");
        await AracAsync(o, p1, km: 3000);
        await AracAsync(o, p2, km: 1000, durum: VehicleStatus.Serviste);
        await AracAsync(o, p3, km: 2000, marka: "Renault");
        await AracAsync(o, pb, sube: "SubeB", km: 500);

        var admin = await GirisAsync(o, Kim.Admin);
        var tum = await Json(await Gonder(admin, HttpMethod.Get, Arac + "?sirala=-km"));
        Assert.Equal(4, tum.GetProperty("toplam").GetInt32());
        Assert.Equal([p1, p3, p2, pb], Plakalar(tum)); // km azalan: 3000, 2000, 1000, 500

        // Sayfalama: boyut 2, sayfa 2 → km artan 3. ve 4. (2000, 3000).
        var s2 = await Json(await Gonder(admin, HttpMethod.Get, Arac + "?sirala=km&boyut=2&sayfa=2"));
        Assert.Equal([p3, p1], Plakalar(s2));
        Assert.Equal(4, s2.GetProperty("toplam").GetInt32());

        // Süzgeç: marka araması + durum adı.
        Assert.Equal([p3], Plakalar(await Json(await Gonder(admin, HttpMethod.Get, Arac + "?q=renault"))));
        Assert.Equal([p2], Plakalar(await Json(await Gonder(admin, HttpMethod.Get, Arac + "?durum=Serviste"))));

        // Geçersiz sıralama / enum → 400 alan hatası (sessizce "filtre yok"a düşmez).
        await ProblemBekle(await Gonder(admin, HttpMethod.Get, Arac + "?sirala=sifre"), HttpStatusCode.BadRequest, "dogrulama", "sirala");
        await ProblemBekle(await Gonder(admin, HttpMethod.Get, Arac + "?durum=3"), HttpStatusCode.BadRequest, "dogrulama", "durum");

        // Operatör A yalnız A şubesini görür; özet de kapsamlı (3 araç, 2 müsait, 1 serviste → doluluk %33).
        var opA = await GirisAsync(o, Kim.OperatorA);
        var a = await Json(await Gonder(opA, HttpMethod.Get, Arac + "?sirala=plaka"));
        Assert.Equal(3, a.GetProperty("toplam").GetInt32());
        Assert.DoesNotContain(pb, Plakalar(a));
        var ozet = await Json(await Gonder(opA, HttpMethod.Get, Arac + "/ozet"));
        Assert.Equal(3, ozet.GetProperty("toplam").GetInt32());
        Assert.Equal(2, ozet.GetProperty("musait").GetInt32());
        Assert.Equal(1, ozet.GetProperty("serviste").GetInt32());
        Assert.Equal(33, ozet.GetProperty("doluluk").GetInt32());

        // Modele göre grupla: A'da "Fiat Egea" 2, "Renault Egea" 1 → çok olan önce.
        var mg = await Json(await Gonder(opA, HttpMethod.Get, Arac + "/model-gruplari"));
        var gruplar = mg.GetProperty("gruplar").EnumerateArray().ToList();
        Assert.Equal("Fiat Egea", gruplar[0].GetProperty("etiket").GetString());
        Assert.Equal(2, gruplar[0].GetProperty("toplam").GetInt32());

        // Muhasebe (ViewReports) listeyi okuyabilir (Blazor sayfası yalnız [Authorize]).
        var muh = await GirisAsync(o, Kim.Muhasebe);
        Assert.Equal(4, (await Json(await Gonder(muh, HttpMethod.Get, Arac))).GetProperty("toplam").GetInt32());
    }

    [Fact]
    public async Task Detayli_liste_izin_ve_kvkk_ile_durum_panosu()
    {
        var o = await OrtamKurAsync();
        var pa = Plaka("34D"); var pb = Plaka("06D");
        var va = await AracAsync(o, pa, durum: VehicleStatus.Kirada);
        await AracAsync(o, pb, sube: "SubeB");
        // Anonim adlı + anonim telefonlu cari, A aracında AKTİF kirada (oracle: gerçek ad/telefon hiçbir yüzeyde görünmez).
        var anonim = new RentACar.Domain.Entities.Customer
        { Tip = CustomerType.Bireysel, Ad = "Gizli", Soyad = "Kisi", CepTel = "05329998877", AnonimAd = true, AnonimTelefon = true };
        await VeriYazAsync(o.TenantId, db =>
        {
            db.Customers.Add(anonim);
            db.Rentals.Add(new RentACar.Domain.Entities.RentalContract
            {
                SozlesmeNo = "K-F61", VehicleId = va, MusteriId = anonim.Id, Durum = RentalStatus.Kirada,
                BasTar = DateTimeOffset.UtcNow.AddDays(-1), BitTar = DateTimeOffset.UtcNow.AddDays(2),
            });
        });

        var muh = await GirisAsync(o, Kim.Muhasebe);
        var detayli = await Json(await Gonder(muh, HttpMethod.Get, Arac + "/detayli?sirala=plaka"));
        Assert.Equal(2, detayli.GetProperty("toplam").GetInt32());
        var satirA = detayli.GetProperty("kayitlar").EnumerateArray().Single(x => x.GetProperty("plaka").GetString() == pa);
        Assert.Equal("K-F61", satirA.GetProperty("aktifKiraSozlesmeNo").GetString());
        Assert.Equal("Anonim müşteri", satirA.GetProperty("aktifKiraMusteri").GetString());
        Assert.DoesNotContain("Gizli", detayli.ToString());

        var opA = await GirisAsync(o, Kim.OperatorA);
        await ProblemBekle(await Gonder(opA, HttpMethod.Get, Arac + "/detayli"), HttpStatusCode.Forbidden, "yetki_yok");

        // Durum panosu: operatör A yalnız A aracını görür; kira müşterisi anonim, telefon yok.
        var durum = await Json(await Gonder(opA, HttpMethod.Get, Arac + "/durum"));
        var liste = durum.GetProperty("liste");
        Assert.Equal(1, liste.GetProperty("toplam").GetInt32());
        var r = liste.GetProperty("kayitlar")[0];
        Assert.Equal(pa, r.GetProperty("plaka").GetString());
        Assert.Equal("Anonim müşteri", r.GetProperty("musteriAd").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, JsonValueKind(r, "musteriTel"));
        Assert.Equal(1, durum.GetProperty("kirada").GetInt32());
        Assert.DoesNotContain("05329998877", durum.ToString());

        // Muhasebe OperationsWrite'sız → durum panosu 403 (Blazor sayfası izin:OperationsWrite).
        await ProblemBekle(await Gonder(muh, HttpMethod.Get, Arac + "/durum"), HttpStatusCode.Forbidden, "yetki_yok");
    }

    private static System.Text.Json.JsonValueKind JsonValueKind(System.Text.Json.JsonElement e, string alan)
        => e.GetProperty(alan).ValueKind;
}
