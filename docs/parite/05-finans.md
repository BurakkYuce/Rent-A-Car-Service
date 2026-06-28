# 05 — FİNANS Modülü Parite Analizi

> Kaynak: diske kaydedilmiş canlı TürevRent HTML'leri (`parite_html/`), ASP.NET WebForms + DevExpress `ASPxGridView`.
> Karşılaştırma: clone `src/` (.NET 10 / Blazor SSR). Tarih: 2026-06-28.
> Yöntem: alan adları `name="ctl00$ContentPlaceHolder1$…"`, grid kolonları DevExpress `dxgvHeader` hücre metni, butonlar `type=submit value`.
> Analiz edilen ekran: **33** (31 atanan + `cari_virman` & `cari_virman_islem_ara` bonus; `bakiye_islem_ara` = 146B stub, atlandı).

Lejant: ✅ tam karşılığı var · 🟡 kısmi (çekirdek var, alanlar/akış eksik) · ❌ clone'da yok.

---

## A. FATURA / E-FATURA

### 1. `fatura.aspx` — Fatura kesme (ana ekran) 🟡
**Amaç:** Cariye fatura kesme; KDV sistemi, ÖTV, tevkifat, iade faturası, e-fatura gönderimi, satır kalemleri.
**Form alanları (seçme):**
- Cari/müşteri: `Musteri_No`, `Ad`, `Adres`, `Sehir`, `Ilce`, `Vergi_Dairesi`, `Vergi_Numarasi`, `Cep_Tel`, `Mail_Adresi`, `PostaKutusu`, `Internet_Sitesi`, `Teklif_Sorumlusu`
- Belge: `Evrak_No`, `Fatura_Tarihi`, `Fatura_Saat`, `Vade_Tarihi`, `Donem_Bitis_Tarihi`, `Fatura_Kayit_No_X`, `Islem_Sube`, `Islem_Turu_X`, `Odeme_Turu`, `Gonderim_Sekli`, `Fatura_Sablon`, `Fatura_Ozel_Kod`, `Tek_Plaka`
- **KDV/vergi:** `Kdv_Sistemi`, `Kdv_Indirimi`, `Kdv_Sifir_Sebep`, `OTV_Tutari`, `OTV_KDV`, **`Tevkifat_Kodu`, `Tevkifat_Oran`**
- **İade faturası:** `Iade`, `IadeFaturaNo`, `IadeFaturaTarihi`
- **e-Fatura:** `EFatura_Tipi`, `EFatura_Firma`, `EFatura_ID`, `E_Fatura_UUID`, `Cari_Entegre`, `E_Fatura_Degistir`, `E_Fatura_Silemez`, `E_Fatura_Ek_Seri_Aktif`, `Fatura_Pdf_Adi`, `Fatura_Cikti_Yol`
- **Footer (toplamlar):** `Footer_Ara_Toplam`, `Footer_Indirim_Toplam`, `Footer_Kdv`, `Footer_Genel_Toplam`, `Footer_Toplam_Tutar`, `Footer_Doviz`, `Footer_Doviz_Toplam`, `EDoviz`, `USD`/`EURO`/`GBP`
- Bayraklar: `Fatura_Kesmez`, `Fatura_Iptal_Edemez`, `Fatura_Tek_Satir`, `Kira_Dan_Fatura`, `Komisyon_Bakiye_Dus`, `Otomatik_Uzat`
**Butonlar:** `Fatura Kaydet`, `Durum`, `Evrak`, `Kontrol`
**Clone durumu:** `Invoice`(No, Durum, CariId, RentalId, Tarih, NetTutar, KdvTutar, GenelToplam, Currency, Kur, `EFaturaEttn`, `EFaturaGonderildi`) + `InvoiceLine`(Aciklama, Miktar, BirimNetFiyat, KdvOrani, SatirNet, SatirKdv, SatirToplam). Servis: `InvoiceService.CreateFromRentalAsync` (yalnız kiradan otomatik). e-Fatura = **stub** (ETTN yazılıyor, gerçek GİB yok).
**Eksik:** ❌ ÖTV (`OTV_Tutari`/`OTV_KDV`), ❌ **tevkifat** (`Tevkifat_Kodu`/`Tevkifat_Oran`), ❌ **iade/return faturası**, ❌ **serbest/manuel fatura** (kiradan bağımsız kesme), ❌ KDV sistemi seçimi & KDV-sıfır sebebi, ❌ vade tarihi/dönem, ❌ damga vergisi, ❌ fatura şablon/çıktı yolu/PDF adı, ❌ e-fatura tip/firma/UUID alanları. → **🟡**

