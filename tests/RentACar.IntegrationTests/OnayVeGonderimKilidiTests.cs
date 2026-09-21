using System.Text.RegularExpressions;

namespace RentACar.IntegrationTests;

/// <summary>
/// Onay (<c>data-confirm</c>) + çift-gönderim kilidi çiti — <c>wwwroot/js/rc-ui.js</c>.
///
/// <para><b>Neden var (gerçek hata):</b> <c>rc-ui.js</c> yalnız <c>form[data-confirm]</c>'e
/// bakıyordu. Yedi geri-alınamaz işlem ise mesajı GÖNDER BUTONUNA koymuştu — toplu fatura
/// kesimi (<c>InvoiceList</c>), cari bakiye düzeltme (<c>BakiyeDuzeltme</c>), otomatik tahsilat
/// (<c>OtomatikTahsilat</c>), gider ödemesi (<c>ExpenseList</c>) ve ücretli WhatsApp / e-posta /
/// SMS testleri (<c>Ayarlar</c>). Bu yedisi HİÇ sormuyordu; kullanıcı "onay istenir" sanıyordu.
/// Ayrıca hiçbir POST formunda çift-tık koruması yoktu: ikinci tık ikinci bir POST (ikinci fatura
/// denemesi, ikinci tahsilat) gönderiyordu.</para>
///
/// <para><b>Neden kaynak taraması:</b> davranış tarayıcıda çalışır; test projesinde tarayıcı
/// yok. Davranış Playwright ile ölçüldü (senkron kapatma gönderen butonun <c>name/value</c>'sunu
/// gövdeden düşürdü; ertelenmiş kilit düşürmedi; çift tık tek POST; <c>target=_blank</c> ve GET
/// kilitlenmedi; dosya indiren POST 10 sn sonra çözüldü). Bu çit, o ölçümün dayandığı YAPININ
/// sessizce geri alınmasını engeller: bir "sadeleştirme" kilidi olay içine taşırsa ya da
/// emniyet süresini silerse test kırılır.</para>
///
/// <para>Her iddia rc-ui.js'in kendisinden değil, elle yazılmış beklentiden gelir (bağımsız
/// oracle): "buton mesajı formunkinden ÖNCE okunur", "<c>disabled = true</c> yalnız
/// <c>kilitle</c>'de ve <c>kilitle</c> yalnız ertelenmiş geri-çağrıda" gibi.</para>
/// </summary>
public sealed class OnayVeGonderimKilidiTests
{
    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static string RcUi() =>
        File.ReadAllText(Path.Combine(RepoKok(), "src/RentACar.Web/wwwroot/js/rc-ui.js"));

    // ---------------------------------------------------------------- JS blok yardımcıları

    /// <summary>
    /// <paramref name="acilis"/>'teki <c>{</c> ile açılan bloğun kapanış <c>}</c> indeksi.
    /// String/şablon literalleri ve yorumlar atlanır — yorumlardaki kesme işareti ("Blazor'ın")
    /// sayacı bozmasın diye.
    /// </summary>
    private static int BlokSonu(string js, int acilis)
    {
        Assert.Equal('{', js[acilis]);
        var derinlik = 0;
        for (var i = acilis; i < js.Length; i++)
        {
            var c = js[i];
            if (c == '/' && i + 1 < js.Length && js[i + 1] == '/')
            {
                i = js.IndexOf('\n', i);
                if (i < 0) break;
                continue;
            }
            if (c == '/' && i + 1 < js.Length && js[i + 1] == '*')
            {
                i = js.IndexOf("*/", i + 2, StringComparison.Ordinal) + 1;
                if (i <= 0) break;
                continue;
            }
            if (c is '\'' or '"' or '`')
            {
                var tirnak = c;
                for (i++; i < js.Length && js[i] != tirnak; i++)
                    if (js[i] == '\\') i++;
                continue;
            }
            if (c == '{') derinlik++;
            else if (c == '}' && --derinlik == 0) return i;
        }
        throw new InvalidOperationException("rc-ui.js içinde kapanmayan blok (" + acilis + ")");
    }

