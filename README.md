# Win_Trung — Microservices Control Panel

Công cụ Windows (WinForms + WebView2) để chạy, build, cấu hình và quản lý microservices local từ một dashboard.

## Yêu cầu

- **Windows 10/11**
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (thường đã có trên Windows 11)
- **Không cần cài .NET** nếu dùng bản zip phát hành (self-contained)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) — chỉ khi build từ source
- **Redis** (`redis-server.exe`) — cấu hình `redisPath` trong Workspace Paths
- **RabbitMQ** — `localhost:5672` (nếu service cần)
- **Node.js + npm** — cho Admin Portal (Angular) và FE khác

## Cài đặt nhanh (bản exe)

1. Tải `Win_Trung-MicroservicesControlPanel.zip` từ kênh phát hành nội bộ (Releases / SharePoint / …).
2. Giải nén và chạy `DLLRunTool.exe`.
3. Tab **Workspace** → chỉnh `loyaltyRoot`, `fptcxRoot`, `redisPath` → **Lưu**.
   - Hoặc copy `paths.local.example.json` thành `paths.local.json` và sửa đường dẫn **trên máy bạn**.
4. Tab **Dịch vụ** → Run service theo thứ tự gợi ý (Redis → AuthServer → …).

## Build từ source

```powershell
cd DLLRunTool
.\publish.ps1
```

Phát hành bản mới (tự động đầu-cuối):

```powershell
.\scripts\release-full.ps1 -Version "1.2.10" -ReleaseNotes "Mo ta thay doi"
```

Hoặc từng bước:

```powershell
.\publish.ps1 -Version "1.2.10" `
  -DownloadUrl "https://github.com/hiimwin/DLLRunTool/releases/download/v1.2.10/Win_Trung-MicroservicesControlPanel.zip" `
  -ReleaseNotes "Mo ta thay doi"
.\create-release.ps1 -Version "1.2.10"
```

`release-full.ps1`: build zip → kiểm tra file nhạy cảm → loại `Co-authored-by` Cursor khỏi commit → push → tạo GitHub Release. Chạy trong PowerShell ngoài Cursor nếu commit bị gắn trailer Cursor.

Sau `publish.ps1`: commit + push `update-manifest.json` lên `main`; upload zip lên Releases (tag trùng version).

