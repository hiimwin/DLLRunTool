# Win_Trung — Microservices Control Panel

Tool chạy / dừng / build microservices local (Back-End + Front-End).

## Lần đầu dùng

1. Giải nén zip vào thư mục cố định (ví dụ `C:\Tools\MCP`).
2. Mở **DLLRunTool.exe**.
3. Tab **Workspace** → cấu hình thư mục gốc (`loyaltyRoot`, `redisPath`, …) → **Lưu**.
4. Tab **Dịch vụ** → Run từng service theo thứ tự gợi ý.

## Auto-update

Tool tự kiểm tra bản mới khi mở. Nhấn **Cập nhật ngay** để tải và áp dụng — giữ nguyên cấu hình local (`paths.local.json`, backup, global config).

## File local (không xóa khi update)

| File | Mục đích |
|------|----------|
| `paths.local.json` | Đường dẫn workspace trên máy bạn |
| `global.*.json` | Cấu hình chung đã lưu trong tool |
| `global.*.secrets.json` | DB password (không commit) |
| `backups/` | Export config |

## Ghi chú

- `services.loyalty.json` chỉ mô tả **cấu trúc thư mục** dự án — mỗi máy trỏ tới repo của chính bạn qua Workspace Paths.
- Không commit / chia sẻ file chứa password.