    /// <summary><paramref name="imza"/>'dan sonraki ilk <c>{</c> bloğunun [baş, son] aralığı.</summary>
    private static (int Bas, int Son) Blok(string js, string imza, int baslangic = 0)
    {
        var i = js.IndexOf(imza, baslangic, StringComparison.Ordinal);
        Assert.True(i >= 0, $"rc-ui.js içinde `{imza}` bulunamadı — yapı değişti mi?");
        var ac = js.IndexOf('{', i);
        return (ac, BlokSonu(js, ac));
    }

    private static (int Bas, int Son) SubmitDinleyicisi(string js)
        => Blok(js, "document.addEventListener('submit'");

    private static string Metin(string js, (int Bas, int Son) b) => js.Substring(b.Bas, b.Son - b.Bas + 1);

    // ---------------------------------------------------------------- 1) onay

    [Fact]
    public void Onay_gonderen_butondaki_data_confirmi_okur_ve_butonun_mesaji_formunkini_ezer()
    {
        var js = RcUi();
        var dinleyici = Metin(js, SubmitDinleyicisi(js));

        // Gönderen buton e.submitter'dan gelir — form="…" ile dışarıdan bağlanan buton dahil.
        var m = Regex.Match(dinleyici, @"(?:var|let|const)\s+(\w+)\s*=\s*e\.submitter\b");
        Assert.True(m.Success,
            "Submit dinleyicisi gönderen butonu e.submitter'dan okumalı. Okumazsa <button data-confirm> " +
            "yine HİÇ sormaz (InvoiceList toplu fatura, BakiyeDuzeltme, OtomatikTahsilat, ExpenseList " +
            "ödeme, Ayarlar ücretli testler).");
        var g = Regex.Escape(m.Groups[1].Value);

        // Sıra ÖNEMLİ: önce buton, sonra form. Tersi, "Kaydet"i de soran bir form-mesajının
        // butonun özgül uyarısını ("fatura silinemez") gölgelemesi demek.
        Assert.Matches(new Regex(
            @"\(\s*" + g + @"\s*&&\s*" + g + @"\.getAttribute\(\s*'data-confirm'\s*\)\s*\)\s*\|\|\s*"
            + @"\w+\.getAttribute\(\s*'data-confirm'\s*\)"), dinleyici);
    }