### 2. `fatura_islem_listesi.aspx` — Fatura işlem listesi (+ toplu faturala) 🟡
**Filtreler:** `Fatura_No1/2`, `Tarih1/2`, `Tarih_Listesi`, `Islem_Turu`, `Ofis`, `Plaka`, `Fatura_Durumu`, `Fatura_Kesmez`
**Grid (22 kol):** ID · Fatura Tarihi · Cari Kod · Cari Bilgi · Vergi Dairesi · Vergi Numarası · Fatura No · E-Fatura No · İptal · Ofis · Tutar · Kdv · Genel Toplam · Enteg. iletim · Müş Özel Kod · Açıklama · Müşteri Ülke · Genel Toplam Dvz · Doviz · Vade Tarihi · Durum · Özel Kod
**Butonlar:** `Excel'e Aktar`, **`Seçili Olanları Faturala`** (batch), `Fatura Durumları`, `XML'e Aktar`, `Tablo Ayarlarını Kaydet`
**Clone:** `InvoiceService.ListAsync` + Web liste. → 🟡 — liste var; ❌ **toplu faturala**, ❌ XML/Excel export, ❌ durum/iletim yönetimi.

### 3. `fatura_detay_listesi.aspx` — Fatura satır/detay listesi 🟡
**Filtreler:** `Fatura_No1/2`, `Tarih1/2`, `Tarih_Listesi`, `Islem_Turu`, `Ofis`, `Plaka`, `Satir_Kodu`, `RezDetayGoster`
**Butonlar:** `Excel'e Aktar`, `Tablo Ayarlarını Kaydet`
**Clone:** Satır-bazlı rapor görünümü yok; `InvoiceLine` verisi mevcut ama detay listesi/export ekranı yok. → 🟡

### 4. `gelen_e_fatura_listesi.aspx` — GELEN e-Fatura listesi ❌
**Amaç:** GİB'den gelen e-faturaları onaylama/reddetme/işleme (inbound).
**Filtreler:** `Fatura_No1/2`, `Tarih1/2`, `Islem_Turu`, `Plaka`, `EFatura_Firma`, `Arac_KM`, `Satir_Kodu`
**Butonlar:** `Faturayı Göster`, **`Faturayı Onayla`**, **`Faturayı Reddet`**, **`Faturayı İşle`**, **`Sadece Kdv Yansıt`**, `Excel'e Aktar`
**Clone durumu:** ❌ **Gelen/inbound e-fatura tamamen yok.** Outbound bile stub. → **❌ KRİTİK**

---

## B. KASA / NAKİT

### 5. `nakit_islem.aspx` — Nakit tahsilat/ödeme ✅
**Alanlar:** `Musteri_No`, `Ad`/`Soyad`, `Tarih`, `Kasa_Kodu`, `Tutar`, `Kasa_Doviz`/`Kasa_Kur`, `Cari_Tutar`/`Cari_Doviz`/`Cari_Kur`, `Islem_Turu`, `Islem_Sube`, `Makbuz_No`, `Aciklama`, `Cari_Bakiye_Dus`, `Onerilen_Tutar`, `Kira_Key`, `Kredi_Key`
**Butonlar:** `Yeni Tahsilat`, `Yeni Ödeme`, `Kaydet`, `Sil`, `Tahsilat Listesi`, `Ödeme Listesi`
**Clone:** `CashService.CollectAsync` (tahsilat) + `PayAsync` (ödeme), `KarsiHesap = Kasa`; çift-taraflı defter + kiraysa sözleşme Tahsilat/Bakiye günceller; düzeltme `ReverseAsync` (ters kayıt). → ✅ (çekirdek tam; çok-döviz Money ile var).

### 6. `nakit_islem_ara.aspx` — Nakit işlem arama 🟡
**Filtreler:** `Tarih1/2`, `Tarih_Listesi`, `Islem_Turu`, `Islem_Sube`
**Grid:** Cari Kod · Cari Bilgi · Tarih · Kasa Kodu · Tutar · Döviz · Cari Tutar · Cari Döviz · İşlem Şube · Makbuz No · Müş. Özel Kod · Açıklama
**Clone:** `CashService.ListAsync` (filtreli). → 🟡 (liste/filtre var; Excel export yok).

