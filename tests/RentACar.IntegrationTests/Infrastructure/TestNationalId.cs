namespace RentACar.IntegrationTests.Infrastructure;

/// <summary>Geçerli (algoritmaya uyan) rastgele TC kimlik numarası — testlerde sabit kişisel veri olmasın.</summary>
public static class TestNationalId
{
    public static string New()
    {
        var d = new int[11];
        d[0] = Random.Shared.Next(1, 10);
        for (var i = 1; i < 9; i++) d[i] = Random.Shared.Next(0, 10);
        d[9] = (((d[0] + d[2] + d[4] + d[6] + d[8]) * 7 - (d[1] + d[3] + d[5] + d[7])) % 10 + 10) % 10;
        d[10] = d.Take(10).Sum() % 10;
        return string.Concat(d);
    }
}
