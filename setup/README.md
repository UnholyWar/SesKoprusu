# Windows x64 kurulumu

Derleme makinesinde .NET 10 SDK ve Inno Setup 6 kurulu olmalı. ISCC.exe dosyasını PATH'e ekleyin. Resmî VB-CABLE Windows paketini https://vb-audio.com/Cable/ adresinden indirip bir klasöre çıkarın. Paketi başkalarına dağıtmadan önce sürücünün dağıtım koşullarını kontrol edin.

~~~powershell
.\setup\Build.ps1 -CablePackageDirectory C:\Drivers\VB-CABLE
~~~

İsteğe bağlı çevrimdışı paketleme için -CodexDirectory ile resmî Windows x64 Codex CLI paketinin codex.exe ve yardımcı dosyalarını içeren klasörü kullanın. Uygulama açılınca Codex Ayarları üzerinden cihaz koduyla giriş yapın. Parola ve ikinci doğrulama adımı tarayıcıdaki ChatGPT giriş sayfasında tamamlanır. Oturum SesKöprüsü için ayrı tutulur.

setup/Output/SesKoprusu-Setup.exe uygulama ile sürücü kurulumunu içerir. Sürücü sihirbazını tamamlayıp Windows'u yeniden başlatın. İlk çalıştırmada Windows varsayılan çıkışını dinleyeceğiniz fiziksel aygıta ayarlayın. Seslendirme için Codex hesabında realtime ses erişimi gerekir. Ses oturumunu destekleyen Codex iş parçacığı gpt-6.2-sol ve low muhakeme düzeyini kullanır. Ses akışı ayrı realtime modeliyle yürür.

## Windows kabul testi

1. Windows ses panelinde varsayılan oynatma ve kayıt aygıtlarını, ayrıca iletişim varsayılanlarını kaydedin. Tarayıcıyı sistemin varsayılan çıkışına ayarlayın.
2. YouTube'da konuşma içeren bir video açın. Oynatma sürerken anahtarı açın. Yalnızca gelen çeviri açıkken varsayılan oynatma aygıtının CABLE Input olduğunu ve kayıt panelinde CABLE Output göstergesinin videodaki sesle hareket ettiğini kontrol edin. Konuşmanın önceki hoparlör veya kulaklıktan duyulduğunu dinleyerek doğrulayın.
3. Videoyu durdurmadan anahtarı kapatın. Dört varsayılanı ilk kayıtla karşılaştırın ve sesi dinleyin. İşlemi tekrarlayıp anahtar açıkken uygulamayı kapatın ve aynı karşılaştırmayı yapın.
4. Anahtar açıkken Görev Yöneticisi ile uygulamayı sonlandırın. Yeniden açılışta kayıtlı aygıtların geri geldiğini kontrol edin.
5. Codex Ayarları'ndan cihaz koduyla giriş yapın. Bağlantı testi hesap kullanım limitlerini sorgular; modelden metin üretmesini istemez. Başarılı olduğunda ana ekranda “Codex bağlı” görünür. CLI eksikse ilk girişte resmî Windows x64 bileşeni kullanıcı klasörüne indirilir ve SHA-256 doğrulanır. ChatGPT hesabı bölümünde gösterilen yeni kodu açılan doğrulama sayfasına girin. İptal işlemi kodu ekrandan kaldırır. Durumu yenile ile oturumu kontrol edebilirsiniz.
6. Voice sekmesinde Codex default veya oturumun sunduğu bir sesi seçin. Türkçe örneği dinleyin. Gelen ses için VB-CABLE, yalnızca giden çeviri için de temel VB-CABLE yeterlidir. İki yönde birden çeviri için temel kabloya ek olarak VB-Audio Hi-Fi Cable kullanılır. İki yönün hedef dilini ayrı seçin. Anahtarı açtığınızda seçilen yönün sesi Codex App Server realtime oturumuna iletilir. Codex’in ses çıkışı seçilen aygıtta oynatılır. Dil seçimini değiştirmek için anahtarı kapatın.
7. Gelen sesi kulaklıkta, giden sesi ayrı bir görüşme istemcisinde dinleyin. Oturumu kapatıp yeniden açınca iki dil tercihini doğrulayın.

