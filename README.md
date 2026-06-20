# Win_Trung — Microservices Control Panel

Công cụ Windows (WinForms + WebView2) để chạy, build, cấu hình và quản lý microservices **LoyaltyPlatform** và **FPTCXSuite** từ một dashboard.

## Yêu cầu

- **Windows 10/11**
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (thường đã có trên Windows 11)
- **Không cần cài .NET** nếu dùng bản zip/exe từ `publish.ps1` (self-contained)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) — chỉ khi build từ source
- **Redis** (`redis-server.exe`) — cấu hình đường dẫn trong Workspace Paths
- **RabbitMQ** — `localhost:5672` (nếu service cần)
- **Node.js + npm** — cho Admin Portal (Angular) và FE khác

## Cài đặt nhanh (bản exe)

1. Giải nén `Win_Trung-MicroservicesControlPanel.zip` vào thư mục bất kỳ.
2. Chạy `DLLRunTool.exe`.
3. Vào **Workspace Paths** → chỉnh `loyaltyRoot`, `fptcxRoot`, `redisPath` → **Lưu**.
   - Hoặc copy `paths.local.example.json` thành `paths.local.json` và sửa đường dẫn.
4. Lần đầu mở: tool tự quét **local defaults** từ source project.

## Build từ source

```powershell
cd DLLRunTool
.\publish.ps1
```

Tăng version khi phát hành bản mới:

```powershell
.\publish.ps1 -Version "1.2.0" -DownloadUrl "https://link-to-your-zip" -ReleaseNotes "Mô tả thay đổi"
```

## Tự động thông báo cập nhật (cho người dùng tool)

Khi mở tool, app so sánh **version local** với file `update-manifest.json` trên server.

### Cấu hình một lần (người phát hành — Win_Trung)

1. Sửa `DLLRunTool/update-check.config.json` — đặt `manifestUrl` trỏ tới file JSON public (GitHub raw, SharePoint, …):

```json
{
  "manifestUrl": "https://raw.githubusercontent.com/hiimwin/DLLRunTool/main/update-manifest.json"
}
```