### 7. `genel_kasa.aspx` — Genel kasa defteri 🟡
**Filtreler:** `Kasa_Kodu`, `Doviz`, `Islem_Tipi`, `Personel`, `Arac_Sahibi`, `Tarih1/2`
**Grid:** Cari Bilgi · Kasa Kodu · Borç · Alacak · Döviz · İşlem Türü · Açıklama · Şube · Evrak No · Personel · Araç Sahibi · Plaka
**Clone:** `ReportService.GetKasaBankaSummaryAsync` + `GetAccountLedgerAsync`. → 🟡 (defter raporu var; kasa-bazlı borç/alacak görünümü kısmi).

### 8. `kasa_dagilimi.aspx` — Kasa dağılımı (detaylı defter) 🟡
**Filtreler:** `Kasa_Kodu`, `OfisX`, `Tarih1/2`, `Bas_Saat`/`Bit_Saat`, `Sadece_Depozit`
**Grid (35 kol):** Tarih · Saat · Kasa Adı · Borç · Alacak · Döviz · Hesap No · Banka · Cari Kod · Cari Bilgisi · TC/Vergi · İşlem Şube/Türü · Plaka · RA No · Personel · Rez. Kaynağı · Kira Gün · Makbuz No · Fatura Cari · Sanal Pos Onay/Detay · Mail · Ülke · Dosya No …
**Clone:** Ledger raporu var; bu kadar geniş dağılım/saat-bazlı görünüm yok. → 🟡

### 9. `kasa_virman.aspx` — Kasa↔Kasa virman 🟡
**Alanlar:** `K_Kasa_Kodu`/`H_Kasa_Kodu` (kaynak/hedef kasa), `*_Doviz`/`*_Kuru`/`*_Tutari`, `Tarih`, `Makbuz_No`, `Aciklama`, `Islem_Sube`
**Butonlar:** `Yeni Kasa Virman`, `Kaydet`, `Kasa Virman Listesi`
**Clone:** `CashService.TransferAsync(kaynak, hedef, …)` — ama `LedgerAccountType` seviyesinde (Kasa↔Banka tipi), **belirli kasa kodu↔kasa kodu değil**. → 🟡 (genel virman var; spesifik kasa-kodu virman & liste yok).

---

## C. BANKA / HESAP

### 10. `banka_islemleri.aspx` — Banka/kredi-kartı tahsilatı (POS) 🟡
**Amaç:** Banka tahsilatı + **kredi kartı / sanal POS / taksit / 3D / kart saklama**.
**Alanlar (seçme):** `Hesap_No`, `Banka_No`, `Tutar`, `Banka_Doviz`/`Banka_Kur`, `Cari_Tutar`/`Cari_Doviz`/`Cari_Kur`, `Makbuz_No`, `Islem_Turu`, `Islem_Sube`, `Tarih`, `Aciklama` + **POS/kart:** `Kart_No`, `Kart_CVV`, `Kart_Sahibi`, `Kart_Vade`, `Kart_Turu`, `Taksit_Adet`, `TaksitDegeri`, `Sanal_Pos_*`, `Pos_Turu`/`Pos_Isyeri_No`/`Pos_Sifre`/`Pos_Store_Key`, `Garanti_Pos_*`, `Kart_Provizyon`, `Odeme_Link_URL_Ayarlar`, `Odemelink`, `Hediye_Cek`, `Depozit_Sanal_Pos`, `Iade_Tutar_Degeri`
**Butonlar:** `Kaydet`, `Yeni Kredi Kart Tahsilatı İşlemi`, `Kredi Kart Tahsilatı Listesi`, `Kayıtlı Kartı Sil`, `Mail Gönder`, `Sil`
**Clone:** `CashService.CollectAsync` `KarsiHesap=Banka` (defter). → 🟡 — banka tahsilatı defter düzeyinde var; ❌ **POS/sanal POS/kredi kartı/taksit/3D/provizyon/ödeme linki** (entegrasyon stub bile yok).

