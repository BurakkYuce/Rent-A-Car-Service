# KVKK — Excel/CSV Export'larda Kişisel Veri Notu

> Bu not, RentACar'daki liste/rapor **dışa aktarımlarının (export)** içerdiği kişisel verileri, hukuki dayanağı, erişim
> kontrolünü ve sorumlulukları özetler. TürevRent paritesi gereği export'lar PII içerir (TC/telefon/maaş); bu bilinçli
> bir karardır ve aşağıdaki kontrollerle sınırlanmıştır. Sütun tanımları tek yerde (`ListExportCatalog`) → gerektiğinde
> PII sütunları kaldırılıp "PII'siz mod"a geçmek tek dosyada mümkündür.

## 1. Hangi export hangi kişisel veriyi içerir
| Export | Kişisel veri (PII) | Erişim (rol) |
|---|---|---|
| **cariler** | TC Kimlik No, Vergi No, telefon, e-posta, il/ilçe | ViewReports |
| **kiralar / cezalar** | müşteri ad-soyad, plaka | ViewReports |
| **nakit-işlem / gider / araç-*** | — (ticari/operasyonel; doğrudan kişi verisi az) | ViewReports |
| **personel** | **TC Kimlik No, MAAŞ**, sürücü belge no, işe giriş/çıkış | **ManageUsers (yalnız Admin)** |

Plaka, kişiye bağlanabildiği ölçüde kişisel veridir. Personel maaşı **özel nitelikli olmasa da hassas** kabul edilip
en dar role (ManageUsers) bağlanmıştır; ticari müşteri PII'si ViewReports ile sınırlıdır.

## 2. Hukuki dayanak (KVKK md. 5/2)
- **Müşteri PII'si (cari/kira/ceza):** sözleşmenin kurulması/ifası + veri sorumlusunun meşru menfaati (operasyon, tahsilat,
  raporlama). TC/vergi no ayrıca vergi/faturalama mevzuatı gereği tutulur.
- **Personel PII'si (TC/maaş):** iş sözleşmesi + kanuni yükümlülük (SGK/vergi/bordro). Erişim İK/yönetim (Admin) ile sınırlı.

## 3. Erişim kontrolü (teknik)
- **Rol-bazlı gate:** ticari export'lar `ViewReports`; personel export'u `ManageUsers` (ayrı uç `/listeler/export-personel`).
- **Kiracı izolasyonu:** tüm export'lar tenant-kapsamlı (PostgreSQL RLS + EF query filter) → başka firmanın verisi sızmaz.
- **Şube kapsamı:** operatör export'ları kendi şubesiyle sınırlı (kira/gider/BAF `BranchScope` → yalnız kendi şube satırları).
- **Şifreleme:** TC/maaş at-rest şifreli (`*Enc`, ISecretProtector); yalnız yetkili export ucunda bellekte çözülür.

## 4. İzlenebilirlik (denetim izi)
Her export bir `GET /listeler/export/*` isteğidir ve **Serilog request-log'unda** kaydedilir: kim (kullanıcı), ne zaman,
hangi liste. **PII değerleri loga YAZILMAZ** (yalnız URL + kullanıcı + zaman). Daha güçlü denetim gerekirse export ucuna
özel audit-write eklenebilir (küçük ek).

## 5. Veri minimizasyonu ve saklama
- **Minimizasyon:** sütunlar deklaratif (`ListExportCatalog`) → TC/maaş gibi alanlar tek dosyada çıkarılıp "PII'siz mod"a
  geçilebilir (raporlama çoğu senaryoda TC/maaş gerektirmez). İstenirse config anahtarıyla varsayılan PII'siz yapılabilir.
- **Saklama:** indirilen dosya **kullanıcının cihazında** oluşur; sunucuda saklanmaz. İndirilen Excel/CSV kurumsal
  veri-saklama/imha politikasına tabidir (paylaşım, e-posta, bulut yüklemesi = yeni aktarım riski).
- **Aktarım:** dosyanın kurum dışına çıkarılması üçüncü-tarafa aktarım olabilir → aydınlatma/açık rıza ve aktarım
  kayıtlarının güncel olması gerekir.

## 6. Öneriler (uyum sertleştirmesi)
1. Export yetkisini **need-to-know**'a daralt (ViewReports/ManageUsers'ı yalnız gereken kullanıcılara ver).
2. TC/telefon için **maskeleme** opsiyonu değerlendir (ör. TC son 2 hane) — tam değer yalnız zorunlu senaryoda.
3. İndirilen dosyalar için kurumsal **DLP / saklama-imha** kuralı; hassas export'lar için erişim-log denetimi.
4. Aydınlatma metni + (gerekliyse) açık rıza kayıtlarını export kapsamını içerecek şekilde güncel tut.

---

### Özet (yönetim için)
RentACar export'ları, TürevRent'teki gibi kişisel veri içerir; ancak **rol-bazlı erişim (ticari=ViewReports,
personel/maaş=Admin), kiracı+şube izolasyonu, at-rest şifreleme ve Serilog denetim izi** ile sınırlandırılmıştır. TC/maaş
gibi alanlar tek noktadan (`ListExportCatalog`) yönetilir; istenirse dakikalar içinde "PII'siz mod"a geçilebilir. İndirilen
dosyaların kurum içi saklanması/paylaşılması KVKK sorumluluğu doğurur → need-to-know daraltması + DLP önerilir.
