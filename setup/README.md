# Windows x64 kurulumu

Derleme makinesinde .NET 10 SDK ve Inno Setup 6 kurulu olmalı. ISCC.exe dosyasını PATH'e ekleyin. Resmî VB-CABLE Windows paketini https://vb-audio.com/Cable/ adresinden indirip bir klasöre çıkarın. Paketi başkalarına dağıtmadan önce sürücünün dağıtım koşullarını kontrol edin.

~~~powershell
.\setup\Build.ps1 -CablePackageDirectory C:\Drivers\VB-CABLE -CodexDirectory C:\Tools\Codex-x64
~~~

CodexDirectory için resmî Windows x64 Codex CLI paketinin codex.exe ve yardımcı dosyalarını içeren klasörü kullanın. Uygulama açılınca Codex Ayarları üzerinden cihaz koduyla giriş yapın. Parola ve ikinci doğrulama adımı tarayıcıdaki ChatGPT giriş sayfasında tamamlanır. Oturum SesKöprüsü için ayrı tutulur.

setup/Output/SesKoprusu-Setup.exe uygulama ile sürücü kurulumunu içerir. Sürücü sihirbazını tamamlayıp Windows'u yeniden başlatın. İlk çalıştırmada Windows varsayılan çıkışını dinleyeceğiniz fiziksel aygıta ayarlayın. Seslendirme için uygulamayı başlatmadan önce OPENAI_API_KEY ortam değişkenini tanımlayın. SESKOPRUSU_CODEX_MODEL ile hesabınızdaki CLI uyumlu Codex modelini seçebilirsiniz.

## Windows kabul testi

1. Windows ses panelinde varsayılan oynatma ve kayıt aygıtlarını, ayrıca iletişim varsayılanlarını kaydedin. Tarayıcıyı sistemin varsayılan çıkışına ayarlayın.
2. YouTube'da konuşma içeren bir video açın. Oynatma sürerken anahtarı açın. Windows varsayılanlarının CABLE Input ve CABLE Output olduğunu ve kayıt panelinde CABLE Output göstergesinin videodaki sesle hareket ettiğini kontrol edin. Konuşmanın önceki hoparlör veya kulaklıktan duyulduğunu dinleyerek doğrulayın.
3. Videoyu durdurmadan anahtarı kapatın. Dört varsayılanı ilk kayıtla karşılaştırın ve sesi dinleyin. İşlemi tekrarlayıp anahtar açıkken uygulamayı kapatın ve aynı karşılaştırmayı yapın.
4. Anahtar açıkken Görev Yöneticisi ile uygulamayı sonlandırın. Yeniden açılışta kayıtlı aygıtların geri geldiğini kontrol edin.
5. Codex Ayarları menüsünden bir ses seçin, Türkçe örneği dinleyin, uygulamayı yeniden açıp seçimi kontrol edin. Kısa bir soru gönderin ve ekrandaki cevabın seçilen sesle okunduğunu dinleyin.