### 11. `banka_islem_ara.aspx` — Gelen havale/banka işlem arama 🟡
**Filtreler:** `Hesap_No`, `Islem_Turu`, `Islem_Sube`, `Tarih1/2`, `Provizyon`, `Fatura_Listesi`, `Muhasebe_Sorumlusu`, `Talep`
**Grid (27 kol):** Cari Bilgi/Kod · TC · Vergi No · Tarih · Hesap No · Banka · Tutar · Döviz · Cari Tutar/Döviz · Makbuz No · İşlem Türü · Sanal Pos İşlem No · İşlem Şube/Yapan · İade Toplam · Kaynak · Özel Kod · Açıklama · İlişkili Kira/Fatura No · Kira Durum · Plaka · Araç Sahibi · Kira Bit. Tar
**Butonlar:** `Yeni Gelen Havale İşlemi`, `Excel'e Aktar`
**Clone:** `CashService.ListAsync` (banka). → 🟡 (liste var; havale-özel kolonlar/sanal pos no/export yok).

### 12. `banka_hesap_hareketleri.aspx` — Banka hesap hareketleri 🟡
**Filtreler:** `Hesap_No`, `Hesap_No_Kendi`, `Islem_Sube`, `Tarih1/2`, `Devir_Goster`
**Grid:** Cari Bilgi · Hesap No · Borç · Alacak · Döviz · İşlem Türü · Açıklama · Şube
**Clone:** `GetAccountLedgerAsync`/`GetKasaBankaSummaryAsync`. → 🟡 (defter var; hesap-no bazlı hareket + devir kısmi).

### 13. `banka_virman.aspx` — Banka↔Banka virman 🟡
**Alanlar:** `K_Banka`/`H_Banka` + `K_Hesap_No`/`H_Hesap_No` (kaynak/hedef hesap), `*_Doviz`/`*_Kuru`/`*_Tutari`, `K_Sube`/`H_Sube`, `Tarih`, `Makbuz_No`, `Aciklama`
**Butonlar:** `Yeni Banka Virman`, `Kaydet`, `Banka Virman Listesi`
**Clone:** `TransferAsync` tip-seviyesi (Kasa↔Banka). → 🟡 (belirli banka-hesap↔banka-hesap virman yok).

### 14. `banka_virman_islem_ara.aspx` — Banka virman listesi 🟡
**Grid:** Tarih · Kaynak Hesap No · Hedef Hesap No · Kaynak Tutar/Döviz · Hedef Döviz
**Butonlar:** `Yeni Banka Virman İşlemi`, `Excel'e Aktar` → 🟡 (virman listesi yok).

### 15. `hesap_para_islem.aspx` — Hesaptan para çekme/yatırma 🟡
**Alanlar:** `Hesap_No`, `Banka`/`Banka_No`, `Banka_Doviz`/`Banka_Kur`, `Kasa_Kodu`, `Tutar`, `Islem_Turu`, `Sube`, `Tarih`, `Makbuz_No`, `Aciklama`
**Butonlar:** `Yeni Hesaptan Para Çekme Kaydı`, `Kaydet`, `Sil`, `Hesaptan Para Çekme Listesi`
**Clone:** `TransferAsync` / Collect-Pay. → 🟡 (kasa↔banka aktarım var; hesap-no bazlı para çekme/yatırma belgesi yok).

### 16. `banka_nakit_listesi.aspx` — Hesaba para yatırma listesi 🟡
**Filtreler:** `Hesap_No`, `Islem_Turu`, `Islem_Sube`, `Tarih1/2` · **Buton:** `Yeni Hesaba Para Yatırma Kaydı` → 🟡

### 17. `banka_para_listesi.aspx` — Banka para işlem listesi 🟡
**Filtreler:** `Hesap_No`, `Islem_Turu`, `Islem_Sube`, `Tarih1/2` (sadece liste) → 🟡

---

## D. CARİ / EKSTRE / BAKİYE

### 18. `hesap_extresi.aspx` — Cari hesap ekstresi ✅
**Filtreler:** `Musteri_No`, `Ad`/`Soyad`, `Devir_Tarih`/`Bit_Tarih`, `Doviz`, `Tarih_Turu`, `Ekstra_Sekli`, `Sozlesme_Durum`, `Firma_Sec`, `Devreden_Ucret`
**Grid:** Vade · Borç · Alacak · Bakiye · İşlem Türü · Açıklama · İşlem Şube · Evrak No · Plaka
**Butonlar:** `Excel'e Aktar`, `Genel Borç Alacak Listesi`
**Clone:** `CashService.GetStatementAsync` (AccountLedger) + `GetCariBalanceAsync`. → ✅ (cari ekstre var; çoklu-firma/devreden kısmi).

