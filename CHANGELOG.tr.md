# Değişiklik Günlüğü

🇬🇧 [English](CHANGELOG.md) · 🇹🇷 **Türkçe**

Bu projedeki tüm önemli değişiklikler bu dosyada belgelenir.
Biçim [Keep a Changelog](https://keepachangelog.com/tr-TR/1.1.0/) esas alınarak hazırlanmıştır.

## [Yayımlanmamış]

### Eklendi

- Serilog ile `%LOCALAPPDATA%\EtkinlikYonetim\logs` altında günlük dönen log dosyaları: 30 günden eski dosyalar
  silinir ve en fazla 30 dosya tutulur (testler bunu tarihli dosyalarla denetler). Hata kayıtları yalnızca istisna
  türlerini, HResult kodlarını ve metot adlarını içerir.
- Anonim denetim kayıtları: yalnızca sabit bir kod ve bir rol, asla bir hesap. Her başarısız giriş (bilinmeyen hesap,
  yanlış şifre, kilitli hesap) aynı kodu yazar: `auth.sign_in.failed`.
- Kayıt, giriş, ilk yönetici, katılımcı, etkinlik, etkinlik kaydı ve iptal kuralları için merkezi FluentValidation
  doğrulayıcıları; kurallar ve Türkçe mesajlar değişmedi.
- Daha fazla Moq ve SQLite testi: kapasite sınırları, geçmiş tarihler, iptal ve yeniden kayıt, IDOR yanıtları, kilit
  süresi ve T.C. Kimlik No kontrol basamakları; log dosyalarının kişisel veri veya anahtar içermediğini denetleyen
  testler.
- Mimari diyagramlı İngilizce ve Türkçe README, tehdit modeli (`docs/THREAT_MODEL.md`) ve bu değişiklik günlüğü.
- CI'da sürüm işi: `v*` etiketlerinde etiket adı denetlenir, win-x64 derlemesi yayımlanır ve zip çıktısı olarak
  yüklenir.
- Bekleme listesi: dolu bir etkinlik katılımcıyı sıra numarasıyla bekleme listesine alır; iptal edilen yer hemen
  sıradaki kişiye biletiyle birlikte geçer (`AddWaitingList` migration'ı).
- Yöneticiler için etkinlik yönetimi: günü geçmemiş bir etkinliğin tarihi, kapasitesi (mevcut kayıtların altına
  inmeden) ve açıklaması değiştirilir ya da etkinlik iptal edilir; iptal edilen etkinlik listede kalır ve kayıtları
  hesaplarına iptal edildi olarak gösterilir (`AddEventManagement` migration'ı).
- Katılımcının hesabının yeniden gösterebildiği QR kodlu biletler (QRCoder 1.6.0, MIT) ve yöneticiler için ikinci
  okutmada "zaten kullanıldı" diyen, sayaçlı giriş kontrolü penceresi (`AddTicketCheckIn` migration'ı).
- Bir etkinliğin katılımcı ve giriş listesinin bilet kodu içermeyen CSV ve yazdırılabilir HTML dışa aktarımı.
- Yöneticiler için hesap yönetimi: etkinleştirme, devre dışı bırakma, yönetici veya kullanıcı yapma; yöneticinin
  kendi hesabı ve son etkin yönetici korunur (`AddAccountDisabledFlag` migration'ı).
- Bekleme listesi, kapasite değişikliği, etkinlik iptali, eşzamanlı giriş kontrolü ve birbirinin rolünü düşüren
  yöneticiler için SQL Server yarış testleri.
- Kendine özgü kimliğiyle yeniden tasarlanan ekranlar: özgün bir bilet logosu (`assets/logo.svg`, simgeye
  `tools/Screenshots` ile dönüştürülür), serin mürekkep grisi nötrler üzerinde kobalt ve macenta bir palet,
  Bahnschrift başlıklar ve Segoe UI metin. Açık, koyu ve Windows yüksek karşıtlık temaları; `Appearance:Theme` ayarı
  (`Light`, `Dark`, `System`) ve uygulama çalışırken karşıtlık ile uygulama modu değişikliklerini izleyen açık
  pencereler.
- Başlık, pencere içi ileti şeridi, kayan gövde ve alt şeritten oluşan ortak bir ekran çerçevesi; her ekran
  içeriğine göre, ekranının çalışma alanı içinde boyutlanır ve monitör başına DPI uyumludur.
- Sezonun etkinliklerini yer sayılarıyla ve her ekrana kısayolları gösteren bir yönetim paneli, bilet biçiminde bir
  bilet penceresi, türünün renginde bir panelde büyük gösterilen giriş yanıtı, listelerde yer, bekleme sırası, iptal,
  giriş ve hesap durumları için etiketler ve giriş ekranına döndüren bir oturum kapatma ("Oturumu kapat").
- Uydurma verilerle 1x ve 2x pencere açmadan ekran görüntüleri (`tools/Screenshots`) ve her ekranı göstermeden
  kurup yerleşimini 1024x600, 1366x768 ve 1920x1040 çalışma alanlarında 1-2 yazı ölçeğinde denetleyen arayüz testleri.

### Değişti

- Sonuçlar ve kural retleri pencerenin içinde gösterilir; yalnızca sorular ve bir pencereden uzun yaşaması gereken
  iletiler, ekrandan büyük açılmayan temalı bir ileti penceresi açar. Girişten sonraki karşılama kutusu kaldırıldı.
- Eski üçüncü taraf denetim kütüphanesi ve özgün ekranların arka plan görseli artık kullanılmaz.
- Hatalar izleme çıktısı yerine log dosyasına yazılır; hata pencereleri değişmedi.
- Bilet kodları artık `ETK-` ve 26 karakterdir (130 rastgele bit); migration mevcut her bilete 128 rastgele bitlik
  yeni bir kod verir, bu yüzden yükseltmeden önce gösterilen kodlar artık çalışmaz.
- Yöneticinin katılımcı listesi bilet kodu yerine kaydın durumunu (giriş yaptı, bekliyor, iptal) gösterir; yönetici
  bilet kodlarını yalnızca kendi eklediği katılımcıların görür.
- Reddedilen işlemler log dosyasına artık "Operation refused" satırı yazmaz.
- Aynı etkinliğin veya hesapların başka bir değişikliğini 10 saniyeden fazla bekleyen bir değişiklik, sunucuyla
  ilgili bir mesaj yerine "hiçbir değişiklik yapılmadı, birkaç saniye sonra tekrar deneyin" diye reddedilir. Komutlar
  istemci tarafından iptal edilmeden önce 60 saniye, açılıştaki işlemler (eski veritabanı yükseltmesi ve
  migration'lar) beş dakika alır; böylece istemci, kilidin kendi bekleme süresinden önce vazgeçmez.
- Giriş ekranı içeri alınan katılımcının adını gösterir, biletleri yalnızca etkinlik günü kullanır (başka bir gün
  bileti harcamadan "ETKİNLİK GÜNÜ DEĞİL" gösterir), kod alanını açık tutar; böylece okuyucunun okuttuğu sonraki kod
  kaybolmaz, süren denetimden hemen sonra denetlenir. Eski bir sayaç yanıtı artık yenisinin yerine geçmez.
- Bilet penceresi QR kodu göstermeden önce kaydı yeniden okur; bu arada iptal edilmiş bir bilet gösterilmez.

### Güvenlik

- Kurulum anahtarının süresi, anahtarın kullanıldığı anda veritabanının saatine göre belirlenir: son `UPDATE`
  `SonKullanma > SYSUTCDATETIME()` koşulunu arar ve `TuketimTarihi` değerini veritabanının saatiyle yazar;
  kullanılabilir anahtarların seçimi de artık bilgisayarın saatine bağlı değildir. Bir kurulum beklerken süresi
  dolan ya da yalnızca geri kalmış bir saatin kabul edeceği anahtar reddedilir ve kullanılmamış kalır.
- Adlar, e-posta adresleri, kimlik numaraları, şifreler, kurulum anahtarları ve istisna mesajları loga asla yazılmaz.
- Açık oturumlar her işlemde veritabanına göre denetlenir: rolü düşürülen, devre dışı bırakılan veya silinen bir
  hesap hâlâ açık bir pencereden işlem yapamaz; devre dışı hesaplar giriş yapamaz. Bir hesabın erişimindeki her
  değişiklik oturum sürümünü artırır (migration `AddSessionVersion`); hesap devre dışı bırakılmadan veya rolü
  düşürülmeden önce açılan bir pencere, hesap yeniden etkinleştirilse ya da yönetici yapılsa da kapalı kalır; hesap
  yeniden giriş yapar.
- Bilet kodları SHA-256 özetleriyle bulunur ve sabit zamanda karşılaştırılır; loga ve dışa aktarımlara asla yazılmaz.
- Log dosyası kayıt olma, etkinlik kaydı, iptal ve girişte okutmanın her sonucu için tek bir nötr kod yazar; artık
  hangi hesapların, kayıtların veya biletlerin var olduğunu belli etmez.
- Tarihler ve saatler veritabanının saatinden alınır: hangi etkinliklerin geçmiş olduğu, bir kaydın hâlâ yapılıp
  yapılamayacağı veya iptal edilip edilemeyeceği, giriş kilitleri ve kayıtlar, biletler, iptaller ve girişler için
  yazılan zamanlar. Saati yanlış olan ya da bilerek geri alınmış bir bilgisayar artık geçmiş bir etkinliğe kayıt
  yapamaz, kayıtlarını iptal edemez, bir giriş kilidini erken bitiremez ve sahte giriş zamanları yazamaz.
- Bileti girişte okutulmuş bir kayıt artık iptal edilemez ("Giriş yapılmış bir kayıt iptal edilemez.") ve giriş
  işlemi etkinliğin kilidini alır: içerideki katılımcı yerini korur, yer sıradaki bekleyene geçmez; böylece salonda
  yer sayısından fazla kişi olmaz.
- Form açıldıktan sonra başka bir yöneticinin değiştirdiği bir etkinliği kaydetmek, eski tarihi, kapasiteyi veya
  açıklamayı geri yazmak yerine reddedilir; etkinlik yönetimi güncel değerleri gösterir ve değişikliğin yeniden
  yapılmasını ister. Karar değerlerle değil, her değişiklikte artan bir düzenleme sürümüyle verilir (migration
  `AddEventEditVersion`); böylece büyük/küçük harfe duyarsız harmanlamanın eşit saydığı, açıklamanın yalnızca harf
  büyüklüğünü değiştiren bir düzenleme ("Salon" yerine "SALON") de artık ezilmez.
- Her yazma işlemi hesabını kendi işleminin içinde, kilidi aldıktan sonra yeniden denetler: bir kayıt, iptal,
  etkinlik değişikliği veya giriş etkinliğin kilidini beklerken devre dışı bırakılan, rolü düşürülen ya da değiştirilip
  geri alınan bir hesap hiçbir şey yazmaz ve penceresi giriş ekranına döner. Hesap değişiklikleri artık işlemi yapan
  yöneticinin oturum sürümünü de karşılaştırır. Kilitlenme (deadlock) kurbanı olan bir işlem meşgul mesajıyla
  bildirilir; hiçbir şey yazılmamıştır.
- Kayıtlar, iptaller, etkinlik değişiklikleri ve girişler veritabanının saatini yazan işlemin içinde, kilitten sonra
  okur; gece yarısını kilit bekleyerek geçen bir işlem artık geçmiş bir gün için işlem yapmaz ve yazdığı anın
  zamanını kaydeder. Giriş, etkinliğin tarihini kilit altında yeniden okur: okutma beklerken başka bir güne taşınan
  bir etkinliğin bileti artık kullanılmış sayılmaz.
- Etkinlik yönetimi, hangi etkinliklerin hâlâ değiştirilebileceğine bilgisayarın saatiyle değil veritabanının
  günüyle karar verir; ileri giden bir saat artık güncel etkinlikleri kilitlemez.

## Temel

Özgün uygulamanın yeniden yazımı; bu değişiklik günlüğü başlamadan önce birleştirildi.

### Eklendi

- EF Core, bağımlılık enjeksiyonu ve otomatik migration'larla katmanlı .NET 10 çözümü (Domain, Business, Data, UI);
  özgün veritabanı verileri korunarak yerinde yükseltilir ve katılımcının tek etkinliği biletli bir kayda dönüşür.
- Bilet kodlu kayıtlar, iptal; kapasite ve çift kayıt denetimleri veritabanı işleminin içinde.
- Boş veritabanı için ilk kurulum; hesapları olan ama yöneticisi olmayan veritabanı için yalnızca veritabanı
  sahibinin `docs/issue-setup-key.sql` ile ürettiği tek kullanımlık kurulum anahtarı ve uygulama oturumu için
  `docs/grant-app-login.sql` örnek yetkileri.
- Varsayılan olarak kapalı, isteğe bağlı demo modu (`ETKINLIK_DEMO`, `Seed:Demo`).
- SQLite üzerinde servis testleri; SQL Server'da yarış, kurulum anahtarı ve eski şema yükseltmesi testleri; SQL
  Server işi olan CI.

### Güvenlik

- PBKDF2 şifre özetleri ve düz metin şifrelerin bir sonraki girişte yükseltilmesi; 5 hatalı denemeden sonra 15
  dakikalık kilit ve her hatada aynı yanıt.
- Her serviste rol ve sahiplik denetimi; kimlik numaraları hesap başına benzersiz ve yönetim listelerinde maskeli.
- Varsayılan kurulumda bilinen şifreli hesap yok.
