# 12. 部署指南：Ubuntu 24.04 + SQL Server

本文件是把 NtmcScheduler 部署到公司內網 Ubuntu 24.04 VM 的完整步驟，資料庫使用外部 Microsoft SQL Server，
對外只有 IP、沒有網域，且必須走 HTTPS。應用程式不需要修改任何程式碼，全部由環境變數與設定驅動。

架構是 Kestrel 直接對外：`NtmcScheduler.Web` 本身內建網頁伺服器，自己持有憑證處理 HTTPS，
由 systemd 負責開機自啟與異常重啟，不使用 nginx／IIS 反向代理。

以下指令中的預留值請自行替換：

| 預留值 | 意義 | 範例 |
|---|---|---|
| `<APP_IP>` | VM 對使用者的 IP | `10.20.30.40` |
| `<SQL_HOST>` | SQL Server 主機名或 IP | `10.20.30.50` |
| `<DB_NAME>` | 公司已建立的資料庫名稱 | `NtmcScheduler` |
| `<DB_USER>` | 公司提供的 SQL Server Login | `App_SYSchedule` |
| `<DB_PASSWORD>` | 公司提供的資料庫帳號密碼 | — |
| `<PFX_PASSWORD>` | 伺服器憑證密碼 | — |
| `<DPKEY_PASSWORD>` | Data Protection 憑證密碼 | — |

## 0. 事前準備清單

開始之前先確認這些東西都到位，否則會卡在中途：

- Ubuntu 24.04 LTS VM，具 sudo 權限。
- SQL Server 連線資訊，且 VM 到 SQL Server 的 TCP 1433 已開通。
- 本系統只在公司內網使用，網路存取限制由公司既有 Firewall／ACL 管理；VM 本身不另外啟用 UFW。
- 準備一張 **含 IP SAN** 的自簽 HTTPS 伺服器憑證（見第 4 節），並讓所有使用者電腦信任該憑證。
  本系統是 Blazor Interactive Server，靠 WebSocket（`wss:`）維持連線；瀏覽器若不信任憑證，WSS 會被封鎖，畫面可能載入後完全沒有反應。

## 1. 安裝 .NET 10

專案目標框架是 `net10.0`。若在這台 VM 上直接建置，安裝 SDK；若只執行由別台機器 publish 出來的檔案，
安裝 ASP.NET Core Runtime 即可。

```bash
sudo apt update
sudo apt install -y dotnet-sdk-10.0          # 建置用
# 或只要執行：sudo apt install -y aspnetcore-runtime-10.0
dotnet --list-sdks
```

Ubuntu 24.04 的 .NET 10 套件由 Ubuntu 套件來源提供；若 `apt` 找不到套件，先檢查 VM 的 Ubuntu 套件來源設定並重新執行 `sudo apt update`，不要另外混用 Microsoft 的 Ubuntu 套件庫。

OR-Tools 是 native library，需要 C++ 執行期與 ICU。dotnet 套件通常會一併帶入，仍建議明確確認：

```bash
sudo apt install -y libstdc++6 libicu74
```

程式所有時間計算都以固定的 UTC+8 偏移量處理，**不依賴系統時區**，所以不設定 VM 時區也不會算錯班表。
但為了讓 log 與維運人員的認知一致，建議還是設好：

```bash
sudo timedatectl set-timezone Asia/Taipei
```

## 2. 準備執行帳號與目錄

不要用 root 執行應用程式。VM 上已有一般使用者 `ntmsy-schedule` 與既有家目錄時，**不要再執行 `useradd`**，也不要改家目錄或 shell。

仍須另外建立應用程式目錄。`/var/lib/ntmsy-schedule` 只放憑證與 Data Protection key ring，不是使用者的 home；systemd unit 設了 `ProtectHome=true`，服務程序讀不到 `/home`，所以 key 與憑證不能放在家目錄裡。

```bash
sudo mkdir -p /opt/ntmsy-schedule /var/lib/ntmsy-schedule/keys /etc/ntmsy-schedule
sudo chown root:root /opt/ntmsy-schedule
sudo chmod 755 /opt/ntmsy-schedule
sudo chown -R ntmsy-schedule:ntmsy-schedule /var/lib/ntmsy-schedule
sudo chmod 700 /var/lib/ntmsy-schedule/keys
sudo chown root:ntmsy-schedule /etc/ntmsy-schedule
sudo chmod 750 /etc/ntmsy-schedule
```

