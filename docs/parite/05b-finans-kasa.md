# Modül 05b — Finans / Kasa (30 ekran)

**Özet dağılım:**
- `PARA — OPUS'A DEVİR`: 8 (bakiye_islem, banka_islemleri, banka_virman, cari_virman, cezalar, gider_islemleri, kasa_virman, nakit_islem)
- `🩹 CANLI BOZUK`: 3 (bakiye_islem_ara, banka_para_islem, dis_banka_entegrasyon_listesi)
- `⚠ ERİŞİLEMEZ`: 5 (banka_hesap_hareketleri, banka_islem_ara, banka_nakit_listesi, banka_para_listesi, kasa_dagilimi)
- `❌ YOK`: 12 (banka_virman_islem_ara, cari_virman_islem_ara, ceza_gecis_listesi, ceza_listesi, extre_ozeti, genel_borc_alacak, gider_ara, hesap_extresi, hgs_gecis_listesi, kredi_takip_listesi, nakit_islem_ara, tahsilat_raporu)
- `🟡 KISMİ`: 2 (gider_tanimlama, kasa_tanimi)
- `✅ TAM`: 0

**Genel gözlem (metodoloji notu):** Bizim tarafta kasa/banka işlem formları (Kasa Hub, CariVirman, CustomerStatement Tahsilat/Ödeme) canlının çok-alanlı ("Kasa Kodu", "Hesap No" bazlı çoklu hesap seçimi, Makbuz No, İşlem Şube, İşlem Yapan, ayrı Kaynak/Hedef kur-döviz çiftleri) formlarına göre yalnızca **tek Kasa + tek Banka** kovası üzerinden çalışıyor (bkz. `FinancialAccount`/`Bank` tanım tabloları var ama `CashService.TransferAsync` bunları KULLANMIYOR — `LedgerAccountType.Kasa/Banka` sabit iki-değerli enum). Ayrıca liste/rapor ekranlarının büyük kısmında (gider_ara, ceza_listesi, genel_borc_alacak, hesap_extresi, kredi_takip_listesi, nakit_islem_ara vb.) canlıdaki zengin filtre panelleri (Tarih1/Tarih2, Cari Ara, Şube, Döviz…) bizde YOK — sayfalar filtresiz düz liste. Bu, K2 (alan örtüşmesi) hesaplarını sistematik olarak düşürüyor; altta madde madde işaretlendi.

---

## bakiye_islem.aspx — Cari Alacaklandırma
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** — (gerçek karşılık yok; en yakın kavramsal komşular `/cari-virman` (`Finance/CariVirman.razor`) ve `/cariler/{Id}/ekstre` (`Customers/CustomerStatement.razor`) ama farklı iş)
- **kanit:** — · K2=%0 (0/12) · K3=n/a (tip=diğer, kolon yok)
- **Canlı fazlası:** Tüm alanlar: Musteri_No/Ad/Soyad (cari), Tarih, **Vade**, Tutar, Doviz, Makbuz_No, Kur, Islem_Sube, Islem_Yapan, Aciklama. `Islem_Turu=1|2` parametresi (Alacaklandır/Borçlandır) ile TEK cari üzerinde nakit hareketi OLMAKSIZIN bakiye ayarlaması yapıyor.
- **Bizde fazlası:** —
- **Not:** Bizde tahsilat/ödeme (`/finans/tahsilat`, `/finans/odeme`) HER ZAMAN Kasa/Banka hesabına karşı post ediyor; cari-virman da HER ZAMAN iki cari arasında. Tek-cari, nakitsiz manuel bakiye düzeltmesi (goodwill/muhasebe düzeltmesi) yolu yok. Para hattı → Opus.

## bakiye_islem_ara.aspx — Alacaklandırma Listesi
- **Durum:** 🩹 CANLI BOZUK
- **Bizde:** — (canlı yetki reddine düşüyor, profil boş)
- **kanit:** — · K2=n/a · K3=n/a
- **Canlı fazlası:** n/a (canlı erişilemedi)
- **Bizde fazlası:** —
- **Not:** Talimatta belirtildiği gibi canlı bu ekranda yetki reddine düşüyor; "bizde yok" değerlendirmesi yapılmadı.