### 19. `genel_borc_alacak.aspx` — Genel borç/alacak + yaşlandırma 🟡
**Filtreler:** `Borc_Turu`, `Doviz`, `Ofis`, `Ozel_Kod`, `Tarih_Turu`, `Devir_Tarih`, `Sozlesme_Durum`, `Acik_Sozlesme`, `Sadece_Kira`, `Taksit`, `Firma_Sec`
**Grid:** Cari Bilgi · Telefon · Mail · Borç · Alacak · Kalan · Döviz · Durum · Banka · **Mutabakat Gönder/Mutabakat** · Müşteri Temsilcisi · Müşteri ÖzelKod · Cari Kod
**Butonlar:** `Filtrele`, `Excel'e Aktar`
**Clone:** `GetCariBalancesAsync` + `GetAgingAsync` (yaşlandırma). → 🟡 (bakiye+yaşlandırma var; ❌ **mutabakat gönderimi/mail**, kapı filtreleri kısmi).

### 20. `extre_ozeti.aspx` — Ekstre özeti (ofis bazlı) 🟡
**Filtreler:** `Ofis`, `Tarih1`, `Tarih_Listesi` · **Grid:** Müşteri · Tarih · Vade · Tutar · Plaka · **Buton:** `Excel Kaydet`
**Clone:** `GetCariBalancesAsync`. → 🟡 (özet rapor kısmi).

### 21. `bakiye_islem.aspx` — Cari alacaklandır (manuel bakiye) 🟡
**Alanlar:** `Musteri_No`, `Ad`/`Soyad`, `Tarih`, `Vade`, `Tutar`, `Doviz`/`Kur`, `Islem_Turu`, `Islem_Sube`, `Plaka`, `Makbuz_No`, `Aciklama`
**Butonlar:** `Yeni Cari Alacaklandır Kaydı`, `Kaydet`, `Cari Alacaklandır Listesi`
**Clone:** Manuel cari alacaklandırma belgesi yok (yalnız tahsilat/ödeme defteri etkiler). → 🟡 (ters/ödeme ile yaklaşık; özel manuel-bakiye dökümanı yok).

### 22. `cari_virman.aspx` — Cari↔Cari virman ❌ *(bonus)*
**Alanlar:** `Kaynak_Musteri_No`/`Kaynak_Ad`, `Hedef_Musteri_No`/`Hedef_Ad`, `Tutar`, `Doviz`/`Kur`, `Vade`, `Tarih`, `Makbuz_No`, `Aciklama`, `Islem_Turu`
**Butonlar:** `Kaydet`, `Cari Virman Listesi`
**Clone durumu:** ❌ **Cari↔cari virman yok.** `TransferAsync` yalnız Kasa↔Banka tipi. → **❌ KRİTİK**

### 23. `cari_virman_islem_ara.aspx` — Cari virman listesi ❌ *(bonus)*
**Filtreler:** `Islem_Turu`, `Tarih1/2`, `Tarih_Listesi` · **Buton:** `Yeni Cari Virman Kaydı` → ❌ (yok).

---

## E. GİDER

### 24. `gider_islemleri.aspx` — Gider işlem girişi ✅
**Alanlar:** `Gider_Adi`, `Plaka`/`Marka`/`Tipi`/`Yili`/`Vites`/`Yakit_Turu`, `Musteri_No`, `Tutar`, `Kdv_Orani`/`Kdv_Tutari`, `Tutar_Doviz`/`Tutar_Kur`, `Odeme`/`Odeme_Doviz`/`Odeme_Kur`/`Odeme_Tarihi`/`Odeme_Turu`, `Hesap_No`, `Kasa_Kodu`, `Islem_Turu`, `Islem_Sube`, `Evrak_No`, `Kira_ID`, `Sozlesme_No`, `Ceza_Kodu`, `Periyodik_KM`, `Hazir_Aciklama`, `FileUpload1`, `Gider_Iade`
**Butonlar:** `Yeni Araç Gider Kaydı`, `Kaydet`, `Görüntüle`, `Araç Gider Listesi`
**Clone:** `Expense`(Tip, Tarih, VehicleId, CariId, Sube, EvrakNo, NetTutar, KdvOrani, KdvTutar, GenelToplam, Currency, Kur, OdemeYontemi, KasaBankaHesap) + `ExpenseService.CreateAsync`. → ✅ (gider+KDV+ödeme defter var; dosya yükleme/iade alanları kısmi).