| 路徑 | 用途 |
|---|---|
| 既有家目錄（例如 `/home/ntmsy-schedule`） | 登入與維運用，應用程式不寫入 |
| `/opt/ntmsy-schedule` | publish 出來的應用程式檔案 |
| `/var/lib/ntmsy-schedule/keys` | Data Protection key ring，**必須持久保存** |
| `/var/lib/ntmsy-schedule/*.pfx` | 憑證 |
| `/etc/ntmsy-schedule/ntmsy-schedule.env` | 環境變數，含密碼，權限 640 |

## 3. 確認公司提供的 SQL Server 資料庫與帳號

資料庫與 SQL Server Login 已由公司／DBA 建立，**部署時不要自行執行 `CREATE DATABASE`、`CREATE LOGIN` 或修改伺服器層級帳號設定**。

部署前向 DBA 確認以下資訊：

- SQL Server 主機名或 IP：`<SQL_HOST>`
- 資料庫名稱：`<DB_NAME>`
- Login：`<DB_USER>`
- 密碼：`<DB_PASSWORD>`
- `<DB_USER>` 已映射到 `<DB_NAME>`，且可正常登入及讀寫該資料庫。

應用程式目前在啟動時會自動執行 EF Core migration，因此若採用自動 migration，DBA 還必須提供建立／修改資料表等 schema 變更所需權限。**不要由部署人員自行提升資料庫權限**；實際權限由公司 DBA 依政策設定。

若公司不允許應用程式帳號具有 schema 變更權限，改用第 10 節「由 DBA 手動套用 schema」流程，應用程式帳號只保留正式執行所需的資料讀寫權限。

從 VM 先確認 SQL Server 網路可通：

```bash
sudo apt install -y netcat-openbsd
nc -zv <SQL_HOST> 1433
```

## 4. 準備兩張憑證

**這是兩張不同用途的憑證，不要共用。**

### 4a. 伺服器憑證（HTTPS，自簽且需 IP SAN）

本系統只在公司內網使用，直接在 VM 上建立含 IP SAN 的自簽伺服器憑證：

```bash
sudo openssl req -x509 -newkey rsa:2048 -nodes -days 3650 \
  -keyout /var/lib/ntmsy-schedule/server.key \
  -out /var/lib/ntmsy-schedule/server.crt \
  -subj "/CN=<APP_IP>" \
  -addext "subjectAltName=IP:<APP_IP>" \
  -addext "extendedKeyUsage=serverAuth"
```

再合併成 Kestrel 使用的 PKCS#12。不要把密碼直接寫在 command line；OpenSSL 會在終端機提示輸入匯出密碼，該密碼就是後續的 `<PFX_PASSWORD>`：

```bash
sudo openssl pkcs12 -export \
  -inkey /var/lib/ntmsy-schedule/server.key \
  -in /var/lib/ntmsy-schedule/server.crt \
  -out /var/lib/ntmsy-schedule/server.pfx
```

將 `server.crt` 匯入所有使用者電腦的受信任憑證存放區；若公司有 GPO，可由 GPO 集中派送。`subjectAltName` 必須是 `IP:`，不是 `DNS:`；使用者也必須以憑證中的 `<APP_IP>` 連線。

### 4b. Data Protection 憑證（加密硬碟上的 key ring）

Production 環境沒有這張憑證會**拒絕啟動**。它不對外，不需要 IP SAN，可自簽並設長效期。第二個 `openssl pkcs12 -export` 會提示輸入匯出密碼，該密碼就是後續的 `<DPKEY_PASSWORD>`：

```bash
sudo openssl req -x509 -newkey rsa:2048 -nodes -days 3650 \
  -subj "/CN=NtmcScheduler Data Protection" \
  -keyout /tmp/dp.key -out /tmp/dp.crt
sudo openssl pkcs12 -export -inkey /tmp/dp.key -in /tmp/dp.crt \
  -out /var/lib/ntmsy-schedule/dp.pfx
sudo rm /tmp/dp.key /tmp/dp.crt
```