## banka_hesap_hareketleri.aspx — Banka Hesap Hareketleri
- **Durum:** ⚠ ERİŞİLEMEZ
- **Bizde:** `/raporlar/kasa-banka` (`Reports/KasaBankaDefteri.razor`) — envanterde `menude=hayir`, koda göre başka hiçbir sayfadan link yok (yalnız kendi `@page` yönlendirmesi)
- **kanit:** /raporlar/kasa-banka · K2=%40 (2/5: Tarih1≈from, Tarih2≈to eşleşti; Hesap_No[spesifik IBAN] ve Islem_Sube, Devir_Goster eşleşmedi) · K3=%44 (4/9: Tarih, Borç, Alacak, Açıklama)
- **Canlı fazlası:** Hesap_No (spesifik IBAN seçimi — bizde sadece "Kasa/Banka" tip seçimi var, banka hesabı bazında ayrım yok), Cari Bilgi kolonu, Hesap No kolonu, Döviz kolonu, İşlem Türü, Şube, Devir_Goster (açılış bakiyesi gösterme anahtarı).
- **Bizde fazlası:** Bakiye (yürüyen bakiye) kolonu canlıda yok.
- **Not:** Menüde olmayan sayfa en iyi eşleşme adayı; erişilebilir olsaydı K2/K3 yine kısmi kalırdı. Reachable alternatif `/kasa` (KasaHub) var ama o da filtresiz ve daha az kolonlu.

## banka_islem_ara.aspx — K.Kartı Tahsilat Listesi
- **Durum:** ⚠ ERİŞİLEMEZ
- **Bizde:** `/raporlar/kasa-banka` (`Reports/KasaBankaDefteri.razor`) — `menude=hayir`, başka giriş yolu yok
- **kanit:** /raporlar/kasa-banka · K2=%29 (2/7: Tarih1≈from, Tarih2≈to; Hesap_No, Islem_Sube, Fatura_Listesi, TextBox1 Cari Ara, Sanal_Pos_Deger eşleşmedi) · K3=%11 (3/28: Tarih, Tutar≈Giriş/Çıkış, Açıklama)
- **Canlı fazlası:** Cari Bilgi/Kod, TC Kimlik, Vergi No, Banka, Cari Tutar/Döviz, Makbuz No, İşlem Türü, Sanal Pos İşlem No, İşlem Şube/Yapan, İade Toplam, Kaynak, Özel Kod, İlişkili Kira/Fatura No, Kira Durum, Plaka, Araç Sahibi — kredi kartı/sanal-POS altyapısı bizde tamamen yok (bkz. banka_islemleri.aspx notu).
- **Bizde fazlası:** —
- **Not:** Reachable alternatif `/kasa` filtresiz son-50-işlem listesi (Tip=Tahsilat/Ödeme ayrımı var ama kart/POS detayı yok).

## banka_islemleri.aspx — K.Kartı Tahsilatı
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `/finans/tahsilat` (form, `Customers/CustomerStatement.razor` üzerinden erişilir; `Finance/FinanceEndpoints.cs`) — en yakın ama kredi kartı özel değil
- **kanit:** /cariler/{Id}/ekstre (Tahsilat formu) · K2=%15 (4/27: Tutar, Cari_Doviz≈Döviz, Cari_Kur≈Kur, Aciklama) · K3=n/a (tip=form)
- **Canlı fazlası:** Kart_No, Kart_Vade, Kart_CVV, Kart_Turu, Finans_Kaydet (kart sakla), Kart_Sahibi, Taksit_Adet, Kart_Provizyon, Cekim_Turu (3D/3Dsiz), Sanal_Pos_Deger, Odemelink (ödeme linki gönder), Makbuz_No, Hesap_No (spesifik banka), Banka_Kur/Cari_Kur ayrı çift kur, Islem_Sube, Islem_Yapan, Ozel_Kod.
- **Bizde fazlası:** —
- **Not:** Kredi kartı/sanal POS altyapısı (grep: `kredi kart`, `Pos`, `SanalPos` — Web/Application içinde YOK) sıfırdan; canlıdaki 4 alt-akış (`Islem_Turu=1..4`: Depozit Kapama, Gelen/Giden Havale, Kredi Kartı Tahsilatı/İade, Provizyon) bizde tek genel "Banka" hesabı üzerinden tahsilat/ödemeye indirgeniyor. Para hattı → Opus.

## banka_nakit_listesi.aspx — Para Yatırma Listesi
- **Durum:** ⚠ ERİŞİLEMEZ
- **Bizde:** `/raporlar/kasa-banka` (`Reports/KasaBankaDefteri.razor`) — `menude=hayir`, başka giriş yolu yok
- **kanit:** /raporlar/kasa-banka · K2=%50 (2.5/5: Tarih1≈from, Tarih2≈to, Hesap_No≈Hesap[kısmi — sadece tip seviyesinde]) · K3=❓ DOĞRULANAMADI (canlı kolon kaynağı `yok`)
- **Canlı fazlası:** Hesap_No (spesifik IBAN — bizde yalnız Kasa/Banka tipi), Islem_Sube.
- **Bizde fazlası:** —
- **Not:** Canlı profilinde kolon listesi çıkarılamamış (⚠ GÜVENSİZ), kolon karşılaştırması yapılmadı (kural 4).

