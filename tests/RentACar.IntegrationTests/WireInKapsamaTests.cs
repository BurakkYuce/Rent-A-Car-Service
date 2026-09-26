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
/// <para><b>F13.1a:</b> Blazor formları ve form uçları silindi; çit aynı dokuz alanı yeni arayüzün formunda
/// (Angular, JSON alan adı) ve <c>/api/ui/v1</c> ucunun gövde/DTO dosyasında (C# özellik adı) arar.
/// Kapsam bilinçli olarak DAR — yalnız parite taramasında ölçülmüş kalemler.</para>
/// </summary>
public sealed class WireInKapsamaTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(Root(), relativePath));

    private const string Loan = "src/RentACar.Frontend/src/app/features/vehicle-finance/loans/loan-form-model.ts";
    private const string Sale = "src/RentACar.Frontend/src/app/features/finance-documents/vehicle-sales/sale-create-form.ts";
    private const string Baf = "src/RentACar.Frontend/src/app/features/vehicle-finance/allocations/allocation-return-panel.ts";

    public static TheoryData<string, string, string> ExpectedValues() => new()
    {
        // (SPA form dosyası, JSON alan adı, neden önemli)
        { Loan, "vehicleId", "taksit gideri araca atfedilir (AccountRef=VehicleId); yoksa karnede (Atanmamış)" },
        { Loan, "cariId", "FAZ-13 kredi-cari ilişkisi; sorulmazsa liste cari filtresi hep boş sonuç verir" },
        { Loan, "dosyaNo", "FAZ-13 banka dosya referansı; kolon ve arama bu alana dayanır" },
        { Sale, "hedefFiyat", "satış analizi" },
        { Sale, "satisKm", "satış anı km" },
        { Sale, "satisKanali", "satış kanalı kırılımı" },
        { Sale, "devir", "noter/trafik devir notu" },
        { Baf, "donusYakit", "dönüş yakıt seviyesi (0-12)" },
        { Baf, "donusTarihi", "gerçek teslim anı; yoksa kayıt anı yazılır" },
    };

    [Theory]
    [MemberData(nameof(ExpectedValues))]
    public void Form_alani_SORULUYOR(string file, string alan, string reason)
        => Assert.True(Regex.IsMatch(Read(file), $@"\b{Regex.Escape(alan)}\b"),
            $"`{alan}` alanı {file} formunda YOK — {reason}.");

    [Theory]
    [MemberData(nameof(ExpectedValues))]
    public void Ucta_da_OKUNUYOR(string file, string alan, string reason)
    {
        // Formda sorulup uçta okunmaması da aynı sınıf hata: kullanıcı doldurur, veri kaybolur. /api/ui gövdesi
        // camelCase JSON → C# PascalCase özellik.
        var endpoint = file == Loan ? "src/RentACar.Web/Api/AracFinans/AracKrediDtolari.cs"
            : file == Sale ? "src/RentACar.Web/Api/FinansBelge/VehicleSaleUiApi.cs"
            : "src/RentACar.Web/Api/AracFinans/OperasyonDtolari.cs";
        var property = char.ToUpperInvariant(alan[0]) + alan[1..];
        Assert.True(Regex.IsMatch(Read(endpoint), $@"\b{Regex.Escape(property)}\b"),
            $"`{property}` uçta ({endpoint}) okunmuyor — {reason}.");
    }
}
