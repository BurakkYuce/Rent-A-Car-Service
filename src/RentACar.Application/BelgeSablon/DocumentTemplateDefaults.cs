namespace RentACar.Application.BelgeSablon;

/// <summary>
/// Belge şablonu bölümlerinin KODDAKİ varsayılanları — hiç şablon tanımlanmamışsa (veya bölüm boş
/// bırakılmışsa) PDF renderer bunları basar. Değerler, şablon sistemi eklenmeden ÖNCE PdfExportService
/// içinde gömülü olan metinlerin BİREBİR kopyasıdır → şablonsuz çıktı bugünküyle aynı kalır (parite kilidi).
/// Fatura/makbuz başlığı ve footer'lar duruma bağlı hesaplandığından burada yalnız sabit metinler var.
/// </summary>
public static class DocumentTemplateDefaults
{
    public const string ContractTitle = "ARAÇ TESLİM BELGESİ / RENTAL AGREEMENT";

    public const string ContractLegalLeft =
        "By signing, the tenant has inspected the vehicle's damages and is responsible for the new damages.\n" +
        "Kiracı imza etmekle: Aracın hasarlarını incelemiş, yeni oluşacak hasarlardan sorumlu olduğunu kabul eder.";

    public const string ContractLegalRight =
        "Kiracı imza etmekle: Kiralayanın Standart Kiralama Koşullarını ve sözleşmenin arka yüzündeki hususları tam anlamıyla kabul ettiğini beyan eder.\n" +
        "By signing the lessee accepts the Lessor's Standard Lease terms and the points stated on the reverse.";

    public const string InvoiceTitle = "FATURA";
}
