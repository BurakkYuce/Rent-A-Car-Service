using System.Net;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests;

public sealed partial class UiAracTests
{
    [Fact]
    public async Task Liste_kapsam_siralama_sayfalama_ve_ozet()
    {
        var o = await SetUpEnvironmentAsync();
        // Elle kurulmuş filo: A şubesinde 3 araç (km 3000/1000/2000; biri serviste), B şubesinde 1 araç.
        var p1 = Plate("34A1"); var p2 = Plate("34A2"); var p3 = Plate("34A3"); var pb = Plate("06B1");
        await VehicleAsync(o, p1, km: 3000);
        await VehicleAsync(o, p2, km: 1000, status: VehicleStatus.Serviste);
        await VehicleAsync(o, p3, km: 2000, brand: "Renault");
        await VehicleAsync(o, pb, branch: "SubeB", km: 500);

        var admin = await LoginAsync(o, Kim.Admin);
        var all = await Json(await Gonder(admin, HttpMethod.Get, SampleVehicle + "?sirala=-km"));
        Assert.Equal(4, all.GetProperty("toplam").GetInt32());
        Assert.Equal([p1, p3, p2, pb], Plates(all)); // km azalan: 3000, 2000, 1000, 500

        // Sayfalama: boyut 2, sayfa 2 → km artan 3. ve 4. (2000, 3000).
        var s2 = await Json(await Gonder(admin, HttpMethod.Get, SampleVehicle + "?sirala=km&boyut=2&sayfa=2"));
        Assert.Equal([p3, p1], Plates(s2));
        Assert.Equal(4, s2.GetProperty("toplam").GetInt32());

        // Süzgeç: marka araması + durum adı.
        Assert.Equal([p3], Plates(await Json(await Gonder(admin, HttpMethod.Get, SampleVehicle + "?q=renault"))));
        Assert.Equal([p2], Plates(await Json(await Gonder(admin, HttpMethod.Get, SampleVehicle + "?durum=Serviste"))));

        // Geçersiz sıralama / enum → 400 alan hatası (sessizce "filtre yok"a düşmez).
        await ExpectProblem(await Gonder(admin, HttpMethod.Get, SampleVehicle + "?sirala=sifre"), HttpStatusCode.BadRequest, "dogrulama", "sirala");
        await ExpectProblem(await Gonder(admin, HttpMethod.Get, SampleVehicle + "?durum=3"), HttpStatusCode.BadRequest, "dogrulama", "durum");

        // Operatör A yalnız A şubesini görür; özet de kapsamlı (3 araç, 2 müsait, 1 serviste → doluluk %33).
        var opA = await LoginAsync(o, Kim.OperatorA);
        var a = await Json(await Gonder(opA, HttpMethod.Get, SampleVehicle + "?sirala=plaka"));
        Assert.Equal(3, a.GetProperty("toplam").GetInt32());
        Assert.DoesNotContain(pb, Plates(a));
        var summary = await Json(await Gonder(opA, HttpMethod.Get, SampleVehicle + "/ozet"));
        Assert.Equal(3, summary.GetProperty("toplam").GetInt32());
        Assert.Equal(2, summary.GetProperty("musait").GetInt32());
        Assert.Equal(1, summary.GetProperty("serviste").GetInt32());
        Assert.Equal(33, summary.GetProperty("doluluk").GetInt32());

        // Modele göre grupla: A'da "Fiat Egea" 2, "Renault Egea" 1 → çok olan önce.
        var mg = await Json(await Gonder(opA, HttpMethod.Get, SampleVehicle + "/model-gruplari"));
        var groups = mg.GetProperty("gruplar").EnumerateArray().ToList();
        Assert.Equal("Fiat Egea", groups[0].GetProperty("etiket").GetString());
        Assert.Equal(2, groups[0].GetProperty("toplam").GetInt32());

        // Muhasebe (ViewReports) listeyi okuyabilir (Blazor sayfası yalnız [Authorize]).
        var acct = await LoginAsync(o, Kim.Muhasebe);
        Assert.Equal(4, (await Json(await Gonder(acct, HttpMethod.Get, SampleVehicle))).GetProperty("toplam").GetInt32());
    }

    [Fact]
    public async Task Detayli_liste_izin_ve_kvkk_ile_durum_panosu()
    {
        var o = await SetUpEnvironmentAsync();
        var pa = Plate("34D"); var pb = Plate("06D");
        var va = await VehicleAsync(o, pa, status: VehicleStatus.Kirada);
        await VehicleAsync(o, pb, branch: "SubeB");
        // Anonim adlı + anonim telefonlu cari, A aracında AKTİF kirada (oracle: gerçek ad/telefon hiçbir yüzeyde görünmez).
        var anonymous = new RentACar.Domain.Entities.Customer
        { Tip = CustomerType.Bireysel, Ad = "Gizli", Soyad = "Kisi", CepTel = "05329998877", AnonimAd = true, AnonimTelefon = true };
        await WriteDataAsync(o.TenantId, db =>
        {
            db.Customers.Add(anonymous);
            db.Rentals.Add(new RentACar.Domain.Entities.RentalContract
            {
                SozlesmeNo = "K-F61", VehicleId = va, MusteriId = anonymous.Id, Durum = RentalStatus.Kirada,
                BasTar = DateTimeOffset.UtcNow.AddDays(-1), BitTar = DateTimeOffset.UtcNow.AddDays(2),
            });
        });

        var acct = await LoginAsync(o, Kim.Muhasebe);
        var detailed = await Json(await Gonder(acct, HttpMethod.Get, SampleVehicle + "/detayli?sirala=plaka"));
        Assert.Equal(2, detailed.GetProperty("toplam").GetInt32());
        var rowA = detailed.GetProperty("kayitlar").EnumerateArray().Single(x => x.GetProperty("plaka").GetString() == pa);
        Assert.Equal("K-F61", rowA.GetProperty("aktifKiraSozlesmeNo").GetString());
        Assert.Equal("Anonim müşteri", rowA.GetProperty("aktifKiraMusteri").GetString());
        Assert.DoesNotContain("Gizli", detailed.ToString());

        var opA = await LoginAsync(o, Kim.OperatorA);
        await ExpectProblem(await Gonder(opA, HttpMethod.Get, SampleVehicle + "/detayli"), HttpStatusCode.Forbidden, "yetki_yok");

        // Durum panosu: operatör A yalnız A aracını görür; kira müşterisi anonim, telefon yok.
        var status = await Json(await Gonder(opA, HttpMethod.Get, SampleVehicle + "/durum"));
        var list = status.GetProperty("liste");
        Assert.Equal(1, list.GetProperty("toplam").GetInt32());
        var r = list.GetProperty("kayitlar")[0];
        Assert.Equal(pa, r.GetProperty("plaka").GetString());
        Assert.Equal("Anonim müşteri", r.GetProperty("musteriAd").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, JsonValueKind(r, "musteriTel"));
        Assert.Equal(1, status.GetProperty("kirada").GetInt32());
        Assert.DoesNotContain("05329998877", status.ToString());

        // Muhasebe OperationsWrite'sız → durum panosu 403 (Blazor sayfası izin:OperationsWrite).
        await ExpectProblem(await Gonder(acct, HttpMethod.Get, SampleVehicle + "/durum"), HttpStatusCode.Forbidden, "yetki_yok");
    }

    private static System.Text.Json.JsonValueKind JsonValueKind(System.Text.Json.JsonElement e, string alan)
        => e.GetProperty(alan).ValueKind;
}
