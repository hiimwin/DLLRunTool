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

Người dùng bản cũ sẽ thấy banner **Có bản cập nhật mới** khi mở tool (cần internet).

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
- Lệnh chạy BE: `dotnet {Service}.dll --urls "https://localhost:PORT"` (từ thư mục `bin\Debug\net6.0`)
- **Console Log** theo service, lọc log, **Stop All**
- **Khóa service** (icon ổ khóa): service khóa không bị dừng khi **Stop All** hoặc thoát tool (chọn *Có* dừng service). Trạng thái lưu trong `service-locks.json`
- Toggle **Console riêng** — mở cửa sổ console riêng cho từng process

### Workspace Paths

Đường dẫn gốc tới repo, thay thế placeholder `{{loyaltyRoot}}`, `{{fptcxRoot}}`, `{{redisPath}}` trong `services*.json`.

### Global Configuration

- **BE**: connection string + host/scheme — ghi vào **mọi key** trong `ConnectionStrings` và `App.SelfUrl` (không rewrite toàn file → giữ PublicKey/secrets)
- Bỏ qua Redis và service không có `appsettings`
- **FE**: biến `env.js` (base_url, api_url, …)

### Export / Import

- Backup/restore cấu hình (`appsettings`, `ocelot`, `env.js`, …)
- **Quét local defaults** / **Áp dụng local defaults**

## File cấu hình (cạnh exe)

| File | Mục đích |
|------|----------|
| `paths.local.json` | Đường dẫn workspace |
| `services.loyalty.json` | Danh sách service Loyalty |
| `services.json` | Danh sách service FPTCXSuite |
| `global.{platform}.be.json` | Global config BE đã lưu |
| `service-locks.json` | Service đang khóa |
| `run-settings.json` | Console riêng on/off |
| `defaults\local.*.json` | Snapshot cấu hình local mặc định |

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
