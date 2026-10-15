# KABİS — Kiralık Araç Bildirim Sistemi

Araştırma tarihi: 2026-10-15. Bu belge iki şeyi taşır: (1) KABİS yükümlülüğü ve entegrasyon yolu hakkında
**doğrulanmış** bilgi ile **doğrulanamayan** noktalar, (2) kimlik bilgisi gerektirmeyen iç akışın (kuyruk,
yeniden deneme, ekranlar) tasarımı. "Doğrulanamadı" yazan yerde tahmin yoktur; resmî metin bulunamamıştır.

Kaynak sınıfları: **[R]** resmî metin (Resmî Gazete, mevzuat.gov.tr, EGM/JGK sitesi, KVKK kararı) ·
**[İ]** ikincil kaynak (meslek odası duyurusu, hukuk yayını, haber) · **[G]** doğrudan gözlem (adresin kendisi).

---

## 1. Yasal dayanak (kesin)

| Konu | Hüküm | Kaynak |
|---|---|---|
| Kapsam | 1774 s. Kimlik Bildirme Kanunu md. 1: "araç … kiralayan gerçek ve tüzel kişilerin kimliklerinin tespiti ve bildirilmesi" (6638 s. K. md. 18 ile, yürürlük 4/4/2015). | [R] mevzuat.gov.tr 1774 |
| Asıl yükümlülük | Ek md. 3/1 (6638/19 ile eklendi; **7533 s. K. md. 16 ile değişik, yürürlük 30/11/2024**): araç kiralama işletmesinin sorumlu işletmecileri/yöneticileri kiralanan araç bilgileri, kiralayanların kimlik bilgileri ve kira sözleşmesi kayıtlarını bilgisayarda tutar, kolluk incelemesine hazır bulundurur, terminallerini genel kolluk terminallerine bağlar, **"kiralayan şahıs ile kiralanan araç bilgilerini araç teslimi esnasında genel kolluk kuvvetlerine sistem üzerinden anlık olarak"** bildirir; kiralanan araçlarda **GPS** bulundurur ve konum kayıtlarını **3 yıl** saklar. | [R] 1774 Ek md. 3; RG 30.11.2024 (7533) |
| Kamu kurumu kiralayan | "Araç kiralayanın kamu kurum veya kuruluşu olması hâlinde sadece kamu kurum veya kuruluşuyla yapılan sözleşme ile araç bilgileri sisteme kaydedilir." | [R] Ek md. 3/1 |
| Aktif kiralama sorgusu | Ek md. 3/2 (7533 ile eklendi): kolluğun tuttuğu verilerden "şahısların devam eden aktif araç kiralamasının olup olmadığı bilgisi" işletmelerle paylaşılır. | [R] Ek md. 3/2 |
| Yönetmelik | Kimlik Bildirme Kanununun Uygulanması ile İlgili Yönetmelik ek md. 2 (RG 10.06.2015, sayı 29382): kayıtlar "günü gününe" tutulur; veriler **5 yıl** saklanır ve sistemde 5 yıl sonra otomatik silinir. Geçici md. 1: kayıtlar İçişleri Bakanlığının belirleyeceği programda tutulur. | [R] RG 29382 |
| Sistem | EGM duyurusu: KABİS **03.10.2015** itibarıyla hizmette; kullanıcı firmanın sorumlu işleticileri/yöneticileri bilgileri EGM'ye elektronik ortamda anlık bildirir; giriş `arackiralama.egm.gov.tr`. | [R] egm.gov.tr/kabisduyuru |
| KVKK | Kurul kararı 2020/335: KABİS kaydı kanunda açıkça öngörülen yükümlülüktür (KVKK md. 5/2-a ve c); bu kayıt için açık rıza gerekmez, rızayı hizmet şartı yapmak hukuka aykırıdır. | [R] kvkk.gov.tr 2020/335 |
| Trafik cezası | Trafik İdari Para Cezası Karar Tutanaklarının … Yönetmeliği'nde 25.12.2020 (RG 31345) değişikliği: ihlal tarihinde aracın kiralık olduğu KABİS'ten anlaşılırsa tutanak kiracı adına düzenlenir. Yönetmelik metni doğrudan okunmadı. | [İ] haberler, hukuk dergisi makalesi |

**Not:** "KABİS" adı kanunda geçmez; kanun "sistem" ve "genel kolluk kuvvetlerinin bilgisayar terminalleri" der.

## 2. Emniyet / Jandarma

