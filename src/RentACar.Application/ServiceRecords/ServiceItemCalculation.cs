namespace RentACar.Application.ServiceRecords;

/// <summary>Bir servis kaleminin türetilmiş tutarları (hiçbiri kalıcı DEĞİL — Tutar hariç).</summary>
/// <param name="Brut">Birim fiyat × miktar (indirim öncesi). Bileşen yoksa net'in kendisi.</param>
/// <param name="Net">KDV hariç, indirim sonrası satır tutarı = kalıcı <c>ServiceLine.Tutar</c>.</param>
/// <param name="KdvTutar">Satırın KDV'si = round(Net × KdvOran, 2).</param>
/// <param name="GenelToplam">Net + KdvTutar.</param>
public readonly record struct ServisKalemTutar(decimal Brut, decimal Net, decimal KdvTutar, decimal GenelToplam);

/// <summary>
/// Servis kalemi satır hesabı — SAF fonksiyon (DB/servis bağı yok, testte doğrudan çağrılır).
///
/// <para><b>YUVARLAMA KURALI: SATIR BAZINDA.</b> Hem brüt (birim×miktar) hem KDV her satırda
/// 2 haneye yuvarlanır; belge toplamı yuvarlanmış satırların TOPLAMIDIR. Yuvarlama artığı
/// dolayısıyla SATIRDA kalır ve toplamda görünür: 3 × 33,33 net %20 KDV'de satır KDV'si
/// 6,666 → 6,67 olur, toplam KDV 20,01 çıkar; oysa toplamdan hesaplansa 99,99 × 0,20 = 19,998
/// → 20,00 olurdu. Fark (0,01) bilinçlidir — satır ile belge birbirini tutmalı, çünkü kullanıcı
/// kalemi kalem doğrular. (Testte açıkça yazılıdır.)</para>
///
/// <para><b>KDV NET'E KARIŞMAZ.</b> <c>ServiceRecord.ToplamIscilik</c> = Σ Net'tir ve rücu
/// yansıtmasıyla DEFTERE giden tek sayıdır; KDV'yi net'e katmak yansıtılan tutarı sessizce
/// şişirirdi.</para>
/// </summary>
public static class ServiceItemCalculation
{
    /// <summary>Para yuvarlaması — repodaki diğer para hesaplarıyla aynı: 2 hane, AwayFromZero.</summary>
    public static decimal Round(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Satırın NET tutarı = round(birimFiyat × miktar, 2) − round(indirim, 2).
    /// İndirim TUTARDIR (oran değil). Miktar verilmezse 1 kabul edilir.
    /// </summary>
    public static decimal Net(decimal unitPrice, decimal? quantity = null, decimal? discount = null)
        => Round(unitPrice * (quantity ?? 1m)) - Round(discount ?? 0m);

    /// <summary>
    /// Kalemin gösterim tutarları. <paramref name="net"/> kalıcı <c>ServiceLine.Tutar</c>'dır;
    /// bileşenler yalnız BİLGİ olduğu için brüt hesaplanamıyorsa net'e düşülür (uydurma yapılmaz).
    /// </summary>
    public static ServisKalemTutar Calculate(decimal net, decimal? unitPrice, decimal? quantity, decimal? vatRate)
    {
        var gross = unitPrice is { } bf ? Round(bf * (quantity ?? 1m)) : net;
        var vat = vatRate is { } o ? Round(net * o) : 0m;
        return new ServisKalemTutar(gross, net, vat, net + vat);
    }
}