設定權限：

```bash
sudo chown ntmsy-schedule:ntmsy-schedule /var/lib/ntmsy-schedule/*.pfx
sudo chmod 600 /var/lib/ntmsy-schedule/*.pfx
sudo chown root:root /var/lib/ntmsy-schedule/server.key
sudo chmod 600 /var/lib/ntmsy-schedule/server.key
```

`dp.pfx` 與 `/var/lib/ntmsy-schedule/keys` 一定要納入備份。遺失的後果是所有使用者的登入 cookie 立即失效，
必須全部重新登入。

## 5. 建置與發行

在專案原始碼目錄執行：

```bash
dotnet publish src/NtmcScheduler.Web -c Release -r linux-x64 --self-contained false -o /tmp/ntmsy-schedule-publish
sudo cp -a /tmp/ntmsy-schedule-publish/. /opt/ntmsy-schedule/
sudo chown -R root:root /opt/ntmsy-schedule
sudo find /opt/ntmsy-schedule -type d -exec chmod 755 {} +
sudo find /opt/ntmsy-schedule -type f -exec chmod 644 {} +
sudo chmod 755 /opt/ntmsy-schedule/NtmcScheduler.Web
```

指定 `-r linux-x64` 可確保 OR-Tools 的 Linux native library 被正確複製，在非 Linux 機器上建置時尤其重要。
若 VM 不允許安裝 .NET runtime，改用 `--self-contained true`，publish 產物會自帶執行期，體積較大但不需預裝任何東西。

## 6. 環境變數設定檔

建立 `/etc/ntmsy-schedule/ntmsy-schedule.env`：

```ini
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=https://0.0.0.0:443
ASPNETCORE_HTTPS_PORTS=443
AllowedHosts=<APP_IP>

DatabaseProvider=SqlServer
ConnectionStrings__Default=Server=<SQL_HOST>,1433;Database=<DB_NAME>;User Id=<DB_USER>;Password=<DB_PASSWORD>;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=False

Kestrel__Certificates__Default__Path=/var/lib/ntmsy-schedule/server.pfx
Kestrel__Certificates__Default__Password=<PFX_PASSWORD>

DataProtection__KeyPath=/var/lib/ntmsy-schedule/keys
DataProtection__CertificatePath=/var/lib/ntmsy-schedule/dp.pfx
DataProtection__CertificatePassword=<DPKEY_PASSWORD>
```

```bash
sudo chown root:ntmsy-schedule /etc/ntmsy-schedule/ntmsy-schedule.env
sudo chmod 640 /etc/ntmsy-schedule/ntmsy-schedule.env
```

幾個容易踩到的點：

- 這個檔案使用 systemd 的 `EnvironmentFile=` 語法，不要用 shell 的 `source` 方式載入。一般值可直接寫；若值需要引號，使用 systemd 支援的單引號或雙引號語法。連線字串中的分號不需額外跳脫。
- `DataProtection__KeyPath` 請用絕對路徑；相對路徑會相對於 `ContentRootPath`，隨工作目錄改變。
- 本環境的 SQL Server 使用自簽 TLS 憑證，因此連線字串刻意保留 `Encrypt=True;TrustServerCertificate=True`。流量仍會加密，但不另外驗證 SQL Server 憑證鏈；此設定只適用於目前受公司內網與 Firewall 控制的部署環境。
- **不要設定 `KnownProxies`**。本架構沒有反向代理，若信任了不存在的代理，
  `X-Forwarded-For` 就可被偽造，AuditLog 記錄的來源 IP 將不可信。

## 7. 初始化資料庫與第一位管理者

以 `ntmsy-schedule` 身分手動執行一次。這個指令會先跑 migration 建好所有資料表，再建立管理者，然後結束程序：

```bash
sudo systemd-run --unit=ntmsy-schedule-init \
  --wait --collect --pty \
  --uid=ntmsy-schedule --gid=ntmsy-schedule \
  --working-directory=/opt/ntmsy-schedule \
  --property=EnvironmentFile=/etc/ntmsy-schedule/ntmsy-schedule.env \
  /opt/ntmsy-schedule/NtmcScheduler.Web --init-admin admin
```

