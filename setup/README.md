# Windows x64 kurulumu

Derleme makinesinde .NET 10 SDK ve Inno Setup 6 kurulu olmalı. ISCC.exe dosyasını PATH'e ekleyin. Resmî VB-CABLE Windows paketini https://vb-audio.com/Cable/ adresinden indirip bir klasöre çıkarın. Paketi başkalarına dağıtmadan önce sürücünün dağıtım koşullarını kontrol edin.

~~~powershell
.\setup\Build.ps1 -CablePackageDirectory C:\Drivers\VB-CABLE
~~~

İsteğe bağlı çevrimdışı paketleme için -CodexDirectory ile resmî Windows x64 Codex CLI paketinin codex.exe ve yardımcı dosyalarını içeren klasörü kullanın. Uygulama açılınca Codex Ayarları üzerinden cihaz koduyla giriş yapın. Parola ve ikinci doğrulama adımı tarayıcıdaki ChatGPT giriş sayfasında tamamlanır. Oturum SesKöprüsü için ayrı tutulur.

setup/Output/SesKoprusu-Setup.exe uygulama ile sürücü kurulumunu içerir. Sürücü sihirbazını tamamlayıp Windows'u yeniden başlatın. İlk çalıştırmada Windows varsayılan çıkışını dinleyeceğiniz fiziksel aygıta ayarlayın. Seslendirme için Codex hesabında realtime ses erişimi gerekir. SESKOPRUSU_CODEX_MODEL ile hesabınızdaki CLI uyumlu Codex modelini seçebilirsiniz.

## Windows kabul testi

1. Windows ses panelinde varsayılan oynatma ve kayıt aygıtlarını, ayrıca iletişim varsayılanlarını kaydedin. Tarayıcıyı sistemin varsayılan çıkışına ayarlayın.
2. YouTube'da konuşma içeren bir video açın. Oynatma sürerken anahtarı açın. Windows varsayılanlarının CABLE Input ve CABLE Output olduğunu ve kayıt panelinde CABLE Output göstergesinin videodaki sesle hareket ettiğini kontrol edin. Konuşmanın önceki hoparlör veya kulaklıktan duyulduğunu dinleyerek doğrulayın.
3. Videoyu durdurmadan anahtarı kapatın. Dört varsayılanı ilk kayıtla karşılaştırın ve sesi dinleyin. İşlemi tekrarlayıp anahtar açıkken uygulamayı kapatın ve aynı karşılaştırmayı yapın.
4. Anahtar açıkken Görev Yöneticisi ile uygulamayı sonlandırın. Yeniden açılışta kayıtlı aygıtların geri geldiğini kontrol edin.
5. Codex Ayarları'ndan cihaz koduyla giriş yapın. Bağlantı testi gerçek bir kısa Codex isteği gönderir. Başarılı olduğunda ana ekranda “Codex bağlı” görünür. CLI eksikse ilk girişte resmî Windows x64 bileşeni kullanıcı klasörüne indirilir ve SHA-256 doğrulanır. ChatGPT hesabı bölümünde gösterilen yeni kodu açılan doğrulama sayfasına girin. İptal işlemi kodu ekrandan kaldırır. Durumu yenile ile oturumu kontrol edebilirsiniz.
6. Voice sekmesinde Codex default veya oturumun sunduğu bir sesi seçin. Türkçe örneği dinleyin. Gelen ses için VB-CABLE, giden çeviri için ayrıca VB-CABLE A gerekir. Giden çeviriyi kullanacaksanız VB-Audio'nun A+B paketini kurun. İki yönün hedef dilini ayrı seçin. Anahtarı açtığınızda seçilen yönün sesi Codex App Server realtime oturumuna iletilir. Codex’in ses çıkışı seçilen aygıtta oynatılır. Dil seçimini değiştirmek için anahtarı kapatın.
7. Gelen sesi kulaklıkta, giden sesi ayrı bir görüşme istemcisinde dinleyin. Oturumu kapatıp yeniden açınca iki dil tercihini doğrulayın.

Oturum kalıcılığını sınamak için ilk başarılı girişten sonra uygulamayı kapatıp aynı Windows kullanıcısıyla yeniden açın. Kayıtlı ChatGPT oturumu otomatik okunmalı ve yeni kod gösterilmemelidir. Bağlantı kesilirse ağı geri getirip Durumu yenile düğmesini kullanın.

Codex Settings → Audio devices bölümündeki Install or enable devices seçeneği, devre dışı uçları etkinleştirir. Temel sürücü yoksa paketlenmiş kurucuyu veya VB-Audio’nun resmî indirmesini kullanır. Giden ses için ayrıca üreticiden edinilen A+B zip dosyasını seçin. İndirmeler ve çalışma dosyaları %LOCALAPPDATA%\SesKoprusu altında tutulur. Paketle gelen araçlar uygulama klasöründen kullanılır. Windows sürücü kaydı yönetici onayı ve bazen yeniden başlatma gerektirir.
