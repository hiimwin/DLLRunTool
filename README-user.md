# Win_Trung — Microservices Control Panel

Tool chạy / dừng / build microservices local (Back-End + Front-End).

## Lần đầu dùng

1. Giải nén zip vào thư mục cố định (ví dụ `C:\Tools\MCP`).
2. Mở **DLLRunTool.exe**.
3. Tab **Workspace** → cấu hình `loyaltyRoot`, `redisPath`, … → **Lưu**.
4. Tab **Dịch vụ** → Run theo thứ tự: Redis → AuthServer → các BE → Gateway → FE.

## Console log

- Log hiển thị dưới cùng mọi tab; lọc theo service, tìm/copy log.
- **CMD tất cả** / **CMD đang chọn**: mở cửa sổ mirror log (tùy chọn).
- Redis: log nằm trong tool, không mở CMD riêng.

## Health check

- Service có URL: sau ~30s tool tự thử `/health` (tối đa 3 lần: ~30s, ~90s, ~210s).
- Đang chờ / thử lại: LED vàng hoặc tím + dòng trạng thái (vd. *Health chưa OK — thử lại 1/3*).
- Redis và exe: không check health.
- Service không có endpoint health: hiện *Không có endpoint health* — process vẫn chạy bình thường.

## Auto-update

Tool tự kiểm tra bản mới khi mở. **Cập nhật ngay** → tải và áp dụng, giữ cấu hình local.

## Cảnh báo "Windows protected your PC" (SmartScreen)

Lần đầu chạy `DLLRunTool.exe`, Windows có thể chặn vì file **chưa có chữ ký số** từ nhà phát hành (bình thường với app nội bộ / chưa ký CA).

**Người dùng — chạy tạm:**
1. Hộp thoại SmartScreen → **More info** / **Thông tin thêm**
2. **Run anyway** / **Vẫn chạy**

**Người phát hành — giảm / hết cảnh báo (khuyên dùng):**
- Mua **chứng chỉ Code Signing** (OV hoặc EV) từ CA (DigiCert, Sectigo, …)
- Ký `DLLRunTool.exe` khi build:

```powershell
$env:MCCP_SIGN_PFX = "C:\path\to\codesign.pfx"
$env:MCCP_SIGN_PASSWORD = "..."
.\publish.ps1 -Version "1.2.5"
```

- **EV certificate**: SmartScreen thường hết chặn ngay sau vài lần phát hành
- **OV certificate**: cần tích lũy reputation (nhiều máy tải cùng bản đã ký)
- **Nội bộ công ty**: IT whitelist qua GPO / Defender Application Control

Không có cách tắt SmartScreen 100% bằng code nếu **không ký** exe — đó là cơ chế bảo vệ của Windows.

## File local (không xóa khi update)

| File | Mục đích |
|------|----------|
| `paths.local.json` | Đường dẫn workspace |
| `global.*.json` | Cấu hình chung |
| `global.*.secrets.json` | DB password |
| `backups/` | Export config |

## Ghi chú

- `services.loyalty.json` mô tả cấu trúc service — mỗi máy trỏ repo qua Workspace Paths.
- Không chia sẻ file có password.