Oturum kalıcılığını sınamak için ilk başarılı girişten sonra uygulamayı kapatıp aynı Windows kullanıcısıyla yeniden açın. Kayıtlı ChatGPT oturumu otomatik okunmalı ve yeni kod gösterilmemelidir. Bağlantı kesilirse ağı geri getirip Durumu yenile düğmesini kullanın.

Codex Settings → Audio devices bölümündeki Install or enable devices seçeneği, devre dışı uçları etkinleştirir. Temel sürücü yoksa paketlenmiş kurucuyu veya VB-Audio’nun resmî indirmesini kullanır. Çift yön için **Codex Settings → Devices → Set up microphone driver (Hi-Fi Cable)** ile üreticinin kurucusunu indirin ve Windows kurulumunu tamamlayın. Üreticinin önerdiği yeniden başlatmadan sonra **Refresh devices** ile kontrol edin. Hi-Fi Cable Input ve Output için Windows Ses özelliklerindeki varsayılan biçimlerin örnekleme hızını eşitleyin (örneğin her ikisi de 48000 Hz). ASIO Bridge açıksa Pass Through modunu kullanın. [Üreticinin indirme ve kişisel kullanım bilgisi](https://vb-audio.com/Cable/) İndirmeler ve çalışma dosyaları %LOCALAPPDATA%\SesKoprusu altında tutulur. Paketle gelen araçlar uygulama klasöründen kullanılır. Windows sürücü kaydı yönetici onayı ve bazen yeniden başlatma gerektirir.

## Test kanalından güncelleme

GitHub Actions, test/ses-koprusu dalına gelen push sonrasında Windows x64 setup üretir. Başarılı derlemeler GitHub Releases altında setup-test-v1.0.N etiketiyle ön sürüm olarak yayımlanır. İlk kurulumda SesKoprusu-Setup.exe çalıştırılır. Sonraki sürümler Codex Settings → Updates → Check for updates üzerinden kurulabilir. Uygulama tam kurulum paketini indirir, SHA-256 doğrulaması yapar ve kurucuyu açar. Kullanıcı ayarları ve oturum uygulamanın LocalAppData klasöründe kalır.

Otomatik pakette VB-CABLE yeniden dağıtılmaz. Kurulu sürücüler kullanılmaya devam eder, eksikler Devices bölümünden kurulur. Workflow için depoda Actions ve Releases yazma yetkisi gerekir. Uygulama güncelleme kanalını kimlik doğrulamasız okuduğu için release dosyaları kullanıcıya açık olmalıdır.

Windows Ses Kaydedicisi testi: Anahtar kapalıyken fiziksel mikrofonu varsayılan giriş, kulaklığı varsayılan çıkış yapın. Incoming audio → Keep original ve Outgoing audio → English seçin. Köprüyü açtıktan sonra Ses Kaydedicisi’nde CABLE Output ile kayıt başlatın, Türkçe bir cümle söyleyin ve çeviri için birkaç saniye bekleyin. Kaydı durdurup kulaklıktan dinleyin. Gelen yönlendirme de açıksa giden ses Hi-Fi Cable Output üzerinden kaydedilir.

### Çift yönlü görüşme testi

1. Köprü kapalıyken fiziksel kulaklığı ve fiziksel mikrofonu Windows varsayılanları olarak seçin. Görüşme uygulamasını sistem varsayılanlarını kullanacak şekilde ayarlayın.
2. Incoming → Turkish ve Outgoing → Keep original seçin. Karşı tarafın sesi kulaklığa Türkçe gelirken mikrofonunuzu doğal haliyle göndermesi beklenir.
3. Köprüyü kapatıp Outgoing → English seçin. Devices altında hem CABLE hem Hi-Fi Cable hazır olmalı. Anahtarı açın. Görüşme uygulaması elle aygıt seçiyorsa hoparlör için CABLE Input, mikrofon için Hi-Fi Cable Output kullanın.
4. Karşı taraf konuşurken kulaklıktan çeviriyi dinleyin. Siz Türkçe konuştuğunuzda karşı tarafın İngilizce duyduğunu doğrulayın. Sizin mikrofonunuz sessizken karşı tarafın sesi onun mikrofon girişine geri dönmemelidir. Köprüyü kapatıp önceki aygıtları kontrol edin.