## banka_para_islem.aspx — Para Giriş(Serbest)
- **Durum:** 🩹 CANLI BOZUK
- **Bizde:** — (canlı Runtime Error / HTTP 500, profil boş)
- **kanit:** — · K2=n/a · K3=n/a
- **Canlı fazlası:** n/a (canlı erişilemedi)
- **Bizde fazlası:** —
- **Not:** Talimatta belirtilen canlı-bozuk ekran; "bizde yok" değerlendirmesi yapılmadı.

## banka_para_listesi.aspx — Para Giriş Listesi
- **Durum:** ⚠ ERİŞİLEMEZ
- **Bizde:** `/raporlar/kasa-banka` (`Reports/KasaBankaDefteri.razor`) — `menude=hayir`, başka giriş yolu yok
- **kanit:** /raporlar/kasa-banka · K2=%50 (2.5/5: Tarih1≈from, Tarih2≈to, Hesap_No≈Hesap[kısmi]) · K3=❓ DOĞRULANAMADI (canlı kolon kaynağı `yok`)
- **Canlı fazlası:** Hesap_No (spesifik IBAN), Islem_Sube.
- **Bizde fazlası:** —
- **Not:** Companion create-ekranı (banka_para_islem.aspx) canlıda 500 veriyor; bu listenin canlı verisi de muhtemelen zayıf/test amaçlı.

## banka_virman.aspx — Banka Virman
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `/kasa` (Virman formu, `Finance/KasaHub.razor` + `POST /finans/virman`)
- **kanit:** /kasa · K2=%12 (2/17: Tutar, Aciklama) · K3=n/a (tip=diğer, kolon yok)
- **Canlı fazlası:** K_Hesap_No/K_Banka/K_Sube (kaynak spesifik IBAN+banka+şube), H_Hesap_No/H_Banka/H_Sube (hedef aynı), K_Banka_Kuru/H_Banka_Kuru ayrı çift kur, K_Banka_Doviz/H_Banka_Doviz, Makbuz_No, Islemi_Yapan, Islem_Sube.
- **Bizde fazlası:** —
- **Not:** **Yapısal kısıt**: `CashService.TransferAsync` `if (kaynak == hedef) throw ...` ile Banka→Banka (iki farklı banka hesabı arası) virmanı YAPISAL OLARAK ENGELLİYOR — sistemde "Banka" tek bir kova (enum değeri), IBAN bazlı çoklu banka hesabı yok. Canlının asıl işlevi (iki farklı IBAN arası transfer) bizde mümkün değil. Para hattı → Opus.

## banka_virman_islem_ara.aspx — Banka Virman Listesi
- **Durum:** ❌ YOK
- **Bizde:** — (yakın adayı yok; `/kasa` son-işlemler listesi virman kayıtlarını göstermiyor)
- **kanit:** — · K2=%0 (0/2: Tarih_Listesi, Tarih1/Tarih2 filtresi) · K3=%0 (0/7)
- **Canlı fazlası:** Tüm liste: Kayit No, Tarih, Kaynak/Hedef Hesap No, Kaynak Tutar, Kaynak/Hedef Döviz + filtreler.
- **Bizde fazlası:** —
- **Not:** Kod incelemesi: `TransferAsync` (Kasa↔Banka virman) **CashTransaction kaydı OLUŞTURMUYOR** — sadece iki `AccountLedgerEntry` postluyor (`AccountRef=null`). `/kasa` sayfasının "Son İşlemler" tablosu `CashService.ListAsync()` (CashTransaction tablosu) okuyor → virman işlemleri bu listede GÖRÜNMÜYOR. Geçmiş virman işlemlerini görüntüleyen hiçbir ekran yok — gerçek bir boşluk.

## cari_virman.aspx — Cari Virman
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `/cari-virman` (`Finance/CariVirman.razor`, `POST /finans/cari-virman`)
- **kanit:** /cari-virman · K2=%60 (6/10: kaynakCariId, hedefCariId, Tutar, Doviz, Kur, Aciklama) · K3=n/a (tip=diğer, kolon yok)
- **Canlı fazlası:** Tarih (manuel tarih girişi yok, sunucu "şimdi" kullanıyor), Vade, Makbuz_No, Islem_Sube, Islem_Yapan.
- **Bizde fazlası:** —
- **Not:** Bu ekran bizim tarafta en iyi eşleşen kasa/banka-dışı para ekranı (alan örtüşmesi %60). Para hattı → Opus (kur/dengeli-kayıt doğruluğu).

## cari_virman_islem_ara.aspx — Cari Virman Listesi
- **Durum:** ❌ YOK
- **Bizde:** — (bileşen parçaları var ama tek liste yok)
- **kanit:** — · K2=%0 (0/4: TextBox1 Cari Bilgi, Tarih_Listesi, Tarih1, Tarih2) · K3=n/a (canlı kolon kaynağı `yok`)
- **Canlı fazlası:** Tüm filtreli liste (tüm cariler arası virman geçmişi bir arada).
- **Bizde fazlası:** —
- **Not:** `TransferBetweenCariAsync` `SourceType="CariVirman"` ile ledger'a yazıyor → her iki cari'nin KENDİ ekstresinde (`/cariler/{Id}/ekstre`) tek tek görünüyor, ama TÜM cariler arası birleşik "virman listesi" ekranı yok. Veri var, birleşik görünüm yok.