| Output | Mô tả |
|--------|--------|
| `publish\Win_Trung-MicroservicesControlPanel-build\` | Thư mục chạy trực tiếp (~50MB zip) |
| `publish\Win_Trung-MicroservicesControlPanel.zip` | Gói phân phối |

```powershell
.\publish\Win_Trung-MicroservicesControlPanel-build\DLLRunTool.exe
```

Zip release **không** chứa `paths.local.json`, `global.*.secrets.json`, `backup-*.json` (đã scrub trong `publish.ps1`).

### Code signing (SmartScreen)

Windows có thể hiện *Windows protected your PC* với exe **chưa ký**. Cách xử lý:

| Cách | Hiệu quả |
|------|----------|
| Chứng chỉ **Code Signing** (OV/EV) + `signtool` | **Tốt nhất** — giảm hoặc hết SmartScreen |
| Cùng bản đã ký, phát hành lâu dài | Tích lũy reputation (OV) |
| User bấm *More info* → *Run anyway* | Tạm thời, mỗi máy / mỗi bản mới |
| IT whitelist nội bộ | Phù hợp team |

```powershell
$env:MCCP_SIGN_PFX = "C:\certs\codesign.pfx"
$env:MCCP_SIGN_PASSWORD = "..."
.\publish.ps1 -Version "1.2.5"
```

Script `scripts\sign-publish.ps1` tự gọi sau `dotnet publish` nếu có `MCCP_SIGN_PFX`.

## Tự động cập nhật

Tool so sánh version local với `update-manifest.json` trên server cập nhật khi mở app.

1. Dev: sửa `update-check.config.json` → `manifestUrl` trỏ tới file JSON public (HTTPS).
2. Mỗi release: `publish.ps1` cập nhật manifest; push manifest; đăng zip lên Releases.

```json
{
  "version": "1.2.10",
  "releasedAt": "2026-06-23",
  "downloadUrl": "https://github.com/hiimwin/DLLRunTool/releases/download/v1.2.10/Win_Trung-MicroservicesControlPanel.zip",
  "releaseNotes": "Mo ta ngan ve ban moi"
}
```

Manifest mẫu: [update-manifest.json](update-manifest.json) trên repo [hiimwin/DLLRunTool](https://github.com/hiimwin/DLLRunTool).

Người dùng bản cũ thấy banner **Có bản cập nhật mới** → **Cập nhật ngay** tải zip, giữ `paths.local.json`, global config, backups.

## Tính năng chính

### Services Dashboard

- **Run / Stop / Restart / Build** từng service
- BE: chạy từ **thư mục project** (`dotnet bin\Debug\netX.0\{Service}.dll`) — ContentRoot đúng cho ABP Development, **không cần sửa code** microservice
- **Clean bin/obj** (thùng rác ngoài cùng): xóa `bin` + `obj` từng service; tùy chọn xóa trước mỗi lần Build (tab Workspace)
- **Redis**: log stream vào console tool (không mở CMD riêng khi Run)
- **Khóa service** (ổ khóa): không bị Stop All / thoát app dừng nhầm — lưu `service-locks.json`
- **Mở folder / CMD** tại thư mục project từng dòng; **Restart** nằm nhóm icon bên phải
- Ghi chú tùy chọn trên dòng service (`Notes` trong `services*.json`)

### Console log

- Hiển thị trên **mọi tab** (kéo giãn chiều cao)
- Lọc theo service, tìm trong log, copy log
- Màu **đỏ** (error), **vàng** (warning)
- **CMD tất cả** / **CMD đang chọn**: mirror log ra cửa sổ PowerShell (tùy chọn)

### Health check (HTTP)

Chỉ áp dụng service **có URL** và **không phải exe** (Redis không check).

| Giai đoạn | Hiển thị |
|-----------|----------|
| 0–30s sau Run | Đang khởi động (chưa check) |
| ~30s, ~90s, ~210s | Đang kiểm tra / thử lại (1/3, 2/3, 3/3) |
| OK | Health OK |
| Lỗi sau 3 lần | Health lỗi |
| Không có `/health` | Không có endpoint health — chỉ theo process |

Thử lần lượt: `HealthPath` (nếu khai báo) → `/health` → `/health-status` → URL gốc.

**Tắt health** cho service không có endpoint:

```json
"EnableHealthCheck": false
```

**Tùy chỉnh path:**

```json
"HealthPath": "health-status"
```

### Workspace Paths

Placeholder `{{loyaltyRoot}}`, `{{fptcxRoot}}`, `{{redisPath}}` trong `services*.json` — mỗi dev trỏ tới thư mục gốc **trên máy mình**, không hard-code trong repo tool.

**Tùy chọn chạy service** (cùng tab Workspace):

- **Biến môi trường RUN** — lưu `run-settings.json`; mặc định `ASPNETCORE_ENVIRONMENT=Development` (inject khi start process, không dùng `dotnet run`)
- **Xóa bin/obj trước mỗi lần Build** — checkbox tùy chọn

### Global Configuration

- **BE**: `App.SelfUrl` / host / scheme → `appsettings.json` (source); connection string ghi vào source hoặc merge từ backup
- Connection string chung trong tool → `global.{platform}.be.json` (+ file secrets local, gitignored)
- Bỏ qua Redis và service không có appsettings
- **FE**: biến `env.js`

### Export / Import backup

- Backup/restore `appsettings`, `ocelot`, `env.js` vào **source project trên máy local**
- **Quét từ Source** → lưu snapshot đầy đủ vào `defaults/local.{platform}.json`
- **Export Backup** → file `backup-*.json` giữ **đủ** nội dung appsettings (ConnectionStrings, LicenseSettings, StringEncryption, …) để restore round-trip trên cùng máy
- **Apply Import** → merge vào source: chỉ ghi đè section có trong backup; `ConnectionStrings` merge **từng key**; không Unicode-escape `PublicKey` (`+` giữ nguyên)
- Preview **dry-run** trước khi apply
- File `appsettings.secrets.json` **không** đưa vào gói backup (merge riêng nếu có)
- Banner cảnh báo: file backup **có thể chứa password** — **không share** ra ngoài / không commit

**Luồng gợi ý:** Quét từ Source → Export (hoặc dùng Apply Local) → chỉnh config trong tool → Apply Import để khôi phục/đồng bộ source.

## File cấu hình (cạnh exe)

| File | Mục đích |
|------|----------|
| `paths.local.json` | Đường dẫn workspace (local) |
| `services.loyalty.json` | Danh sách service platform Loyalty |
| `services.json` | Danh sách service platform khác |
| `global.{platform}.be.json` | Global host/scheme |
| `global.{platform}.be.secrets.json` | Connection string (local, gitignored) |
| `run-settings.json` | CMD mirror, graceful stop, **env RUN**, clean bin trước build |
| `ui-state.json` | Tab/filter đã chọn |
| `service-locks.json` | Service đang khóa |
| `backups\backup-*.json` | Export local — **không commit / không share** (có thể chứa password) |
| `defaults\local.*.json` | Snapshot quét từ source — local, gitignored |

### Trường hữu ích trong `services*.json`

| Trường | Mô tả |
|--------|--------|
| `Url` | URL local — bắt buộc để health check |
| `EnableHealthCheck` | `false` = không poll HTTP health |
| `HealthPath` | Path health tùy chỉnh (mặc định thử `/health`) |
| `RunProtected` | `true` + khóa = phải mở khóa mới Run (DbMigrator) |
| `Notes` | Ghi chú hiển thị trên dashboard |

### Danh sách service Loyalty (port chính)

| Service | Port |
|---------|------|
| Redis | (exe) |
| AuthServer | 44322 |
| WebGateway | 44325 |
| PublicWebGateway | 44353 |
| Identity | 44388 |
| Saas | 44381 |
| Administration | 44367 |
| MasterData | 44364 |
| Member | 44371 |
| Transaction | 44368 |
| SyncData | 44370 |
| CustomerJourney | 44363 |
| Product | 44361 |
| Segment | 44366 |
| Gdpr | 44362 |
| *Job hosts* | 44365–44375 |
| DbMigrator | (1 lần, RunProtected) |
| Admin Portal FE | 4200 |

Thứ tự gợi ý: **Redis → AuthServer → Identity → Saas → microservices → Gateway → FE**.

## Bảo mật

| Loại file | Ghi chú |
|-----------|---------|
| `appsettings.json` trong repo microservice | URL/CORS/Redis; connection string tùy team — **không commit password lên repo công khai** |
| `appsettings.secrets.json`, `global.*.secrets.json` | **Không commit / không share** |
| `backup-*.json`, `defaults/local.*.json`, `paths.local.json` | **Không commit / không share** — backup giữ full config cho restore máy local |

Repo tool chỉ chứa source công cụ — không đường dẫn máy dev, không password.

## Lưu ý dev local

- **Docker infrastructure** trước khi RUN microservices: `rabbitmq` + `redis` (`docker start rabbitmq redis` hoặc `docker-compose` trong `loyalty-platform/etc/docker`)
- **AuthServer / Identity** cần RabbitMQ (`localhost:5672`) và Redis (`localhost:6379`)
- **AuthServer** có thể dùng DB khác — kiểm tra sau apply global connection string
- Sau đổi config: **Build** (nếu cần) → **Run**; tool sync appsettings/ocelot từ source → `bin`
- Gateway runtime đọc **`ocelot.json`**

## Cấu trúc project tool

```
DLLRunTool/
├── DLLRunTool/           # C# + wwwroot UI
├── publish.ps1
├── release.bat           # Chạy release-full.ps1 (PowerShell ngoài Cursor)
├── scripts/
│   ├── release-full.ps1  # Build + push + GitHub Release
│   └── rewrite-unpushed-clean.ps1
├── update-manifest.json  # Push lên server cap nhat (khong nam trong zip)
├── README.md
├── README-user.md        # Huong dan ngan trong zip
└── publish/
```

## Tác giả

**Win_Trung**
