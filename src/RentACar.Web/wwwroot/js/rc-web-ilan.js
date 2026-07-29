// PR-13.1 — İlan teknik özellik satırları: satır ekle / sil (CSP script-src 'self' uyumlu,
// inline on* handler YOK — rc-ui.js deseni: data-* öznitelik + delegasyon).
//
// İLERİCİ ZENGİNLEŞTİRME (progressive enhancement): bu dosya YÜKLENMESE de ekran çalışır.
// JS'siz yol sunucu tarafındadır — "+ Özellik ekle" bir <a> linkidir ve sayfayı `?bos=N+1` ile
// yeniden yükleyip bir boş satır daha basar; silme ise satırı boşaltmakla olur (sunucu boş
// etiket/değer satırlarını zaten ayıklar). JS varsa ikisi de sayfa yenilemeden yapılır.
//
// Sunucu sözleşmesi (endpoint bunu bekler):
//   name="etiket" / name="deger"  → PARALEL diziler, satır sırasıyla eşlenir
//   name="gorunur" value="<index>" → hangi satırların görünür olduğu
// Satır silinince/eklenince `gorunur` değerleri YENİDEN NUMARALANMALIDIR; aksi halde silinen
// satırdan sonrakilerin görünürlüğü kayar. Bu dosyanın asıl işi budur.
(function () {
    var kok = document.querySelector('[data-ozellik-tablo]');
    if (!kok) return;

    var govde = kok.querySelector('tbody');
    var ekleBtn = document.querySelector('[data-ozellik-ekle]');
    var maxSatir = parseInt(kok.getAttribute('data-max-satir') || '30', 10);
    if (!govde) return;

    // JS varken sunucu-taraflı "+" linki gereksiz: onu butona çevir (href'i etkisizleştir).
    if (ekleBtn && ekleBtn.tagName === 'A') {
        ekleBtn.setAttribute('role', 'button');
        ekleBtn.removeAttribute('href');
        ekleBtn.style.cursor = 'pointer';
    }

    function satirlar() {
        return Array.prototype.slice.call(govde.querySelectorAll('tr'));
    }

    // `gorunur` checkbox'larının value'sunu satır sırasına göre yeniden numaralar.
    function numaralandir() {
        satirlar().forEach(function (tr, i) {
            var cb = tr.querySelector('input[name="gorunur"]');
            if (cb) cb.value = String(i);
        });
        if (ekleBtn) ekleBtn.hidden = satirlar().length >= maxSatir;
    }

    function satirYap() {
        var tr = document.createElement('tr');
        tr.innerHTML =
            '<td><input type="checkbox" name="gorunur" value="0" checked /></td>' +
            '<td><input name="etiket" placeholder="Özellik adı" maxlength="60" /></td>' +
            '<td><input name="deger" placeholder="Değer" maxlength="160" /></td>' +
            '<td class="row-actions"><button type="button" class="link danger" data-ozellik-sil ' +
            'title="Satırı sil" aria-label="Satırı sil">✕</button></td>';
        return tr;
    }

    // Ekle
    if (ekleBtn) {
        ekleBtn.addEventListener('click', function (e) {
            e.preventDefault();
            if (satirlar().length >= maxSatir) return;
            var tr = satirYap();
            govde.appendChild(tr);
            numaralandir();
            var ilk = tr.querySelector('input[name="etiket"]');
            if (ilk) ilk.focus();
        });
    }

    // Sil — delegasyon (sonradan eklenen satırlar da kapsanır).
    govde.addEventListener('click', function (e) {
        var btn = e.target.closest ? e.target.closest('[data-ozellik-sil]') : null;
        if (!btn) return;
        e.preventDefault();
        var tr = btn.closest('tr');
        if (!tr) return;
        // Son satır silinmesin — form tamamen boş kalmasın, kullanıcı yazacak yer bulsun.
        if (satirlar().length <= 1) {
            tr.querySelectorAll('input[name="etiket"], input[name="deger"]').forEach(function (i) { i.value = ''; });
            return;
        }
        tr.remove();
        numaralandir();
    });

    numaralandir();
})();