這裡直接讓 systemd 讀取同一份 `EnvironmentFile`，避免用 `grep | xargs | env` 展開密碼時被空白或特殊字元破壞。

終端機會提示 `Temporary password:`，輸入不會回顯。密碼規則為至少 8 字元、至少 2 種不同字元且含數字。
建立完成後首次登入會強制修改密碼。

## 8. systemd 服務

建立 `/etc/systemd/system/ntmsy-schedule.service`：

```ini
[Unit]
Description=NtmcScheduler Web
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=ntmsy-schedule
Group=ntmsy-schedule
WorkingDirectory=/opt/ntmsy-schedule
ExecStart=/opt/ntmsy-schedule/NtmcScheduler.Web
EnvironmentFile=/etc/ntmsy-schedule/ntmsy-schedule.env
Restart=always
RestartSec=5
KillSignal=SIGTERM
TimeoutStopSec=120
SyslogIdentifier=ntmsy-schedule

AmbientCapabilities=CAP_NET_BIND_SERVICE
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/var/lib/ntmsy-schedule

[Install]
WantedBy=multi-user.target
```

啟用並啟動：

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now ntmsy-schedule
sudo systemctl status ntmsy-schedule
sudo journalctl -u ntmsy-schedule -f
```

`AmbientCapabilities=CAP_NET_BIND_SERVICE` 讓非 root 的 `ntmsy-schedule` 能綁定 443。
`TimeoutStopSec=120` **不是**等待求解跑完的時間。收到 SIGTERM 時，背景 worker 會把 host 的 stopping token
傳給 solver 觸發 `CpSolver.StopSearch()`，CP-SAT 數秒內就會中止，未完成的求解在下次啟動時由 `RecoverAsync`
重新排入佇列（該次求解需從頭重跑）。實際的等待上限是 .NET Generic Host 的 `ShutdownTimeout`，預設 30 秒；
120 秒只是留給 systemd 的安全邊界，確保 systemd 不會比 host 更早送出 SIGKILL。
不要把它設成求解時限（最長可達 4 seeds × 600 秒），否則每次部署都會呆等。
`ProtectSystem=strict` 讓整個檔案系統唯讀，因此必須用 `ReadWritePaths` 明確開放 key ring 目錄。
`Type=simple` 是刻意的：程式沒有呼叫 `UseSystemd()`，不會送出 `READY=1`，改用 `Type=notify` 會讓 systemd 一直等到逾時而判定啟動失敗。

## 9. 網路限制與 Log 保留

本 VM **不另外啟用 UFW**，網路存取限制由公司既有 Firewall／ACL 管理。部署前確認：

- 使用者端只需要連入 VM 的 TCP 443。
- 不提供 TCP 80；本系統沒有 HTTP listener。
- VM 需要能連到 SQL Server 的 TCP 1433。
- 其他對 VM 的連入規則依公司既有維運政策處理。

HSTS 依 RFC 6797 對 IP 位址無效，因此本架構直接不提供 HTTP，避免從 HTTPS 降級。

驗收要求 Log 保留一年。編輯 `/etc/systemd/journald.conf`：

```ini
[Journal]
Storage=persistent
MaxRetentionSec=1year
SystemMaxUse=2G
```

```bash
sudo systemctl restart systemd-journald
```

`SystemMaxUse` 請依磁碟容量調整；容量上限會早於時間上限生效，設太小仍然存不滿一年。

## 10. 由 DBA 手動套用 schema（選用）

不希望應用程式帳號擁有建表權限時，改由 DBA 套用 SQL script：

```bash
dotnet tool restore
NTMC_MIGRATION_PROVIDER=SqlServer dotnet ef migrations script --idempotent \
  -p src/NtmcScheduler.Migrations.SqlServer -s src/NtmcScheduler.Web -o ntmc.sql