- **Emniyet (EGM):** `arackiralama.egm.gov.tr` [G] — erişim geçidinin arkasında (oturumsuz istekte "Oturum başlatılamadı" sayfası). Giriş kullanıcı kodu + şifre ile; tarihsel olarak **sabit IP** şartı (yalnız tanımlı IP'den erişim) [İ].
- **Jandarma (JGK):** `kabis.jandarma.gov.tr` [G] — "Kiralık Araç Bildirim Sistemi (Yeni)"; giriş **e-Devlet** üzerinden (oturumsuz istek e-Devlet aracılı kimlik doğrulamaya yönlenir). JGK'nin 04.12.2020 tarihli yazısı ve TOBB'un 18.12.2020 yazısıyla **statik IP zorunluluğu olmadan** uzaktan erişim açıldı [İ: ticaret odası duyurusu].
- **Hangi işletme hangisine bildirir:** genel kolluk sorumluluk bölgesine (polis/jandarma) göre ayrıldığı yaygın kabul görüyor; bunu açıkça söyleyen resmî metin **doğrulanamadı**. Bu yüzden kurum firma ayarıdır (Emniyet/Jandarma), sistem kendisi seçmez.
- JGK web servislerinde 04.12.2023'ten beri en az **TLS 1.2** zorunlu [R: jandarma.gov.tr KBS duyurusu] — gerçek adaptör için not.

## 3. Bildirim zamanı

- Kanun iki ifade kullanır: yükümlülükte "araç teslimi **esnasında** … anlık", cezada "araç teslim **edilmeden** … anlık olarak bildirmeyenlere". Yani bildirim **en geç teslim anında**, teslimden önce olmalı [R].
- Bizim karşılığımız: teslim işlemiyle AYNI transaction'da kuyruk kaydı + commit sonrası anında deneme (varsayılan). "Teslimi engelle" ayarı açıksa bildirim teslimden ÖNCE senkron yapılır, başarısızsa teslim reddedilir — kanun metnine birebir uyum isteyen firma için.

## 4. Bildirilen alanlar

- Kanun: "kiralayan şahıs ile kiralanan araç bilgileri", "kiralayanların kimlik bilgileri ve kira sözleşmesi" [R].
- Karşı sistemin **resmî alan listesi / şeması kamuya açık değil — doğrulanamadı.** Ekranda plaka, araç bilgisi, kiralayan kimliği ve kiralama tarihlerinin girildiği ikincil kaynaklarda geçiyor; ehliyet, sürücü, adres, saat alanlarının zorunluluğu **doğrulanamadı**.
- Bizim yükümüz (`KabisKiralamaBildirimi`) sahip olduğumuz her ilgili alanı taşır; gerçek adaptör resmî şemaya eşler:
  sözleşme no (referans), plaka, marka/model, kiralayan (ad, soyad, TC **veya** pasaport, uyruk, doğum tarihi, ehliyet no/sınıf/tarih, telefon, adres), ek sürücü (kayıtlı cari ya da misafir ad/soyad), kurumsal kiracıda ünvan + vergi no (kişi alanı gönderilmez), teslim anı, planlanan dönüş, çıkış/dönüş ofisi.
- Zorunlu veri eksikse (bireyselde ad/soyad, TC ya da pasaport; TC 11 hane değilse; plaka yoksa) karşı sisteme gidilmez, kayıt **Hata** olur ve mesaj operatöre ne yapacağını söyler.

## 5. İade bildirimi

- Kanunda ayrı bir "iade bildirimi" yükümlülüğü **yazmıyor** [R]. Ancak trafik cezası kiracıya ihlal tarihi kiralama aralığındaysa yazılıyor [İ]; erken iadede aralığın kapanması pratikte gerekli. Karşı sistemde kapatma/iade işleminin adı ve zorunluluğu **doğrulanamadı**.
- Bizim karşılığımız: dönüşte **İade** kuyruk kaydı (yalnız "gerekli değil" olmayan bir Kiralama kaydı varsa); İade, Kiralama bildirilmeden gönderilmez. Teslim edilmiş ve bildirilmiş kiranın iptalinde **İptal** kaydı.
- **Uzatma** (planlanan dönüşün değişmesi): kanunda ayrı bildirim yok; karşı sistemde bir "güncelleme" işlemi olup olmadığı **doğrulanamadı**. İç akış uzatmada bildirim üretmez.
- **Kapsam dışı (bu PR):** filo kiralama sözleşmeleri (`FleetRental`, ayrı varlık ve teslim akışı) kuyruğa girmez; aynı yükümlülük onlar için de geçerlidir — ayrı iş.

## 6. Ceza tutarları

| Fiil (Ek md. 3/4) | Kanun metni (7533, 2024) | 2026 (yeniden değerlenmiş) |
|---|---|---|
| Bilgisayarda kayıt tutmama, terminali bağlamama, gerçeğe aykırı kayıt/bilgi, GPS bulundurmama, konum kaydını saklamama | 99.896 TL | 180.429 TL |
| Kayıtları kolluk incelemesine hazır bulundurmama; **kiralayan ve araç bilgisini teslimden önce anlık bildirmeme** | 49.938 TL | 90.195 TL |

- Kanun metni [R]. 2026 tutarları Kabahatler Kanunu md. 17/7 yeniden değerlemesiyle hesaplanmış tablodan [İ: Hukuki Haber "2026 Yılı İdari Para Cezaları", İçişleri genelgesine atıf]; **genelgenin kendisi doğrulanamadı**. Yıl içinde tekrarında son cezanın 2 katı; aynı yıl 4. kez işlenirse işletme ruhsatı iptal; tebliğden itibaren 1 ay içinde ödenir; suçun gizlenmesi amacıyla bilgi yok edilirse ruhsat iptali [R].
- Cezanın her bildirilmeyen kiralama için ayrı "fiil" sayılıp sayılmadığı **doğrulanamadı** (kanun "fiilin tekrarı" der).

## 7. Entegrasyon yöntemi

| Soru | Durum |
|---|---|
| EGM KABİS web servisi (SOAP/REST, WSDL) | Kamuya açık doküman **bulunamadı — doğrulanamadı**. |
| JGK KABİS web servisi | JGK konaklama tesisleri için "KBS Tesis 2 Web Servis Tanım Dokümanı" yayımlıyor [R]; KABİS için eşdeğer doküman **bulunamadı**. |
| Ticari yazılımların "otomatik KABİS bildirimi" | Sektörde sözleşmeden otomatik bildirim, teslim/iade bildirimi ve bildirimlerin arşivlenmesi pazarlanıyor [İ]; bunun resmî bir web servisle mi yapıldığı **doğrulanamadı**. |
| Firma kaydı (EGM) | İl/ilçe emniyet (polis merkezi amirliği / kimlik bildirme bürosu) üzerinden firma formu (Ek-2) ve firma kullanıcısı formu (Ek-3); işletme ruhsatı + vergi levhası; sabit IP; kullanıcı kodu/şifre e-posta ile [İ]. e-Devlet üzerinden doğrudan başvuru yok [İ]. |
| Firma kaydı (JGK) | e-Devlet ile `kabis.jandarma.gov.tr` [G]; başvuru adımları/belge listesi **doğrulanamadı**. |
| Test ortamı | Kamuya açık test/sandbox **doğrulanamadı**. |
| Aktif kiralama sorgusu (Ek md. 3/2) | Kanunda var; teknik yolu **doğrulanamadı**. Port'ta `QueryAsync` yalnız referans sorgusu olarak tanımlı. |

## 8. Gerçek bağlantı için gerekenler (sırasıyla)

1. Firmanın KABİS üyeliği: EGM için emniyet kaydı + kullanıcı kodu/şifre (+ sabit IP gerekiyorsa sunucunun çıkış IP'si), JGK için e-Devlet yetkilisi.
2. **Resmî teknik doküman** (web servis tanımı/WSDL ya da API sözleşmesi) — EGM Bilgi Teknolojileri Dairesi / JGK'dan yazılı talep. Şema gelmeden adaptör yazılmaz.
3. **Test hesabı / test ortamı** — yoksa canlı ortamda kontrollü, tek kayıtla kabul testi ve iptal yolu (yanlış bildirim nasıl geri alınır) yazılı olarak öğrenilmeli.
4. Erişim modeli: sunucu tarafı servis çağrısı mı, kullanıcı oturumu (e-Devlet) mı. e-Devlet oturumu gerektiren bir yol sunucudan otomatikleştirilemez — o durumda iç akış "elle bildirildi" işaretiyle çalışmaya devam eder.
5. Adaptör: `IKabisService` arkasına (`NotifyRentalAsync` / `NotifyReturnAsync` / `CancelAsync` / `QueryAsync`); başarıda karşı sistemin referansı `DisReferans` olur; hata sınıfları `GeciciHata` / `KaliciHata` olarak eşlenir; yük ve hata metni loglanmaz. Kimlikler repo dışında, tenant ayarında şifreli.

## 9. İç akış (bu PR — kimlik bilgisi gerektirmez)

**Ayar** (`Ayarlar` satırına eklemeli kolonlar; yalnız `KabisSettingsService`, ManageUsers): `KabisEtkin` (varsayılan kapalı),
`KabisKurum` (Emniyet/Jandarma), `KabisKullanici`, `KabisSifreEnc` (ISecretProtector; hiçbir yanıtta dönmez, denetimde `***`),
`KabisTeslimiEngelle` (varsayılan kapalı). Kullanıcı/kurum değişince kayıtlı şifre yeniden istenir (sırrın yeni hedefe taşınmaması).
Ekran: `/app/kabis-ayarlari` (Ayarlar > Entegrasyon bağlantısı), "Bağlantıyı sına" dürüst sonuç verir.

**Kayıt** `KabisBildirimleri` (tenant-owned, RLS + FORCE; PII YOK): kira × tür (`Kiralama`/`Iade`/`Iptal`) başına **tek**
satır (benzersiz index → idempotency şemada). Durum: `Bekliyor` · `Bildirildi` · `Hata` · `GerekliDegil`; deneme sayısı,
son hata (maskeli), dış referans, olay anı, son/sonraki deneme, elle müdahale (kim, gerekçe).

**Akış**
- Teslim: kapı açıksa Kiralama kaydı teslimle **aynı transaction'da** yazılır (outbox), commit sonrası bir kez denenir.
  "Teslimi engelle" açıksa bildirim teslimden önce senkron yapılır; `Bildirildi`/`GerekliDegil` değilse teslim reddedilir.
- Dönüş: İade kaydı aynı transaction'da (yalnız Kiralama kaydı varsa). İptal: bildirilmemiş Kiralama `GerekliDegil`,
  bildirilmişse İptal kaydı. İş bu uzlaştırmayı periyodik de yapar (çökmeye dayanıklı).
- Gönderim: satır önce **atomik sahiplenilir** (tek `UPDATE … WHERE`; iki gönderici aynı satırı aynı anda gönderemez),
  yük bellekte kurulur (TC/pasaport/ehliyet şifreli cari alanlarından yalnız bellekte çözülür), sonuç sınıflanır:
  - `Basarili` + referans → `Bildirildi` (referanssız "başarı" kabul edilmez).
  - `BagliDegil` (adaptör/kimlik yok) → `Bekliyor`, deneme **sayılmaz**, 60 dk sonra tekrar bakılır.
  - `GeciciHata` → deneme +1, geri çekilme 1–5–15–30–60–120–240 dk; 8. denemede `Hata`.
  - `KaliciHata` / eksik veri → `Hata` (operatör düzeltip "yeniden dene").
- İş: `KabisRetryJob` 5 dk'da bir; tenant başına ucuz ön kontrol, iş yoksa koşu günlüğüne yazmaz.
- Operatör (OperationsWrite, şube kapsamı): **şimdi yeniden dene**, **gerekli değil** (gerekçe zorunlu, onaylı),
  **portaldan elle bildirildi** (referans isteğe bağlı, onaylı).

**Ekran:** kira formunda KABİS kartı (yalnız otomatik bildirim açıkken): durum rozeti, sunucunun uyarı cümlesi, işlemler.
Panelde "KABİS bildirilemeyen" kutusu (bekleyen/hatalı kira sayısı, şube kapsamlı) → `/app/kabis` listesi.

**PII:** kuyruk tablosu kimlik taşımaz; hata metni bilinen kimlik değerleri + 7+ haneli rakam dizileri maskelenerek
saklanır; yük/kimlik kayıtlarının `ToString()`'i kimlik basmaz; servis log yazmaz.

**Dürüst stub:** adaptör yokken hiçbir kayıt `Bildirildi` olmaz, sahte referans üretilmez (`IntegrationStubTests`,
`KabisBildirimTests`).

## 10. Kaynaklar

- 1774 s. Kimlik Bildirme Kanunu (güncel metin, Ek md. 3): https://www.mevzuat.gov.tr/MevzuatMetin/1.5.1774.pdf
- 7533 s. Kanun (RG 30.11.2024): https://www.resmigazete.gov.tr/eskiler/2024/11/20241130-1.htm
- Yönetmelik değişikliği (RG 10.06.2015, 29382): https://www.resmigazete.gov.tr/eskiler/2015/06/20150610-2.htm
- EGM KABİS duyurusu: https://www.egm.gov.tr/kabisduyuru · giriş: https://arackiralama.egm.gov.tr/
- JGK Kimlik Bildirim Sistemi sayfası (KABİS Yeni bağlantısı, TLS duyurusu): https://www.jandarma.gov.tr/kimlik-bildirim-sistemi · https://kabis.jandarma.gov.tr/
- Statik IP zorunluluğunun kaldırılması (JGK/TOBB yazısı, oda duyurusu): https://didimto.org.tr/kiralik-arac-bildirim-sisteminde-statik-ip-kullanilmasi-zorunlulugunun-kaldirilmasi-hakkinda/
- KVKK Kurul kararı 2020/335: https://www.kvkk.gov.tr/Icerik/6909/2020-335
- 2026 idari para cezaları tablosu: https://www.hukukihaber.net/2026-yili-idari-para-cezalari
- Firma kaydı (Ek-2/Ek-3 formları, sabit IP): https://www.ftso.org.tr/duyuru/kiralik-arac-bildirim-sistemi-hakkinda-onemli-duyuru/1495
- KABİS bildirimi ve trafik cezası (25.12.2020 yönetmelik değişikliği): https://tokkder.org/tokkder-dergi/4816