    [Fact]
    public void Onay_iptalinde_gonderim_durur_ve_KILIT_KURULMAZ()
    {
        var js = RcUi();
        var dinleyici = Metin(js, SubmitDinleyicisi(js));

        var m = Regex.Match(dinleyici, @"!\s*window\.confirm\([^)]*\)\s*\)\s*\{(?<govde>[^}]*)\}");
        Assert.True(m.Success, "Onay window.confirm ile sorulmalı ve iptal dalı ayrı bir blok olmalı.");
        var iptal = m.Groups["govde"].Value;

        Assert.Contains("preventDefault()", iptal, StringComparison.Ordinal);
        // stopPropagation: iptal edilen olay Blazor'ın ve sayfa betiklerinin dinleyicilerine
        // (kabarma aşaması) hiç ulaşmasın.
        Assert.Contains("stopPropagation()", iptal, StringComparison.Ordinal);
        Assert.Contains("return", iptal, StringComparison.Ordinal);
        // İptal eden kullanıcı başka bir şey yapabilmeli — butonlar kapanmamalı.
        Assert.DoesNotContain("kilitle", iptal, StringComparison.Ordinal);
        Assert.DoesNotContain("setTimeout", iptal, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- 2) ertelenmiş kilit

    [Fact]
    public void Kilit_ERTELENIR_butonlar_yalniz_ertelenmis_geri_cagride_kapatilir()
    {
        // Tuzak (a): submit olayının İÇİNDE kapatılan gönderen buton, gövdeden name/value'sunu
        // düşürür (disabled kontrol, olaydan SONRA kurulan alan listesine girmez). Depozito'da
        // olduğu gibi tek formda birden çok gönder butonu olan ekranlarda o alan "hangi işlem"
        // bilgisidir. Tuzak (b): başka bir dinleyici (kabarmada, sonra) preventDefault edebilir;
        // karar ancak dağıtım bittikten sonra verilebilir.
        var js = RcUi();
        var dinleyiciAraligi = SubmitDinleyicisi(js);
        var kilitle = Blok(js, "function kilitle(");

        // 1) Butonu kapatan TEK yer kilitle().
        var kapatmalar = Regex.Matches(js, @"\.disabled\s*=\s*true\b").Select(x => x.Index).ToList();
        Assert.NotEmpty(kapatmalar);
        Assert.All(kapatmalar, i => Assert.True(i > kilitle.Bas && i < kilitle.Son,
            "`.disabled = true` kilitle() dışında bulundu (konum " + i + "). Butonu submit olayında " +
            "senkron kapatmak gönderen butonun name/value'sunu POST gövdesinden düşürür."));
        Assert.DoesNotMatch(new Regex(@"setAttribute\(\s*'disabled'"), js);

        // 2) kilitle() yalnız submit dinleyicisinin içindeki setTimeout geri-çağrısından çağrılır
        //    ve o geri-çağrı ÖNCE e.defaultPrevented'a bakar.
        var cagrilar = Regex.Matches(js, @"(?<!function\s)\bkilitle\(").Select(x => x.Index).ToList();
        Assert.NotEmpty(cagrilar);

        var ertelenmisler = new List<(int Bas, int Son)>();
        for (var i = js.IndexOf("setTimeout(function", dinleyiciAraligi.Bas, StringComparison.Ordinal);
             i >= 0 && i < dinleyiciAraligi.Son;
             i = js.IndexOf("setTimeout(function", i + 1, StringComparison.Ordinal))
            ertelenmisler.Add(Blok(js, "setTimeout(function", i));
        Assert.NotEmpty(ertelenmisler);

        foreach (var c in cagrilar)
        {
            var kapsayan = ertelenmisler.Where(b => c > b.Bas && c < b.Son).ToList();
            Assert.True(kapsayan.Count == 1,
                "kilitle() submit dinleyicisindeki ertelenmiş (setTimeout) geri-çağrının DIŞINDA " +
                "çağrılıyor (konum " + c + "). Kilit olayın içinde kurulursa gönderen butonun değeri düşer " +
                "ve sonradan preventDefault edilen gönderim de kilitlenir.");
            var onu = js.Substring(kapsayan[0].Bas, c - kapsayan[0].Bas);
            Assert.Contains(".defaultPrevented", onu, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Kilit_kurulana_dek_ikinci_submit_yutulur_ve_onay_ikinci_kez_sorulmaz()
    {
        // Tuzak (g): tarayıcı kuyruğa girmiş ikinci tıkı setTimeout(0)'dan ÖNCE işleyebilir
        // (Playwright çift tıkında onay İKİ kez soruldu). Bekleyen olay formda saklanır ve
        // onay sorusundan ÖNCE kontrol edilir.
        var js = RcUi();
        var dinleyici = Metin(js, SubmitDinleyicisi(js));

        var onay = dinleyici.IndexOf("window.confirm(", StringComparison.Ordinal);
        Assert.True(onay > 0, "Onay window.confirm ile sorulmalı.");
        var onaydanOnce = dinleyici[..onay];

        // Onaydan ÖNCE: hem kurulmuş kilit (KILITLI) hem bekleyen-iptal-edilmemiş olay yutulur.
        Assert.Contains("_rcBekleyen", onaydanOnce, StringComparison.Ordinal);
        Assert.Contains(".defaultPrevented", onaydanOnce, StringComparison.Ordinal);
        Assert.Contains("KILITLI", onaydanOnce, StringComparison.Ordinal);
        Assert.Contains("preventDefault()", onaydanOnce, StringComparison.Ordinal);
        Assert.Contains("stopPropagation()", onaydanOnce, StringComparison.Ordinal);

        // Olay, butonlara dokunmadan HEMEN (senkron) saklanır — ertelenmiş geri-çağrıda değil.
        var sakla = Regex.Match(dinleyici, @"_rcBekleyen\s*=\s*e\s*;");
        Assert.True(sakla.Success, "Kilitlenecek gönderimin olayı formda saklanmalı (form._rcBekleyen = e).");
        Assert.True(sakla.Index < dinleyici.IndexOf("setTimeout(", StringComparison.Ordinal),
            "Bekleyen olay setTimeout'tan ÖNCE saklanmalı; aksi halde boşluk yine açık kalır.");
    }

    // ---------------------------------------------------------------- 3) kapsam dışı gönderimler

    [Fact]
    public void Yeni_sekme_GET_ve_muaf_formlar_kilitlenmez()
    {
        var js = RcUi();
        var karar = Metin(js, Blok(js, "function kilitlenirMi("));

        // target=_blank (PDF yazdır): sayfa yerinde kalır, kilit asla kendiliğinden çözülmezdi.
        Assert.Matches(new Regex(@"===\s*'_blank'\s*\)\s*return\s+false"), karar);
        // Gönderen butondaki formtarget/formmethod formunkini ezer (tarayıcı kuralı).
        Assert.Contains("'formtarget'", karar, StringComparison.Ordinal);
        Assert.Contains("'formmethod'", karar, StringComparison.Ordinal);
        // GET süzgeçleri: tekrar gönderimi zararsız, kilit yalnız POST.
        Assert.Matches(new Regex(@"!==\s*'post'\s*\)\s*return\s+false"), karar);
        // Muafiyet hem formda hem gönderen butonda.
        Assert.Equal(2, Regex.Matches(karar, @"getAttribute\(\s*'data-kilit'\s*\)\s*===\s*'yok'\s*\)\s*return\s+false").Count);

        // Karar, ertelemeden ÖNCE verilir: kapsam dışı gönderim bekleyen olarak da işaretlenmez.
        var dinleyici = Metin(js, SubmitDinleyicisi(js));
        var kararYeri = dinleyici.IndexOf("kilitlenirMi(", StringComparison.Ordinal);
        Assert.True(kararYeri >= 0 && kararYeri < dinleyici.IndexOf("setTimeout(", StringComparison.Ordinal));
        Assert.True(kararYeri < dinleyici.IndexOf("_rcBekleyen = e", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- 4) emniyet çözmesi

    [Fact]
    public void Kilit_emniyet_suresiyle_geri_tusuyla_ve_enhancedload_ile_cozulur()
    {
        // Dosya indiren POST sayfayı terk etmez ve JS'ten ayırt edilemez → emniyet süresi yoksa
        // buton sayfa yenilenene dek ÖLÜ kalır. Süre 8–10 sn: kısa olursa yavaş bir POST sırasında
        // yeniden gönderime açılır, uzun olursa indirme sonrası kullanıcı bekler.
        var js = RcUi();
        var sure = Regex.Match(js, @"KILIT_EMNIYET_MS\s*=\s*(\d+)\s*;");
        Assert.True(sure.Success, "KILIT_EMNIYET_MS sabiti bulunamadı.");
        var ms = int.Parse(sure.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(ms, 8000, 10000);

        var kilitle = Metin(js, Blok(js, "function kilitle("));
        Assert.Matches(new Regex(@"setTimeout\(\s*function\s*\(\)\s*\{\s*coz\(\s*form\s*\)\s*;?\s*\}\s*,\s*KILIT_EMNIYET_MS\s*\)"), kilitle);

        // bfcache geri dönüşü ve Blazor enhanced navigation: aynı "hepsini çöz" işlevi.
        var ps = Regex.Match(js, @"window\.addEventListener\(\s*'pageshow'\s*,\s*(\w+)\s*\)");
        Assert.True(ps.Success, "pageshow'da kilitler çözülmeli (bfcache kapalı butonlarla geri getirir).");
        var cozucu = ps.Groups[1].Value;
        Assert.Matches(new Regex(@"Blazor\.addEventListener\(\s*'enhancedload'\s*,\s*" + Regex.Escape(cozucu) + @"\s*\)"), js);
        Assert.Contains("coz", Metin(js, Blok(js, "function " + cozucu + "(")), StringComparison.Ordinal);

        // (f) Çözme yalnız BİZİM kapattıklarımızı açar; sunucunun disabled bastığı buton kapalı kalır.
        var coz = Metin(js, Blok(js, "function coz("));
        Assert.Matches(new Regex(@"if\s*\(\s*!\s*b\.hasAttribute\(\s*KILITLEDI\s*\)\s*\)\s*return"), coz);
    }

    // ---------------------------------------------------------------- 5) razor tarafı

    private static readonly Regex Etiket = new(
        @"<(?<ad>[A-Za-z][\w.-]*)\b(?<oz>(?:[^>""']|""[^""]*""|'[^']*')*)>", RegexOptions.Singleline);

    private static readonly Regex RazorYorum = new(@"@\*.*?\*@", RegexOptions.Singleline);

    /// <summary>
    /// data-confirm taşıyan etiket submit olayı ÜRETEBİLİYOR mu? rc-ui.js onayı submit'te
    /// sorar; <c>type="button"</c> ya da <c>&lt;a&gt;</c> üzerindeki data-confirm hiç sorulmaz —
    /// bu çitin önlediği hatanın ta kendisi (kullanıcı "soracak" sanır, sormaz).
    /// </summary>
    private static bool OnayTasiyabilir(string etiketAdi, string ozellikler)
    {
        var tip = Regex.Match(ozellikler, @"\btype\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
        var t = tip.Success ? tip.Groups[1].Value.Trim().ToLowerInvariant() : null;
        return etiketAdi.ToLowerInvariant() switch
        {
            "form" => true,
            "button" => t is null or "submit",           // <button> varsayılan tipi submit
            "input" => t is "submit" or "image",
            _ => false,
        };
    }

    private static IEnumerable<(string Ad, string Ozellikler)> OnayliEtiketler(string razor)
        => Etiket.Matches(RazorYorum.Replace(razor, ""))
            .Where(m => m.Groups["oz"].Value.Contains("data-confirm", StringComparison.Ordinal))
            .Select(m => (m.Groups["ad"].Value, m.Groups["oz"].Value));

    [Theory]
    [InlineData("<form method=\"post\" data-confirm=\"Silinsin mi?\">", true)]
    [InlineData("<button type=\"submit\" data-confirm=\"Devam?\">", true)]
    [InlineData("<button data-confirm=\"Devam?\">", true)]
    [InlineData("<button class=\"link\" type=\"submit\"\n        data-confirm=\"Ödeme kaydedilecek. Devam?\">", true)]
    [InlineData("<input type=\"submit\" value=\"Gönder\" data-confirm=\"Devam?\" />", true)]
    [InlineData("<button type=\"button\" data-confirm=\"Devam?\">", false)]
    [InlineData("<a class=\"link\" href=\"/x\" data-confirm=\"Devam?\">", false)]
    [InlineData("<input type=\"checkbox\" data-confirm=\"Devam?\" />", false)]
    public void Tarayici_onay_tasiyabilen_etiketi_dogru_ayirir(string razor, bool beklenen)
    {
        // Tarayıcının (aşağıdaki çitin) kendisini elle yazılmış örneklerle doğrular — çok satırlı
        // öznitelik dahil. Aksi halde regex sessizce hiçbir şey eşleştirmeyip çiti yeşil tutabilir.
        var bulunan = OnayliEtiketler(razor).ToList();
        Assert.Single(bulunan);
        Assert.Equal(beklenen, OnayTasiyabilir(bulunan[0].Ad, bulunan[0].Ozellikler));
    }

    [Fact]
    public void Data_confirm_yalniz_form_ya_da_gonder_butonunda_durur()
    {
        var kok = RepoKok();
        var razorlar = Directory.EnumerateFiles(
            Path.Combine(kok, "src/RentACar.Web/Components"), "*.razor", SearchOption.AllDirectories).ToList();

        var toplam = 0;
        var hatali = new List<string>();
        foreach (var f in razorlar)
        {
            foreach (var (ad, oz) in OnayliEtiketler(File.ReadAllText(f)))
            {
                toplam++;
                if (!OnayTasiyabilir(ad, oz))
                    hatali.Add($"{Path.GetRelativePath(kok, f)}: <{ad}{oz.Split('\n')[0]}…>");
            }
        }

        // Tarayıcı çalışıyor mu: bugün 94 form + 7 buton. Sıfır bulmak regex'in bozulduğu demektir.
        Assert.True(toplam > 0, "Hiç data-confirm bulunamadı — tarama bozuk.");
        Assert.True(hatali.Count == 0,
            "data-confirm submit olayı ÜRETMEYEN bir etikette: onay HİÇ sorulmaz. Mesajı <form>'a ya da " +
            "type=\"submit\" butona taşıyın (fetch'le çalışan type=\"button\" için onayı betik sormalı).\n  "
            + string.Join("\n  ", hatali));
    }
}