2. Mỗi lần `publish.ps1`, file `update-manifest.json` (ở thư mục gốc repo) được cập nhật version.
3. **Commit + push** `update-manifest.json` lên [hiimwin/DLLRunTool](https://github.com/hiimwin/DLLRunTool).
4. Điền `downloadUrl` trong manifest = link tải zip (khuyên dùng [GitHub Releases](https://github.com/hiimwin/DLLRunTool/releases)):

```powershell
.\publish.ps1 -Version "1.1.0" -DownloadUrl "https://github.com/hiimwin/DLLRunTool/releases/download/v1.1.0/Win_Trung-MicroservicesControlPanel.zip" -ReleaseNotes "..."
```

### Manifest mẫu (`update-manifest.json`)

```json
{
  "version": "1.2.0",
  "releasedAt": "2026-06-17",
  "downloadUrl": "https://.../Win_Trung-MicroservicesControlPanel.zip",
  "releaseNotes": "Sửa lỗi, thêm tính năng X"
}
```

Người dùng bản cũ sẽ thấy banner **Có bản cập nhật mới** khi mở tool (cần internet). Nhấn **Cập nhật ngay** → tool **tự tải zip** từ `downloadUrl`, giải nén, ghi đè file exe (giữ `paths.local.json`, global config, backups), rồi **tự mở lại** — không cần tải tay trên trình duyệt.

Kết quả:

| Output | Mô tả |
|--------|--------|
| `publish\Win_Trung-MicroservicesControlPanel-build\` | Thư mục chạy trực tiếp (đã gói .NET 10, ~150MB) |
| `publish\Win_Trung-MicroservicesControlPanel.zip` | Gói zip phân phối — giải nén và chạy `DLLRunTool.exe` |

Chạy thử sau build:

```powershell
.\publish\Win_Trung-MicroservicesControlPanel-build\DLLRunTool.exe
```

## Tính năng chính

### Services Dashboard

- **Run / Stop / Restart / Build** từng microservice
- Lệnh chạy BE: `dotnet {Service}.dll --urls "https://localhost:PORT"` (tự tìm `bin\Debug\netX.0` theo `.csproj`)
- **Console Log** theo service, lọc log, **Stop All**
- **Khóa service** (icon ổ khóa): service khóa không bị dừng khi **Stop All** hoặc thoát tool (chọn *Có* dừng service). Trạng thái lưu trong `service-locks.json`
- Toggle **Console riêng** — mở cửa sổ console riêng cho từng process

### Workspace Paths

Đường dẫn gốc tới repo, thay thế placeholder `{{loyaltyRoot}}`, `{{fptcxRoot}}`, `{{redisPath}}` trong `services*.json`.

### Global Configuration

- **BE**: `App.SelfUrl` / host / scheme → ghi vào `appsettings.json` (source); **connection string** → `appsettings.secrets.json` (không commit)
- Connection string chung trong tool → `global.{platform}.be.secrets.json` (cạnh exe, gitignored)
- Bỏ qua Redis và service không có `appsettings`
- **FE**: biến `env.js` (base_url, api_url, …)

### Export / Import

- Backup/restore cấu hình (`appsettings`, `ocelot`, `env.js`, …) vào **source project**
- `appsettings.json` trong backup **không chứa** ConnectionStrings / StringEncryption
- Secrets nằm trong `appsettings.secrets.json` (file local, không đưa lên git)
- **Không share** file `backup-*.json` — có thể chứa password DB

## File cấu hình (cạnh exe)

| File | Mục đích |
|------|----------|
| `paths.local.json` | Đường dẫn workspace |
| `services.loyalty.json` | Danh sách service Loyalty (**đủ sẵn** — xem bảng dưới) |
| `services.json` | Danh sách service FPTCXSuite |
| `global.{platform}.be.json` | Global host/scheme (không có password) |
| `global.{platform}.be.secrets.json` | Connection string chung (local only) |
| `backups\backup-*.json` | Export backup — **không commit / không share** |

### `services.loyalty.json` — danh sách mặc định (Loyalty local)

Mở tool lần đầu chỉ cần cấu hình **Workspace Paths** (`loyaltyRoot`, `redisPath`). Toàn bộ service dưới đây đã có sẵn; muốn thêm gateway khách hàng (FMV, Metro, …) thì tự thêm dòng vào file.

| Service | Port |
|---------|------|
| Redis | (exe) |
| AuthServer | 44322 |
| Identity | 44388 |
| Saas | 44381 |
| Administration | 44367 |
| Product | 44361 |
| Segment | 44366 |
| MasterData | 44364 |
| Member | 44371 |
| Transaction | 44368 |
| SyncData | 44370 |
| CustomerJourney | 44363 |
| Gdpr | 44362 |
| MasterData Job | 44365 |
| Member Job | 44372 |
| CustomerJourney Job | 44373 |
| Transaction Job | 44374 |
| SyncData Job | 44375 |
| WebGateway | 44325 |
| PublicWebGateway | 44353 |
| DbMigrator | (chạy 1 lần, không port) |
| Admin Portal FE | 4200 |

Thứ tự chạy gợi ý: Redis → AuthServer → Identity → Saas → các microservice → WebGateway → FE. Job host / DbMigrator khi cần.

## Bảo mật & Git

### Nguyên tắc

| Vị trí | Commit git? |
|--------|-------------|
| `loyalty-platform/**/appsettings.json` | Có — chỉ URL/CORS/Redis |
| `loyalty-platform/**/appsettings.secrets.json` | **Không** |
| `backup-*.json`, `global.*.secrets.json` | **Không** |

Nếu DB password từng lọt vào git: **đổi password DB** (rotate credential). Xóa commit trên GitHub/GitLab **không** thu hồi được secret đã lộ — chỉ giảm rủi ro scan sau này.

### Repo DLLRunTool trên GitHub (`hiimwin/DLLRunTool`)

Repo chỉ chứa source tool — **không** commit `backup-*.json`, `paths.local.json`, hay connection string.

### Tạo bản copy sạch (tùy chọn)

```powershell
cd D:\Codes\LoyaltyPlatform\DLLRunTool
.\scripts\init-clean-repo.ps1 -InitGit
# Hoặc chỉ copy, không git init:
.\scripts\init-clean-repo.ps1 -TargetDir D:\Repos\DLLRunTool-mine

cd D:\Repos\DLLRunTool-mine   # hoặc thư mục -TargetDir
git commit -m "Initial commit: DLLRunTool v1.2.0"
git remote add origin https://github.com/<tai-khoan-cua-ban>/<repo-moi>.git
git push -u origin main
```

### Tránh `Co-authored-by: Cursor` khi commit

Trong Cursor: **Settings → Agents → Attribution** — tắt thêm co-author vào commit. Hoặc commit/push bằng terminal/Git GUI của bạn.

### Xóa history repo cũ (nếu vẫn muốn dùng cùng URL)

**Cách 1 — Xóa repo trên GitHub → tạo lại** (đơn giản nhất): Settings → Delete repository → push repo mới.

**Cách 2 — Orphan branch** (xóa toàn bộ history, giữ tên branch `main`):

```powershell
git checkout --orphan fresh-start
git add -A
git commit -m "Initial commit"
git branch -D main
git branch -m main
git push -f origin main
```

**Cách 3 — `git filter-repo`** (gỡ file/path cụ thể khỏi mọi commit): cần cài [git-filter-repo](https://github.com/newren/git-filter-repo), dùng khi chỉ muốn xóa vài file secrets khỏi history.

### loyalty-platform (GitLab)

Các thay đổi `appsettings` local của bạn hiện **chưa commit** (`git status` = modified). **Đừng commit** nếu còn password trong `appsettings.json`. Dùng `local-dev\apply-secrets.ps1` + `.gitignore` đã thêm `appsettings.secrets.json`.

## Lưu ý khi dev local

- **AuthServer** thường dùng DB khác (`nextgen-masterdata-svc`) — kiểm tra sau khi apply global connection string
- **Gdpr** có nhiều connection string; global chỉ đổi các key có trong `appsettings`
- Sau đổi config: **Build** (nếu cần) → **Run**; tool sync `appsettings`/`ocelot` từ source → `bin`
- Gateway runtime đọc **`ocelot.json`** (không phải `ocelot.localhost.json`)

## Cấu trúc project

```
DLLRunTool/
├── DLLRunTool/           # Source C# + wwwroot UI
├── publish.ps1           # Build & zip
├── README.md
└── publish/
    ├── Win_Trung-MicroservicesControlPanel-build/
    └── Win_Trung-MicroservicesControlPanel.zip
```

## Tác giả

**Win_Trung**