### 25. `gider_ara.aspx` — Gider arama 🟡
**Filtreler:** `Gider_Adi`, `Islem_Turu`, `Ofis`, `Plaka`, `Tarih1/2`, `Gider_Iade`, `Entegrasyon`, `Personel_Gider_Goremez`
**Grid (48 kol):** Tarih · Gider Adı · Cari · Plaka/Marka/Tipi/Yakıt/Vites/Model · Ödeme Aracı · TL Toplam · Kira Yakıt/Hasar · Vergi · Kalan · Fatura Toplam · İşlem/Dönüş/Uyarı KM · Durum · Yansıtma Bedeli · Servis Öncesi/Sonrası · Kusur/Beyan Türü · Periyodik/Bakım/Hasar/Mekanik · Kim Ödeyecek · Servis Yeri · Hasar Dosya/Karşı Plaka/Karşı Sigorta · Değer Kaybı · Hasar Tarihi …
**Butonlar:** `Yeni Araç Gider İşlemi`, `Yeni Ofis Gider İşlemi`, `Excel'e Aktar`
**Clone:** `ExpenseService.ListAsync`. → 🟡 (liste var; hasar/servis/yansıtma kolonları & ofis-gider ayrımı kısmi).

### 26. `toplu_gider.aspx` — Toplu gider (çoklu araç) ❌
**Amaç:** Birden çok araca tek seferde gider yazma.
**Alanlar:** `Arac_Tipi`, `Arac_Kayit_No_X`, `Plaka` (çoklu ekleme), `Gider_Adi`, `Tutar`, `Doviz`/`Kur`, `Hesap_No`/`Kasa_Kodu`, `Odeme_Turu`, `Islem_Turu`, `Vade`
**Butonlar:** `Araç Ekle`, `Kaydet`
**Clone durumu:** ❌ **Toplu/batch gider yok** (tekil `CreateAsync`). → **❌ KRİTİK**

---

## F. TAHSİLAT (toplu / otomatik / rapor)

### 27. `toplu_tahsilat.aspx` — Toplu tahsilat ❌
**Amaç:** Carinin açık işlemlerini getirip toplu tahsilat.
**Alanlar:** `Musteri_No`/`Ad`/`Soyad`, `Mst_K`, `Tutar`, `Secili_Toplam`, `Doviz`/`Kur`, `Kasa_Kodu`/`Hesap_No`, `Odeme_Turu`, `Islem_Sube`, `Tarih`
**Butonlar:** `Carinin İşlem Listesini Getir`, `Tahsilatı Kaydet`
**Clone durumu:** ❌ **Toplu tahsilat yok** (tekil `CollectAsync`). → **❌ KRİTİK**

### 28. `otomatik_tahsilat.aspx` — Otomatik tahsilat ❌
**Amaç:** Sözleşme bazlı otomatik/periyodik tahsilat.
**Filtreler:** `Sozlesme_No_1`, `Bakiye`, `Islem_Turu`, `Islem_Sube`, `SeciliSubeler`, `Tarih1/2`
**Clone durumu:** ❌ **Otomatik/periyodik tahsilat yok.** → **❌ KRİTİK**

### 29. `tahsilat_raporu.aspx` — Tahsilat raporu 🟡
**Filtreler:** `Sozlesme_No_1`, `Bakiye`, `Hizmet`, `Islem_Turu`, `Islem_Sube`, `SeciliSubeler`, `Tarih1/2`
**Grid (34 kol):** Sözleşme No · Plaka · Durum · Rez. Kaynağı · Çıkış/Dönüş Ofisi · Ödeme Şekli · Faturalama Tipi · Kiralama Türü · Müşteri Ad/Soyad · Baş/Bit Tar-Saat · **Matrah · Damga Vergisi** · Sözleşme Toplamı/Tahsilat/Bakiye · Müşteri Bedel/Tahsilat/Bakiye · TPC Tutar/Tahsilat/Bakiye · Faturalanan · Fatura Fark · Günlük Fiyat · Gün · Kira Bedeli · Hizmet
**Clone:** `ReportService.GetTahsilatFaturaAsync`. → 🟡 (tahsilat-fatura raporu var; TPC/damga/müşteri-bedel ayrımı kısmi).

---

## G. CEZA / HGS

