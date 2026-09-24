using System.IO.Compression;
using System.Text;
using RentACar.Application.Common;
using RentACar.Web.Api.Sistem;
using RentACar.Web.Import;

namespace RentACar.IntegrationTests;

/// <summary>
/// #308 H1/L1 — içe aktarım ayrıştırma sınırları (Blazor ve SPA aynı <see cref="ImportService.Parse"/>) ve kişisel veri
/// taşımayan hata özeti. Saf testler (DB yok); beklenenler elle kurulmuş dosyalardan.
/// </summary>
public sealed class ImportLimitsTests
{
    private static MemoryStream Text(string s) => new(Encoding.UTF8.GetBytes(s));

    private static ValidationException Refused(Func<object> parse)
    {
        var ex = Assert.Throws<ValidationException>(parse);
        Assert.Equal("dosya", ex.Alan);
        return ex;
    }

    [Fact]
    public void Wide_header_is_refused_before_rows_are_read_and_memory_stays_low()
    {
        // İnceleme probe'unun biçimi: 14 KB'lık dosya = çok geniş başlık + çok sayıda kısa satır (eskiden 412 MB).
        var header = string.Join(",", Enumerable.Range(0, 3_000).Select(i => $"c{i}"));
        var csv = header[..Math.Min(header.Length, 8_000)] + "\n" + string.Concat(Enumerable.Repeat("x\n", 3_000));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var ex = Refused(() => ImportService.Parse(Text(csv), "genis.csv"));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Contains("100 sütun", ex.Message);
        Assert.True(allocated < 20L * 1024 * 1024, $"ayrıştırma {allocated / (1024 * 1024)} MB ayırdı");

        // Başlık satırı karakter sınırı (sütun sayısından bağımsız): tek dev hücre.
        Refused(() => ImportService.Parse(Text(new string('a', 9_000) + "\nx\n"), "uzun.csv"));
    }

    [Fact]
    public void Row_limit_fires_during_parsing_and_missing_values_add_no_entries()
    {
        var rows = new StringBuilder("Plaka;Marka\n");
        for (var i = 0; i < 20_001; i++) rows.Append("34A").Append(i).Append(";\n");
        var ex = Refused(() => ImportService.Parse(Text(rows.ToString()), "cok.csv"));
        Assert.Equal("Tek seferde en çok 20.000 satır aktarılabilir.", ex.Message);

        // Sınırın tam altı geçer; boş Marka ve eksik sütun sözlüğe girmez (satır başına bellek = dolu hücre).
        var ok = ImportService.Parse(Text("Plaka;Marka;Renk\n34ABC1;;\n34ABC2\n"), "az.csv");
        Assert.Equal(2, ok.Count);
        Assert.All(ok, r => Assert.Equal(["plaka"], r.Keys.ToList()));

        // 100 sütunlu başlık + 20.000 boş satır: sınırlar içinde, ama boş hücreler giriş üretmez → bellek küçük.
        var wide = string.Join(",", Enumerable.Range(0, 100).Select(i => $"c{i}")) + "\n"
                   + string.Concat(Enumerable.Repeat(new string(',', 99) + "\n", 20_000));
        var input = Text(wide);
        var retainedBefore = GC.GetTotalMemory(forceFullCollection: true);
        var parsed = ImportService.Parse(input, "bos.csv");
        var retained = GC.GetTotalMemory(forceFullCollection: true) - retainedBefore;
        Assert.All(parsed, r => Assert.Empty(r));
        Assert.True(retained < 16L * 1024 * 1024, $"ayrıştırma sonucu {retained / (1024 * 1024)} MB tutuyor");
        GC.KeepAlive(parsed);
    }

    [Fact]
    public void Input_size_and_xlsx_zip_bomb_are_refused()
    {
        // Girdi 8 MB'ı aşarsa okuma durur (Blazor yolunun dosya sınırı yok; SPA ucu ayrıca 5 MB).
        var huge = new MemoryStream(new byte[9 * 1024 * 1024]);
        Assert.Contains("8 MB", Refused(() => ImportService.Parse(huge, "buyuk.csv")).Message);

        // xlsx = ZIP: 70 MB sıfırdan oluşan tek girdi (~70 KB sıkıştırılmış) yüklemeden önce reddedilir.
        var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.SmallestSize).Open();
            var zeros = new byte[1024 * 1024];
            for (var i = 0; i < 70; i++) entry.Write(zeros);
        }
        zip.Position = 0;
        Assert.True(zip.Length < 1024 * 1024);
        Assert.Contains("açılmış boyutu", Refused(() => ImportService.Parse(zip, "bomba.xlsx")).Message);

        // ZIP olmayan .xlsx → "okunamadı" (500 değil).
        Refused(() => ImportService.Parse(Text("düz metin"), "sahte.xlsx"));
    }

    [Fact]
    public void Error_summary_uses_messages_only_and_counts_add_up()
    {
        // Etiket ": " içerse bile (ör. "Ali Veli: 05321112233") yanıta girmez; 25 farklı mesaj → 20 + "Diğer hatalar".
        var errors = new List<ImportError> { new("Ali Veli: 05321112233", "E-posta adresi geçersiz."), new("Ayşe", "E-posta adresi geçersiz.") };
        for (var i = 0; i < 24; i++) errors.Add(new ImportError($"Kişi {i}: 0532000{i:D4}", $"Mesaj {i:D2}"));
        var result = new ImportResult(5, 1, errors.Count, []) { Errors = errors };

        var summary = SystemAdminApi.ImportSummary(result);
        var text = System.Text.Json.JsonSerializer.Serialize(summary);
        Assert.DoesNotContain("0532", text);
        Assert.DoesNotContain("Ali Veli", text);
        Assert.DoesNotContain("Kişi", text);
        Assert.Equal(21, summary.HataOzeti.Count);
        Assert.Equal(new ImportErrorSummaryDto("E-posta adresi geçersiz.", 2), summary.HataOzeti[0]);
        Assert.Equal(new ImportErrorSummaryDto("Diğer hatalar", 5), summary.HataOzeti[^1]);
        Assert.Equal(26, summary.HataOzeti.Sum(x => x.Adet));
        Assert.Equal(summary.Hatali, summary.HataOzeti.Sum(x => x.Adet));
    }
}
