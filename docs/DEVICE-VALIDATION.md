# 8DD2 / F.15 — ilk cihaz doğrulaması

14 Ekim 2026, Windows 11, Ryzen AI 7 350 + RTX 5060 Laptop GPU. Bu belge ilk alpha'nın kanıt sınırını kaydeder; uzun süreli kararlılık veya bütün OMEN algoritmalarına eşdeğerlik raporu değildir.

## Geçen kontroller

- .NET 10 Release derlemesi: sıfır uyarı ve hata.
- 23 otomatik kontrol: CPU sayaç hesabı; eksik/hatalı BIOS yanıtını başarı saymama; salt okunur komut listesinde yazım olmaması; bilinmeyen/sayısal IPC komutlarının ve aşırı uzun mesajların reddi; Eco'nun mevcut düşük FPS sınırını koruması.
- Normal kullanıcı → erişimi Windows ACL ile sınırlandırılmış hizmet → HP WMI bağlantısı.
- Cihazın sistem tasarım yanıtı V0 termal politika bildiriyor; iki fan mevcut.
- Default, Quiet, Performance ve Eco komutları kendi kodumuzdan gönderildi. Fan durumu ve GPU güç politikası geri okundu. Performans profilinin doğrudan getter'ı olmadığı için bu, profil adının donanımdan teyidi olarak sunulmaz.
- Default / Quiet / Eco GPU politikası `00010157`; Performance `01010157`.
- Max → Auto geçişi kendi kodumuzla geçti. Kullanıcı fanların yükselip tekrar düştüğünü ayrıca doğruladı.
- Arayüzün kendi Eco işleyicisi ekranı 144 → 60 Hz, NVIDIA FPS sınırını 0 → 60 yaptı. Default'a dönüşte 144 Hz ve önceki 0 FPS sınırı geri geldi; NVAPI ayarının override durumu da aynı kaldı.
- Arayüz tam çıkışında Auto'ya dönüş, FPS/ekran geri yüklemesi ve demand-start hizmetin durması görüldü.
- Gerçek kaldırma/yeniden kurma testi geçti: Default+Auto geri dönüşünden sonra yalnızca `Program Files/VictusHub` ve kendi hizmet/kısayol/kaldırma kaydı kaldırıldı; paket yeniden kuruldu ve hizmet tekrar çalıştı.
- RAM, CPU kullanımı ve RTX kullanım/sıcaklık/güç ölçümleri mevcut Windows/NVIDIA arayüzlerinden alındı.

Yerel ham sonuçlar (kişisel cihazda tutulur, Git'e eklenmez): `local-diagnostics/control-smoke.jsonl`, `ui-smoke/ui-smoke.json`, `lifecycle.json`, kaynak tüketimi JSON dosyaları. OEM ile manuel karşılaştırmanın ham WMI kayıtları da aynı klasördedir. Teknik protokol kaynakları için PROTOCOL-EVIDENCE.md'ye bakın.

## Kapsam dışında kalan doğrulamalar

- Pilden profil/fan yazımı: henüz etkin değil. Adaptör kontrolü hem arayüzde hem hizmette bulunur.
- CPU sıcaklığı/paket gücü sürücüsüz doğrulanamadı. Sonradan kullanıcı izniyle resmî imzalı PawnIO 2.2.0 kuruldu: LibreHardwareMonitor 0.9.6, yönetici yetkisinde yaklaşık 48.4 °C / 15 W okudu. Normal kullanıcı erişimi 0 döndürdüğü için ayrıcalıklı hizmet üzerinden okunur; HP termal sorgusunun sıfırı kullanılmaz.
- Uzun oyun/yük karşılaştırması, OMEN'in dinamik CPU/GPU paylaşım algoritması, uyku/uyanma ve ani hizmet çöküşü sonrası firmware davranışı.
- Gerçek yeniden başlatma/oturum açma testi; Windows başlangıç seçeneği kullanıcı tercihi olarak kapalı bırakıldı.
- OMEN Gaming Hub'ın kaldırılmış olduğu senaryo. Karşılaştırma sırasında OMEN'in kullanıcı kontrol arka planı kapatıldı; HP HSA hizmetleri/paketleri kaldırılmadı.

## Kaynak tüketimi

Sensör destekli paket self-contained .NET/Windows Desktop çalışma zamanıyla yaklaşık 176.5 MiB; geliştirme SDK'sı ayrı tutulur ve kurulan uygulamanın parçası değildir. Süreç belleği/CPU ölçümleri, ölçümün pencere/tepsi durumuyla birlikte yerel JSON dosyalarına kaydedilir. Paket boyutu RAM maliyeti olarak sunulmaz.

## 14 Ekim: sensör desteği güncellemesi

- Kullanıcı, CPU sensörleri için gerekirse imzalı PawnIO kurulmasına izin verdi. Resmî 2.2.0 kurulum dosyasının Authenticode durumu Valid, imzalayan namazso.eu; SHA256 `1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032`.
- LibreHardwareMonitorLib 0.9.6, yalnızca CPU ve GPU grupları etkinleştirilerek kullanıldı. Anakart/EC, disk, SPD ve fan kontrol grupları etkinleştirilmedi; sensör Control.SetValue çağrısı yok.
- Hizmetin Telemetry isteğiyle CPU kullanım/sıcaklık/paket gücü ve iki GPU okundu. İki ardışık örnek: CPU 64.9 → 62.6 °C, 19.1 → 17.9 W; RTX 51.2 → 51.6 °C, 13.8 → 16.7 W. Bunlar doğrulama anının örnekleridir, genel çalışma sıcaklığı değildir.
- BIOS durum sorgusunu beklemek, arayüz sensör yenilemesini artık bloke etmez. Sensörler ayrı bir salt okunur hizmet isteğiyle alınır; eski ölçümler 8 saniyeden sonra gösterilmez.
- Gerçek WPF ekran görüntüsünde CPU/GPU alanları dolu görüldü; kullanıcı da değerlerin görünmesini ve güncellenmesini doğruladı. Önceki 23 kontrol yeniden geçti.
- Pencere açık, sensörler etkin, başlangıç tamamlandıktan sonraki 20 saniyelik ölçüm: uygulama+hizmet yaklaşık **134.2 MiB private working set**, 16 mantıksal işlemciye normalize ortalama **%0.156 CPU**. Tek kısa ölçümdür; uzun süreli tüketim garantisi değildir. Önceki sürümün soğuk tepsi ölçümüyle doğrudan kıyaslanmaz.
- Yerel kanıtlar: `sensors-after-admin.json`, `service-sensors-confirmed.json`, `resources-sensors-steady.json`; arayüz görseli `artifacts/victus-hub-sensors.png`.