### 30. `cezalar.aspx` — Ceza kaydı ✅/🟡
**Alanlar:** `Musteri_No`/`Ad`, `Plaka`/`Marka`/`Tipi`/`Yili`/`Vites`/`Yakit_Turu`/`Grubu`, `Ceza_Turu`, `Ceza_Sebebi`(+2/3), `Ceza_Tutari`(+1/2/3), `Ceza_Tarihi`, `Ceza_Saat`, `Ceza_Yeri`, `Teblig_Tarih`, `Vade_Tarihi`, `Odenme_Tarih`, `Kira_No`, `Bakiye_Tutar`/`Bakiye_Isle`, `Otomatik_Ceza_Indirimli`, `Makbuz_No`, `Durum`, `FileUpload1`
**Butonlar:** `Kaydet`, `Yeni Ceza Geçiş Kaydı`, `Ceza Geçiş Listesi`, `Ceza Excel'ini Görüntüle`
**Clone:** `Penalty`(No, CezaTuru, TebligTarihi, VadeTarihi, VehicleId, CariId, RentalId, Tutar, Sebep, Durum) + `PenaltyService.CreateAsync`/`YansitAsync`/`OdeAsync`/`IptalAsync`. → 🟡 (ceza çekirdeği + yansıtma/ödeme/iptal var; çoklu sebep/tutar, indirimli, ceza saati/yeri, dosya yükleme kısmi).

### 31. `ceza_listesi.aspx` — Ceza listesi ✅
**Filtreler:** `Plaka`, `Arac`, `Musteri_No`, `Ad_Soyad`, `Ceza_Durum`, `Odeme_Durum`, `Ofis`, `Makbuz_No`, `Tarih1/2`
**Grid (28 kol):** Cari Bilgi · Mail · Ceza Tarihi · İşlem Tipi · Durum · Makbuz No · Ceza Sebebi · Tutar · Bakiye Tutar · Toplam Tahsilat · Kalan Bakiye · Tipi/Yakıt/Vites · Tarih · Tebliğ/Vade Tar · Ceza Saati · Sözleşme No · Fatura Tar/No · İşlem Şube · Rez. Kaynağı · Ödeme Tarihi/Tutarı/Şekli · Açıklama · Resim
**Clone:** `PenaltyService.ListAsync`. → ✅ (liste var; bazı kolonlar kısmi).

### 32. `ceza_gecis_listesi.aspx` — Ceza geçiş listesi (import) 🟡
**Filtreler:** `Plaka`, `Sozlesme_No`, `TC_Kimlik`, `Tarih1/2`, `Bas_Saat`/`Bit_Saat`, `Ceza_Yansitma_Tarih`, `Otomatik_Ceza_Indirimli`, `Sadece_Rapor`
**Grid:** Turu · Plaka · Söz. No · Tutar · Ceza Tarihi/Saati · Makbuz No · Ceza Maddesi · Ceza Yeri · Şehir · İlçe · Ekleme Zamanı
**Butonlar:** `Excel'e Aktar`, `Tablo Ayarlarını Kaydet`
**Clone:** Ceza-geçiş import/yansıtma listesi yok (Penalty manuel). → 🟡 (geçiş kaydı→ceza otomasyonu yok).

### 33. `hgs_gecis_listesi.aspx` — HGS geçiş listesi + yansıtma 🟡
**Filtreler:** `Plaka`, `Sozlesme_No`, `Fatura_No`, `Tarih1/2`, `EntegreTarih1/2`, `Bas_Saat`/`Bit_Saat`, `Eslesmeyenler`, `Gecis_Yansitma_Turu`, `Otomatik_Yansit`, `HGS_Hizmet_Orani`/`HGS_Aylik_Hizmet_Orani`, `Otomatik_Hgs_Servis`, `Sadece_Rapor`
**Grid:** HGS Etiket · Turu · Plaka · Söz. No · Tutar · Hizmet Dahil · Baş/Bit Zamanı · Giriş/Çıkış Noktası · Ekleme Zamanı · Değişim Log
**Butonlar:** `Excel'e Aktar`, `Özet Excel'e Aktar`, `Tablo Ayarlarını Kaydet`
**Clone:** `HgsReflectionService.ReflectAsync` (geçişi kira/cariye yansıtma). → 🟡 (yansıtma mantığı var; ❌ HGS geçiş **import/eşleştirme listesi**, hizmet oranı %, otomatik yansıt, özet export yok).

---

## ÖZET TABLO