## ceza_gecis_listesi.aspx — Trafik Ceza Listesi
- **Durum:** ❌ YOK
- **Bizde:** — (`/cezalar` farklı iş yapıyor)
- **kanit:** — · K2=%0 (0/9: Tarih1/Bas_Saat/Tarih2/Bit_Saat, Plaka, Sadece_Rapor, TC_Kimlik, Sifre, Firma_Index) · K3=%23 (3/13: Plaka≈Araç, Tutar, kayıt no benzeri)
- **Canlı fazlası:** TC_Kimlik+Sifre (e-Devlet'ten otomatik ceza ÇEKME entegrasyonu — kimlik/credential gerektirir), Ceza Saati, Ceza Maddesi, Ceza Yeri, Şehir/İlçe, Ekleme Zamanı.
- **Bizde fazlası:** —
- **Not:** Bu canlı ekran esasen bir **e-Devlet scraping entegrasyonu** (TC/şifre ile hükümet portalından ceza çekme) — CLAUDE.md §9'daki "credential gerektiren entegrasyonlar" ertelenmiş backloğuyla örtüşüyor, basit bir liste ekranı değil.

## ceza_listesi.aspx — Ceza & Geçiş Listesi
- **Durum:** ❌ YOK
- **Bizde:** `/cezalar` (`Penalties/PenaltyList.razor`) var ama filtresiz + çok daha az kolonlu
- **kanit:** /cezalar · K2=%0 (0/11: Musteri_No, Ad_Soyad, Makbuz_No, Plaka, Arac, Tarih_Listesi, Tarih1, Tarih2, Ceza_Durum, Odeme_Durum, Ofis) · K3=%28 (8/29: No, Tür≈İşlem Tipi, Tebliğ, Vade, Araç≈Plaka, Müşteri≈Cari Bilgi, Tutar, Durum)
- **Canlı fazlası:** Mail Adresi, Bakiye Tutar, Toplam Tahsilat, Kalan Bakiye, Yakıt/Vites (araç detay eko), Ceza Saati, Sözleşme No, Fatura Tar./No, İşlem Şube, Rez. Kaynağı, Ödeme Tarihi/Tutarı/Şekli, Resim (kanıt fotoğrafı) — ve tüm filtre paneli.
- **Bizde fazlası:** —
- **Not:** Alan (filtre) örtüşmesi %30 eşiğinin altında kaldığı için kural gereği ❌ YOK; temel veri modeli (Penalty) gerçekten var ve `/cezalar` üzerinde görünüyor, sadece arama/filtre ve kısmi-ödeme/bakiye takibi yok.

## cezalar.aspx — Yeni Ceza & Geçiş
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `/cezalar` (create formu, `Penalties/PenaltyList.razor`, `POST /cezalar/create`)
- **kanit:** /cezalar · K2=%23 (~7/30 gruplu: Ceza_Turu, Tebliğ_Tarihi, Vade[kısmi], Plaka[cari küme], Musteri[cari küme], Tutar[tek — 3 ayrı Ceza_Tutari1/2/3 yerine], Sebep[tek — 3 ayrı Ceza_Sebebi yerine]) · K3=n/a (tip=form)
- **Canlı fazlası:** Ceza_Saat, Ceza_Yeri, Cep_Tel, Makbuz_No, Islem_Sube, Bakiye_Isle (bakiye yaz/bekle), Odenme_Tarih, Durum (Ödenmedi/Ödendi/Kısmi/Hukuk/Vazgeçildi — bizde enum daha az kapsamlı), **3 ayrı ceza tutarı/sebebi satırı** (Ceza_Tutari1-3, Ceza_Sebebi1-3 — bir kayıtta birden çok ceza maddesi), FileUpload (resim/PDF kanıt yükleme), araç detay eko alanları (Tipi/Vites/Grubu/Marka/Yılı/Yakıt).
- **Bizde fazlası:** —
- **Not:** Tek-tutar/tek-sebep modeli canlının çok-satırlı yapısını kapsamıyor; dosya/resim kanıt yükleme tamamen yok. Para hattı → Opus.

## dis_banka_entegrasyon_listesi.aspx — Banka Entegrasyonu
- **Durum:** 🩹 CANLI BOZUK
- **Bizde:** — (canlı Runtime Error / HTTP 500, profil boş)
- **kanit:** — · K2=n/a · K3=n/a
- **Canlı fazlası:** n/a (canlı erişilemedi)
- **Bizde fazlası:** —
- **Not:** Talimatta belirtilen canlı-bozuk ekran; "bizde yok" değerlendirmesi yapılmadı. (Bizde de gerçek banka API entegrasyonu yok — CLAUDE.md §9 credential-backloğu — ama bu canlı-bozukluk notunun önüne geçmiyor.)

## extre_ozeti.aspx — Extre Özeti
- **Durum:** ❌ YOK
- **Bizde:** — (en yakın kavramsal `/raporlar/cari-bakiye`, ama farklı granülerlik)
- **kanit:** /raporlar/cari-bakiye · K2=%0 (0/3: Ofis, Tarih_Listesi, Tarih1) · K3=%0 (0/6: ID, Müşteri, Tarih, Vade, Tutar, Plaka)
- **Canlı fazlası:** Tümü — bu ekran müşteri+plaka+vade bazlı açık-tutar satırları listesi; bizde eşdeğer satır-bazlı vade/tutar raporu yok (VadePanosu araç sigorta/MTV/muayene vadesi için, farklı iş).
- **Bizde fazlası:** —
- **Not:** `/raporlar/cari-bakiye` sadece toplu bakiye+yaşlandırma gösteriyor, satır bazlı (plaka+vade) döküm yok; filtre de yok.

## genel_borc_alacak.aspx — Genel Borç & Alacak Raporu
- **Durum:** ❌ YOK
- **Bizde:** `/raporlar/cari-bakiye` (`Reports/CariBakiye.razor`) — kavramsal en yakın ama filtresiz
- **kanit:** /raporlar/cari-bakiye · K2=%0 (0/17: TextBox1 Cari Ara, Ozel_Kod, Firma_Sec, Sozlesme_Durum, Ofis, Borc_Turu, Doviz, TextBox2 Min Tutar, Devir_Tarih_Turu, Devir_Baslangic, Tarih_Turu, Devir_Tarih, F_Gizle, Sadece_Kira, Taksit, Taksit_Tarih, Acik_Sozlesme) · K3=%13 (2/15: Cari≈Cari Bilgi, Bakiye≈Kalan)
- **Canlı fazlası:** Telefon, Mail Adresi, ayrı Borç/Alacak kolonları (bizde nette birleşik Bakiye), Banka, **Mutabakat Gönder/Mutabakat** (e-mutabakat iş akışı — bizde tamamen yok), Müşteri Temsilcisi, Müşteri Özel Kod, Cari Kod + tüm filtre paneli.
- **Bizde fazlası:** Borç Yaşlandırma (0-30/31-60/61-90/90+ kova) — canlı profilinde yok (ama bu genelde ayrı bir "aging" ekranı olabilir, kanıt yetersiz).
- **Not:** K2=0 nedeniyle kural gereği ❌ YOK; alt yapıdaki cari bakiye verisi gerçek ve çalışıyor, eksik olan filtre zenginliği + mutabakat gönderim akışı.

## gider_ara.aspx — Servis & Bakım Listesi (Gider İşlem Listesi)
- **Durum:** ❌ YOK
- **Bizde:** `/giderler` (`Expenses/ExpenseList.razor`) — filtresiz
- **kanit:** /giderler · K2=%0 (0/8: TextBox1 Cari Ara, Tarih_Listesi, Tarih1, Tarih2, Ofis, Gider_Iade, Plaka Araç Ara, Gider_Adi) · K3=%17 (~8/47: No, Tarih, Plaka≈Araç/Tedarikçi, Tutar toplamları)
- **Canlı fazlası:** Servis/bakım-özel kolonların büyük çoğunluğu (İşlem KM, Dönüş KM, Uyarı KM, Geçen Süre, Servis Öncesi/Sonrası, Hazır Açıklama, Hasar Dosya No, Karşı Plaka/Trafik Sigortası, Yansıtma/Garanti Tutarı, Değer Kaybı, Hasar Tarihi) + tüm filtre paneli.
- **Bizde fazlası:** —
- **Not:** Canlı ekran adı yanıltıcı ("Servis & Bakım Listesi" menü adı ile "Gider İşlem Listesi" sayfa başlığı farklı) — muhtemelen gider+servis birleşik bir liste; bizde `/giderler` genel gider listesi olarak var ama servis/hasar-özel sütunları yok, `/servisler` (ServiceRecords, bu modülde değil) ayrı bir sayfa.

## gider_islemleri.aspx — Yeni Personel Gideri
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `/giderler` (create formu, `Expenses/ExpenseList.razor`, `POST /giderler/create`)
- **kanit:** /giderler · K2=%37 (11/30 gruplu: Tarih[yok — sunucu "şimdi"], Tutar≈Net Tutar, Tutar_Doviz≈Döviz, Tutar_Kur≈Kur, Kdv_Orani≈KDV Oranı, Gider_Adi≈Tür, Islem_Sube≈Şube, Evrak_No, Aciklama, Musteri_No≈Tedarikçi, Plaka≈Araç) · K3=n/a (tip=form)
- **Canlı fazlası:** Odeme_Tarihi (ayrı ödeme tarihi), **Kalan** (kısmi ödeme/bakiye takibi — bizde gider hep tam ödenmiş varsayılıyor), Odeme_Turu ayrı Kasa_Kodu+Hesap_No (spesifik kasa/banka hesabı seçimi), Hazir_Aciklama (kanıtlı açıklama şablonu), Sozlesme_No (kiraya bağlama), Islem_Yapan.
- **Bizde fazlası:** —
- **Not:** Canlı ekran hem Personel hem Araç hem Ofis gideri için `Islem_Turu` parametresiyle kullanılıyor; bizde tek form (Araç/Tedarikçi seçimli) tüm türleri kapsıyor ama kısmi-ödeme/"Kalan" takibi yok. Para hattı → Opus.

## gider_tanimlama.aspx — Gider Tanımı
- **Durum:** 🟡 KISMİ
- **Bizde:** `/gider-turleri` (`ExpenseCategories/ExpenseCategoryList.razor`)
- **kanit:** /gider-turleri · K2=%100 (2/2: Gider_Adi≈ad, Gider_Turu≈tur) · K3=%50 (1/2: Ad≈Gider Adı eşleşti; **Gider Türü listede kolon olarak GÖRÜNMÜYOR** — form/edit'te var, tabloda yok)
- **Canlı fazlası:** Liste görünümünde "Gider Türü" kolonu.
- **Bizde fazlası:** Kod alanı (ayrı kısa kod), Durum (Aktif/Pasif) kolonu.
- **Not:** Form alanları tam örtüşüyor ama liste tablosu "Tür"ü göstermiyor — küçük, kolay düzeltilebilir eksik.

## hesap_extresi.aspx — Cari Hesap Extresi
- **Durum:** ❌ YOK
- **Bizde:** `/cariler/{Id:guid}/ekstre` (`Customers/CustomerStatement.razor`) — çekirdek işlev var, filtre yok
- **kanit:** /cariler/{Id}/ekstre · K2=%0 (0/11: Musteri_No/Ad/Soyad cari-arama[bizde URL parametresiyle önceden seçilmiş, arama kutusu yok], Firma_Sec, Sozlesme_Durum, Doviz, Tarih_Turu, Devir_Tarih, Bit_Tarih, Ekstra_Sekli, F_Gizle) · K3=%60 (6/10: Tarih, Borç, Alacak, Bakiye, Açıklama, İşlem Türü≈Kaynak)
- **Canlı fazlası:** Tarih aralığı filtresi, Döviz filtresi, "Toplu/Taksitli" görünüm modu, Sözleşme durumu filtresi, Firma seçimi — tüm filtre/görünüm-modu paneli. Kolon: Vade, İşlem Şube, Evrak No, Plaka.
- **Bizde fazlası:** Ters Kayıt butonu (canlı profilinde görünmüyor, muhtemelen ayrı bir işlem).
- **Not:** **Önemli nüans**: temel ekstre işlevi (tarih/borç/alacak/bakiye + export + ters kayıt) GERÇEKTEN ÇALIŞIYOR ve iyi durumda; ❌ YOK sonucu münhasıran filtre/görünüm-modu zenginliğinin (K2<%30) yokluğundan geliyor, veri modeli eksik değil.

## hgs_gecis_listesi.aspx — Hgs Geçiş Listesi
- **Durum:** ❌ YOK
- **Bizde:** — (bağımsız ekran yok; HGS yalnız kira mega-formu içinde salt-okunur gömülü)
- **kanit:** — · K2=%0 (0/12: EntegrasyonZaman, EntegreTarih1/2, Tarih1/Tarih2, Bas_Saat/Bit_Saat, Plaka, Sadece_Rapor, Otomatik_Yansit, Eslesmeyenler, Fatura_No) · K3=%0 (0/16)
- **Canlı fazlası:** Tüm bağımsız/filtrelenebilir liste (tüm plakalar/kiralar arası HGS geçişleri) + Değişim Log, Alınan/Gerçek Tutar/Fark sütunları.
- **Bizde fazlası:** —
- **Not:** Kod: `KiraForm.razor` + `StickyPanel.razor`'da TEK bir kira dönemine kapsanmış salt-okunur "HGS geçişleri" tablosu var (`Vm.HgsGecisleri`, `IHgsService.GetCrossingsAsync` — v1 STUB, gerçek adaptör yok), yansıtma `HgsReflectionService` ile `/cezalar` üzerinden yapılıyor. Bağımsız, çapraz-kira filtrelenebilir bir liste ekranı YOK. Credential gerektiren HGS entegrasyonu backlog'da (CLAUDE.md §9).

## kasa_dagilimi.aspx — Genel Tahsilat & Ödeme
- **Durum:** ⚠ ERİŞİLEMEZ
- **Bizde:** `/raporlar/kasa-banka` (`Reports/KasaBankaDefteri.razor`) — `menude=hayir`, başka giriş yolu yok
- **kanit:** /raporlar/kasa-banka · K2=%60 (3/5: Tarih1≈from, Tarih2≈to, Kasa_Kodu≈Hesap) · K3=%14 (5/36: Tarih, Kaynak, Açıklama, Borç, Alacak)
- **Canlı fazlası:** Kayıt No/Saat, Hesap No, Hesap No Özel Kod, Banka, Cari Kod/Bilgisi/TC/Vergi No, İşlem Şube/Türü, Cari Türü, Plaka, RA No, D. Şube, Personel, Rez. Kaynağı, Kira Gün, Fatura Cari Id/Bilgi, Sanal Pos Onay/Detay, Merkez Kurumsal, Mail Adresi, Ülke, Dosya No, Sadece_Depozit filtresi, OfisX (şube) filtresi.
- **Bizde fazlası:** Yürüyen Bakiye kolonu (canlı profilinde ayrı görünmüyor, muhtemelen özet kartlarda).
- **Not:** K2 %60 ile makul ama sayfa menüde değil (reachable alternatif `/kasa` filtresiz).

## kasa_tanimi.aspx — Kasa Tanımı
- **Durum:** 🟡 KISMİ
- **Bizde:** `/hesaplar` (`FinancialAccounts/FinancialAccountList.razor`) — Kasa+Banka birleşik tanım tablosu
- **kanit:** /hesaplar · K2=%50 (1/2: Kasa_Kodu≈Ad; Islem_Mail eşleşmedi) · K3=%50 (1/2: Kasa Tanımı≈Ad/Kod; Uyarı Mail Listesi eşleşmedi)
- **Canlı fazlası:** Islem_Mail / "Uyarı Mail Listesi" (kasa bazlı e-posta uyarı listesi) — bizde hiçbir hesap tanımında yok.
- **Bizde fazlası:** Tür (Kasa/Banka ayrımı — canlıda bu SADECE kasa, banka ayrı bir tanım evreninde), Döviz, IBAN, Hesap No, Banka, Şube, Durum(Aktif/Pasif) — bizim `/hesaplar` canlının ayrı "Kasa Tanımı" + "Banka Tanımı" (bu modülde değil, 02-tanım-master'da olabilir) ekranlarını BİRLEŞTİRİYOR.
- **Not:** Kavramsal karşılık var (kasa adı tanımlama) ama uyarı-maili özelliği yok; ayrıca bizim sayfa daha genel (Kasa+Banka birleşik).

## kasa_virman.aspx — Kasa Virman
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `/kasa` (Virman formu, `Finance/KasaHub.razor`, `POST /finans/virman`)
- **kanit:** /kasa · K2=%31 (4/13: Kaynak≈K_Kasa_Kodu[kısmi—tip seviyesi], Hedef≈H_Kasa_Kodu[kısmi], Tutar, Aciklama) · K3=n/a (tip=diğer, kolon yok)
- **Canlı fazlası:** K_Kasa_Doviz/Kuru ve H_Kasa_Doviz/Kuru (ayrı kaynak/hedef döviz+kur — bizde formda döviz/kur seçimi YOK, tek Tutar alanı), Makbuz_No, Islemi_Yapan, Islem_Sube; ayrıca canlıda BİRDEN FAZLA adlandırılmış kasa arasında seçim var (K_Kasa_Kodu/H_Kasa_Kodu select), bizde tek "Kasa" kovası.
- **Bizde fazlası:** —
- **Not:** `FinancialAccount` (`/hesaplar`) çoklu kasa tanımını tutuyor ama `CashService.TransferAsync` bunu kullanmıyor — tanım ile işlem tarafı bağlı değil (wire-in eksikliği). Para hattı → Opus.

## kredi_takip_listesi.aspx — Kredi Takip Listesi
- **Durum:** ❌ YOK
- **Bizde:** `/arac-kredi` (`AracKredileri/AracKrediList.razor`) — ad benzer ama muhtemelen farklı iş
- **kanit:** /arac-kredi · K2=%0 (0/8: Musteri_No/Ad_Soyad, Tarih_Listesi, Tarih1/Tarih2, Plaka/Arac, Odeme_Durum) · K3=%0 (0/6: Plaka, Cari Bilgi, Vade, Toplam Kredi Borç, Taksit Tutarı, Kalan Bedel)
- **Canlı fazlası:** Tüm kolonlar — canlı ekran **Cari Bilgi + Plaka + Vade + Ödeme Durumu** eksenli (müşteriye taksitli satılan/kredi ile verilen araç borcu gibi görünüyor); bizim `AracKredi` entity'si ise "banka kredisi ile araç alımı" (şirketin banka borcu) — Cari alanı YOK, Vade/Ödeme_Durum yok.
- **Bizde fazlası:** BankaAdi, FaizOran, TaksitSayisi/OdenenTaksit, Durum(Aktif/Kapandı) — canlıda görünmüyor.
- **Not:** **Ad benzerliği eşleşme değil (kural 3)**: canlı "Kredi Takip" muhtemelen müşteri kredili satış/taksit takibi, bizim `/arac-kredi` şirketin ARAÇ SATIN ALIRKEN kullandığı banka kredisi takibi — iki farklı iş. Ayrıca not: `AracKredi.VehicleId` alanı şemada var ama ne create formunda ne listede kullanılıyor (wire-in eksik).

## nakit_islem.aspx — Nakit Tahsilat
- **Durum:** PARA — OPUS'A DEVİR
- **Bizde:** `/finans/tahsilat` + `/finans/odeme` (formlar, `Customers/CustomerStatement.razor` üzerinden; `Finance/FinanceEndpoints.cs`)
- **kanit:** /cariler/{Id}/ekstre (Tahsilat/Ödeme formu) · K2=%33 (5/15: Musteri/Ad/Soyad[cari, sayfa bağlamından — arama kutusu değil], Tutar, Cari_Doviz≈Döviz, Cari_Kur≈Kur, Aciklama) · K3=n/a (tip=form)
- **Canlı fazlası:** Tarih (manuel tarih — bizde sunucu "şimdi"), Onerilen_Tutar (bakiyeden otomatik öneri), Makbuz_No, Kasa_Kodu (spesifik kasa — bizde sadece Kasa/Banka tipi), ayrı Kasa_Kur/Cari_Kur (bizde tek Kur), Islem_Sube, Islem_Yapan.
- **Bizde fazlası:** —
- **Not:** Bizde Tahsilat/Ödeme SADECE bir cari'nin ekstre sayfasından (önce cari seçilmiş) yapılabiliyor; canlıda kendi başına bağımsız ekran (cari arama kutusu dahil). Para hattı → Opus.

## nakit_islem_ara.aspx — Nakit Tahsilat Listesi
- **Durum:** ❌ YOK
- **Bizde:** `/kasa` (`Finance/KasaHub.razor`) — erişilebilir ama filtresiz
- **kanit:** /kasa · K2=%0 (0/5: TextBox1 Cari Ara, Islem_Sube, Tarih_Listesi, Tarih1, Tarih2) · K3=%38 (5/13: No≈Kayit No, Tarih, Cari≈Cari Bilgi, Tutar, Açıklama)
- **Canlı fazlası:** Cari Kod, Kasa Kodu (spesifik), İşlem Şube, Cari Tutar/Döviz, Müş. Özel Kod, gerçek Makbuz No kolonu (bizde yalnız PDF linki) + tüm filtre paneli.
- **Bizde fazlası:** Ters mi (bool) — canlı export'unda var, ekran profilinde ayrı kolon değil.
- **Not:** `/kasa` menüde ve erişilebilir, veri gösteriyor (son 50 işlem) ama filtre yok → K2=0 kural gereği ❌ YOK; export (`ListExportCatalog.NakitIslemler`) da Cari adı/kodu kolonunu İÇERMİYOR — export'ta da aynı boşluk var.

## tahsilat_raporu.aspx — Tahsilat Fatura Raporu
- **Durum:** ❌ YOK
- **Bizde:** `/raporlar/tahsilat-fatura` (`Reports/TahsilatFatura.razor`) — farklı granülerlik (özet, satır yok)
- **kanit:** /raporlar/tahsilat-fatura · K2=%29 (2/7: Tarih1≈from, Tarih2≈to; Tarih_Listesi, Sozlesme_No_1, Bakiye, Islem_Sube, Hizmet eşleşmedi) · K3=%0 (0/35 — bizim sayfa satır/tablo İÇERMİYOR, sadece 5 özet kartı)
- **Canlı fazlası:** Sözleşme bazlı TÜM satır kolonları (Sözleşme No, Plaka, Müşteri, Baş/Bit Tarih-Saat, Matrah, Damga Vergisi, Sözleşme/Müşteri/TPC Toplam-Tahsilat-Bakiye üçlüleri, Faturalanan/Fark, Günlük Fiyat, Gün, Kira Bedeli, Hizmet) + Sözleşme No arama, Bakiye durumu filtresi, Hizmet filtresi, Mail At aksiyonu.
- **Bizde fazlası:** —
- **Not:** Bizim ekran sadece dönem-toplamı (Fatura Adet/Toplam, Tahsilat Adet/Toplam, Fark) — canlının sözleşme-satırı düzeyindeki mutabakat raporunun kaba bir özeti. K2 %30 eşiğinin altında (2/7≈%29) → ❌ YOK.

---

TOPLAM: 30 ekran işlendi