```

SQL Server migration set 從目前完整 model 的 `InitialCreate` 起始；只可直接套用至尚未包含
NtmcScheduler tables 與 `__EFMigrationsHistory` 的空資料庫。script 為 idempotent，可重複套用。
套用後可由 DBA 把 `<DB_USER>` 權限降為
`db_datareader`、`db_datawriter` 與必要的 `EXECUTE`，但仍需保留讀取 `__EFMigrationsHistory` 的權限，
因為應用程式啟動時仍會呼叫 migration 檢查。

## 11. 升級既有部署

先完成第 12 節備份，再取得新版並執行 repository 內的部署腳本：

```bash
git pull
bash rebuild_and_deploy.sh
```

部署腳本執行的等效指令如下：

```bash
dotnet publish src/NtmcScheduler.Web -c Release -r linux-x64 --self-contained false -o /tmp/ntmsy-schedule-publish
sudo systemctl stop ntmsy-schedule
sudo find /opt/ntmsy-schedule -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +
sudo cp -a /tmp/ntmsy-schedule-publish/. /opt/ntmsy-schedule/
sudo chown -R root:root /opt/ntmsy-schedule
sudo find /opt/ntmsy-schedule -type d -exec chmod 755 {} +
sudo find /opt/ntmsy-schedule -type f -exec chmod 644 {} +
sudo chmod 755 /opt/ntmsy-schedule/NtmcScheduler.Web
sudo systemctl start ntmsy-schedule
sudo journalctl -u ntmsy-schedule -n 100
```

新版若含 migration，啟動時會自動套用。升級時先清空舊 publish 內容，避免已被新版移除的 DLL 或靜態檔殘留。**升級前務必先備份資料庫**，migration 沒有自動回滾機制。

## 12. 備份

三樣東西都要備份，缺一不可：

| 對象 | 方式 | 頻率 |
|---|---|---|
| SQL Server 資料庫 | `BACKUP DATABASE <DB_NAME> TO DISK = ...`，建議由 SQL Server Agent 排程 | 每日 |
| `/var/lib/ntmsy-schedule/keys` | 檔案備份 | 每日，或納入既有持續備份 |
| `/var/lib/ntmsy-schedule/dp.pfx`、`server.pfx` | 離線保管 | 建立／更換憑證時 |

還原演練必須實際做過一次才算通過驗收：還原資料庫、Data Protection key ring 與 `dp.pfx`，並確認還原前已建立的登入 session／cookie 仍可被系統解密使用。

## 13. 疑難排解

| 症狀 | 原因與處理 |
|---|---|
| 啟動即失敗，訊息含 `Production requires DataProtection:CertificatePath` | 沒設 `DataProtection__CertificatePath`，見第 4b 節 |
| 啟動即失敗，`ConnectionStrings:Default is required` | `EnvironmentFile` 沒被讀到，檢查路徑與 640 權限 |
| 啟動即失敗，`DatabaseProvider must be Sqlite or SqlServer` | 拼字錯誤，必須剛好是 `SqlServer` |
| 資料庫連線失敗，訊息含 `SSL Provider` 或 `no protocols available` | Ubuntu 24.04 的 OpenSSL 3 預設安全等級拒絕舊版 SQL Server 的 TLS。優先請 DBA 升級 SQL Server 或更新其憑證；短期權宜可在 `/etc/ssl/openssl.cnf` 降低 `CipherString` 的 `SECLEVEL`，但這會削弱全機器的 TLS policy，須經資安同意 |
| 頁面載得出來但完全沒有反應、按鈕無效 | WebSocket 被擋。先確認使用者電腦已信任 `server.crt`，再確認憑證包含正確的 IP SAN |
| 瀏覽器顯示憑證錯誤 | 憑證的 SAN 不是 `IP:<APP_IP>`，或使用者用了與憑證不同的位址連入 |
| 無法綁定 443 | systemd unit 缺 `AmbientCapabilities=CAP_NET_BIND_SERVICE` |
| 找不到 OR-Tools native library | publish 時未指定 `-r linux-x64`，或缺 `libstdc++6` |
| AuditLog 來源 IP 都是 `127.0.0.1` 或明顯錯誤 | 誤設了 `KnownProxies`；本架構沒有反向代理，應留空 |

## 14. 上線驗收清單

- 依公司資安要求確認密碼政策；若調整目前的 8 字元、2 種不同字元且含數字規則，須同步更新程式、驗收案例與 `docs/10-decisions.md`。
- 在正式環境完成資料庫備份／還原、Data Protection key ring 還原與 journald 一年保留驗收。
- 在具備瀏覽器 runtime 的環境完成 Microsoft Playwright 端對端與基準規模互動測試。
