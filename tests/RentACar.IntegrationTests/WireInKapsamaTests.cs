using System.Text.RegularExpressions;

namespace RentACar.IntegrationTests;

/// <summary>
/// "Wire-in" çiti: entity + input modeli + servis hazırken FORMUN o alanı hiç sormaması.
///
/// <para><b>Neden var:</b> 2026-08 parite taraması bu kalıbı üç yerde buldu ve biri kozmetik
/// değildi — <c>AracKrediService</c> taksitin gider bacağını <c>AccountRef = kredi.VehicleId</c>
/// ile yazıyor, yani maliyeti o araca atfediyor; ama formda araç seçici olmadığı için
/// <c>VehicleId</c> hep null kalıyordu ve <b>her araç kredisi taksiti araç karnesinde
/// "(Atanmamış)" tarafında birikiyordu</b>. Servis testi bunu yakalayamaz: servise VehicleId
/// verildiğinde doğru çalışıyor. Kırık olan zincirin form ucuydu.</para>
///
/// <para>Bu test kaynak düzeyinde çalışır: "şu input alanı varsa, şu formda da sorulmalı".
/// Kapsam bilinçli olarak DAR — yalnız parite taramasında ölçülmüş üç kalem. Genel bir
/// "tüm input alanları formda olmalı" kuralı YANLIŞ olurdu (çok alan bilinçli olarak
/// yalnız API/servis yolundan doldurulur).</para>
/// </summary>
public sealed class WireInKapsamaTests
{
    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static string Oku(string gorecelYol) => File.ReadAllText(Path.Combine(Kok(), gorecelYol));

    public static TheoryData<string, string, string> Beklenenler() => new()
    {
        // (form dosyası, form alan adı, neden önemli)
        { "src/RentACar.Web/Components/Pages/AracKredileri/AracKrediList.razor", "vehicleId",
          "taksit gideri araca atfedilir (AccountRef=VehicleId); yoksa karnede (Atanmamış)" },
        { "src/RentACar.Web/Components/Pages/AracKredileri/AracKrediList.razor", "cariId",
          "FAZ-13 kredi-cari ilişkisi; sorulmazsa liste cari filtresi hep boş sonuç verir" },
        { "src/RentACar.Web/Components/Pages/AracKredileri/AracKrediList.razor", "dosyaNo",
          "FAZ-13 banka dosya referansı; kolon ve arama bu alana dayanır" },
        { "src/RentACar.Web/Components/Pages/VehicleSales/VehicleSaleList.razor", "hedefFiyat", "satış analizi" },
        { "src/RentACar.Web/Components/Pages/VehicleSales/VehicleSaleList.razor", "satisKm", "satış anı km" },
        { "src/RentACar.Web/Components/Pages/VehicleSales/VehicleSaleList.razor", "satisKanali", "satış kanalı kırılımı" },
        { "src/RentACar.Web/Components/Pages/VehicleSales/VehicleSaleList.razor", "devir", "noter/trafik devir notu" },
        { "src/RentACar.Web/Components/Pages/Baflar/BafList.razor", "donusYakit", "dönüş yakıt yüzdesi" },
        { "src/RentACar.Web/Components/Pages/Baflar/BafList.razor", "donusTarihi",
          "gerçek teslim anı; yoksa kayıt anı yazılır" },
    };

    [Theory]
    [MemberData(nameof(Beklenenler))]
    public void Form_alani_SORULUYOR(string dosya, string alan, string neden)
        => Assert.True(Regex.IsMatch(Oku(dosya), $@"name=""{Regex.Escape(alan)}"""),
            $"`{alan}` alanı {dosya} formunda YOK — {neden}.");

    [Theory]
    [MemberData(nameof(Beklenenler))]
    public void Ucta_da_OKUNUYOR(string dosya, string alan, string neden)
    {
        // Formda sorulup uçta okunmaması da aynı sınıf hata: kullanıcı doldurur, veri kaybolur.
        var uc = dosya.Contains("AracKredileri") ? "src/RentACar.Web/AracKredileri/AracKrediEndpoints.cs"
               : dosya.Contains("VehicleSales") ? "src/RentACar.Web/VehicleSales/VehicleSaleEndpoints.cs"
               : "src/RentACar.Web/Baflar/BafEndpoints.cs";
        Assert.True(Oku(uc).Contains(alan, StringComparison.Ordinal),
            $"`{alan}` uçta ({uc}) okunmuyor — {neden}.");
    }
}