| # | Ekran | Amaç | Clone | Not |
|---|-------|------|:----:|-----|
| 1 | fatura | Fatura kesme | 🟡 | KDV+satır var; ÖTV/tevkifat/iade/manuel/e-fatura eksik |
| 2 | fatura_islem_listesi | Fatura listesi + toplu | 🟡 | toplu faturala/XML yok |
| 3 | fatura_detay_listesi | Satır detay listesi | 🟡 | detay/export ekranı yok |
| 4 | gelen_e_fatura_listesi | Gelen e-fatura | ❌ | inbound tamamen yok |
| 5 | nakit_islem | Nakit tahsilat/ödeme | ✅ | defter+ters tam |
| 6 | nakit_islem_ara | Nakit arama | 🟡 | export yok |
| 7 | genel_kasa | Kasa defteri | 🟡 | rapor var |
| 8 | kasa_dagilimi | Kasa dağılımı | 🟡 | geniş kolonlar yok |
| 9 | kasa_virman | Kasa↔kasa virman | 🟡 | tip-seviyesi; kasa-kodu yok |
| 10 | banka_islemleri | Banka/POS tahsilat | 🟡 | POS/kredi kartı/taksit yok |
| 11 | banka_islem_ara | Havale arama | 🟡 | liste var |
| 12 | banka_hesap_hareketleri | Banka hareketleri | 🟡 | defter var |
| 13 | banka_virman | Banka↔banka virman | 🟡 | hesap-no virman yok |
| 14 | banka_virman_islem_ara | Banka virman listesi | 🟡 | liste yok |
| 15 | hesap_para_islem | Para çekme/yatırma | 🟡 | hesap-no belgesi yok |
| 16 | banka_nakit_listesi | Para yatırma listesi | 🟡 | — |
| 17 | banka_para_listesi | Banka para listesi | 🟡 | — |
| 18 | hesap_extresi | Cari ekstre | ✅ | statement var |
| 19 | genel_borc_alacak | Borç/alacak+yaşlandırma | 🟡 | mutabakat/mail yok |
| 20 | extre_ozeti | Ekstre özeti | 🟡 | rapor kısmi |
| 21 | bakiye_islem | Cari alacaklandır | 🟡 | manuel-bakiye belgesi yok |
| 22 | cari_virman | Cari↔cari virman | ❌ | yok |
| 23 | cari_virman_islem_ara | Cari virman listesi | ❌ | yok |
| 24 | gider_islemleri | Gider girişi | ✅ | gider+KDV+defter |
| 25 | gider_ara | Gider arama | 🟡 | hasar/servis kolon kısmi |
| 26 | toplu_gider | Toplu gider | ❌ | batch yok |
| 27 | toplu_tahsilat | Toplu tahsilat | ❌ | batch yok |
| 28 | otomatik_tahsilat | Otomatik tahsilat | ❌ | yok |
| 29 | tahsilat_raporu | Tahsilat raporu | 🟡 | TPC/damga kısmi |
| 30 | cezalar | Ceza kaydı | 🟡 | çoklu sebep/indirimli kısmi |
| 31 | ceza_listesi | Ceza listesi | ✅ | liste var |
| 32 | ceza_gecis_listesi | Ceza geçiş import | 🟡 | import/otomasyon yok |
| 33 | hgs_gecis_listesi | HGS geçiş+yansıtma | 🟡 | import/% /özet export yok |

**Skor:** ✅ 5 · 🟡 22 · ❌ 6 (33 ekran).

### Kritik eksikler (öncelik)
1. **Toplu/otomatik işlemler** — `toplu_gider`, `toplu_tahsilat`, `otomatik_tahsilat` (batch & periyodik). Clone yalnız tekil `CreateAsync`/`CollectAsync`.
2. **Gelen e-Fatura** (`gelen_e_fatura_listesi`) — onayla/reddet/işle/KDV yansıt inbound akışı yok; outbound bile stub.
3. **Cari↔cari virman** (`cari_virman`) — yok. Ayrıca `TransferAsync` yalnız `LedgerAccountType` (Kasa↔Banka) seviyesinde; belirli **banka-hesap↔banka-hesap** ve **kasa-kodu↔kasa-kodu** virman da eksik.

**Diğer önemli fatura eksikleri:** ÖTV, **tevkifat**, **iade (return) faturası**, kiradan bağımsız **manuel fatura**, damga vergisi, KDV-sistemi/sıfır-sebep.
