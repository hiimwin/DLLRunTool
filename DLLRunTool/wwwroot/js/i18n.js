(() => {
  const STORAGE_KEY = "mcp-lang";

  const MESSAGES = {
    vi: {
      appTitle: "Win_Trung - Microservices Control Panel",
      rail: { services: "Services", theme: "Dark / Light", lang: "Ngôn ngữ / Language" },
      versionBadge: "Phiên bản hiện tại",
      nav: { main: "Điều hướng chính", dashboard: "Dịch vụ", workspace: "Workspace", global: "Cấu hình chung", backup: "Sao lưu" },
      category: { group: "Nhóm", be: "Back-End", fe: "Front-End" },
      hint: {
        dashboardBe: "Danh sách Back-End — dotnet dll --urls / appsettings",
        dashboardFe: "Danh sách Front-End — npm start / env.js",
        globalBe: "Áp dụng host + connection string cho mọi BE",
        globalFe: "Áp dụng env.js cho mọi FE trong platform"
      },
      dashboard: {
        title: "Danh sách service",
        descBe: "Quản lý chạy / dừng / build các service Back-End",
        descFe: "Quản lý chạy / dừng / build các service Front-End",
        reload: "Tải lại",
        reloadTitle: "Tải lại danh sách từ services.loyalty.json / services.json (tự cập nhật khi lưu file)",
        hint: "Thêm dòng vào services.loyalty.json → lưu file → danh sách tự cập nhật",
        empty: "Không có service nào trong nhóm này.",
        starting: "Đang khởi động…"
      },
      workspace: {
        title: "Workspace Paths",
        desc: "Cấu hình thư mục gốc trên máy — mỗi máy chỉ cần làm 1 lần",
        save: "Lưu workspace paths",
        missingTitle: "Service không tìm thấy thư mục",
        banner: "Chưa cấu hình workspace ({count} mục) — cần thiết trước khi Run/Build trên máy này.",
        configFile: "File cấu hình: {path}",
        pathPlaceholder: "Chọn hoặc dán đường dẫn...",
        browse: "Browse...",
        openPaths: "Cấu hình đường dẫn"
      },
      global: {
        title: "Cấu hình chung",
        subtitleBe: "URL/host → appsettings.json; connection string → appsettings.json (thư mục source project)",
        subtitleFe: "Biến env.js — tự lấy key từ env.prod.js; api_url/auth_url/base_url gợi ý từ URL service BE/FE trong danh sách",
        subtitleDefault: "Áp dụng cho tất cả service trong nhóm đang chọn",
        http: "Kết nối HTTP",
        database: "Database",
        dbHint: "Ghi connection string vào appsettings.json (mọi service BE) — thư mục source",
        connString: "Connection string",
        envTitle: "Biến môi trường env.js",
        addEnv: "+ Thêm biến",
        save: "Lưu & Áp dụng tất cả",
        feHint: "Gợi ý động: {parts} (chỉ điền khi giá trị env đang trống)"
      },
      backup: {
        title: "Sao lưu & khôi phục",
        desc: "Export/Import config vào source — file có thể chứa password, không commit",
        platform: "Platform",
        be: "Back-End",
        fe: "Front-End",
        files: "File config",
        localTitle: "Local mặc định",
        localDesc: "Quét snapshot từ source (BE: appsettings, launchSettings, ocelot · FE: env.js). Apply Local khôi phục snapshot đã quét — muốn áp config đã chỉnh trong tool, dùng tab Cấu hình chung hoặc Export/Import.",
        scanned: "Đã quét: {date}",
        notScanned: "Chưa quét — sẽ tự quét lần đầu mở tool",
        filesCount: "{count} files",
        scan: "Quét từ Source",
        applyLocal: "Apply Local",
        exportTitle: "Export / Import",
        exportDesc: "Backup toàn bộ config platform hiện tại hoặc khôi phục từ file JSON.",
        export: "Export Backup",
        import: "Chọn file Import...",
        previewTitle: "Xem trước Import",
        applyImport: "Apply vào Source",
        cancel: "Hủy",
        recentTitle: "Backup gần đây",
        folder: "Thư mục backup: {path}",
        empty: "Chưa có backup nào.",
        emptyFolder: "Chưa có backup nào trong thư mục backups.",
        preview: "Xem"
      },
      console: {
        title: "Console Log",
        filterTitle: "Lọc log theo service",
        allServices: "Tất cả service",
        cmdToggle: "Mở CMD riêng",
        cmdTitle: "Bật: mở cửa sổ CMD mirror song song — log vẫn hiện trong tool. Tắt: chỉ log trong tool.",
        cmdHint: "CMD riêng: log vẫn hiện ở khung này; thêm cửa sổ PowerShell mirror (Alt+Tab). Bật/tắt áp dụng ngay — không cần RUN lại.",
        stopAll: "Stop All",
        stopAllTitle: "Dừng service đang chạy (trừ service đã khóa)",
        clear: "Clear",
        resize: "Kéo để thay đổi chiều cao log",
        filterLog: "Đang lọc log: {name}",
        logEmptyCmd: "Chưa có log cho {name} trong buffer. Log vẫn stream vào Console Log khi bật CMD riêng — cửa sổ mirror mở ngay (không cần RUN lại).",
        logEmpty: "Chưa có log cho {name} trong buffer. STOP rồi RUN lại từ tool để stream log."
      },
      modal: {
        title: "Cấu hình service",
        type: "Loại",
        config: "Config",
        project: "Project",
        url: "URL service",
        env: "Biến env.js",
        save: "Lưu cấu hình",
        remove: "Xóa"
      },
      btn: {
        run: "Run", stop: "Stop", build: "Build", restart: "Restart", log: "Log",
        settings: "Cấu hình", starting: "Starting", startingTitle: "Đang khởi động..."
      },
      lock: {
        locked: "Đã khóa — không bị Stop All / thoát tool dừng",
        unlocked: "Khóa — giữ chạy khi Stop All hoặc thoát tool",
        protectedLocked: "Service nhạy cảm — đã khóa Run",
        protectedUnlockTitle: "Mở khóa service",
        protectedUnlockMsg: "{name} chỉ chạy khi cần migrate DB.\n\nBạn chắc chắn muốn mở khóa và cho phép Run?",
        protectedRunTitle: "Chạy service nhạy cảm",
        protectedRunMsg: "Bạn sắp chạy {name}.\n\nChỉ dùng khi migrate DB — có thể ảnh hưởng dữ liệu.",
        unlock: "Mở khóa",
        runConfirm: "Chạy ngay",
        cancel: "Hủy"
      },
      log: { viewService: "Xem log service này" },
      update: {
        title: "Có bản cập nhật mới",
        now: "Cập nhật ngay",
        check: "Kiểm tra lại",
        later: "Để sau",
        message: "v{current} → v{latest}{notes}. Nhấn Cập nhật ngay để tự tải, áp dụng và khởi động lại.",
        noUrl: "Chưa có link tải trong manifest",
        downloading: "Đang cập nhật..."
      },
      confirm: {
        stopAll: "Dừng tất cả service đang chạy?",
        stopAllTitle: "Dừng tất cả",
        stopAllLocked: "{count} service đã khóa sẽ không bị dừng: {names}",
        yes: "Đồng ý",
        no: "Không",
        cancel: "Hủy"
      },
      run: { starting: "Đang khởi động...", restarting: "Đang restart..." },
      label: { scheme: "Scheme", host: "Host", port: "Port", key: "Key", value: "Value" }
    },
    en: {
      appTitle: "Win_Trung - Microservices Control Panel",
      rail: { services: "Services", theme: "Dark / Light", lang: "Language / Ngôn ngữ" },
      versionBadge: "Current version",
      nav: { main: "Main navigation", dashboard: "Services", workspace: "Workspace", global: "Global config", backup: "Backup" },
      category: { group: "Group", be: "Back-End", fe: "Front-End" },
      hint: {
        dashboardBe: "Back-End list — dotnet dll --urls / appsettings",
        dashboardFe: "Front-End list — npm start / env.js",
        globalBe: "Apply host + connection string to all BE services",
        globalFe: "Apply env.js to all FE services in platform"
      },
      dashboard: {
        title: "Service list",
        descBe: "Run / stop / build Back-End services",
        descFe: "Run / stop / build Front-End services",
        reload: "Reload",
        reloadTitle: "Reload from services.loyalty.json / services.json (auto-refresh on save)",
        hint: "Add entries to services.loyalty.json → save file → list updates automatically",
        empty: "No services in this group.",
        starting: "Starting…"
      },
      workspace: {
        title: "Workspace Paths",
        desc: "Configure root folders on this machine — one-time setup",
        save: "Save workspace paths",
        missingTitle: "Services with missing folders",
        banner: "Workspace not configured ({count} items) — required before Run/Build on this machine.",
        configFile: "Config file: {path}",
        pathPlaceholder: "Pick or paste a path...",
        browse: "Browse...",
        openPaths: "Configure paths"
      },
      global: {
        title: "Global configuration",
        subtitleBe: "URL/host → appsettings.json; connection string → appsettings.json (source project folder)",
        subtitleFe: "env.js vars — keys from env.prod.js; api_url/auth_url/base_url suggested from BE/FE service URLs",
        subtitleDefault: "Applies to all services in the selected group",
        http: "HTTP connection",
        database: "Database",
        dbHint: "Writes connection string to appsettings.json (all BE) — source project folder",
        connString: "Connection string",
        envTitle: "env.js environment variables",
        addEnv: "+ Add variable",
        save: "Save & apply to all",
        feHint: "Dynamic hints: {parts} (fills only when env value is empty)"
      },
      backup: {
        title: "Backup & restore",
        desc: "Export/Import config to source — files may contain passwords, do not commit",
        platform: "Platform",
        be: "Back-End",
        fe: "Front-End",
        files: "Config files",
        localTitle: "Local defaults",
        localDesc: "Scan snapshot from source (BE: appsettings, launchSettings, ocelot · FE: env.js). Apply Local restores scanned snapshot — to apply tool edits, use Global config or Export/Import.",
        scanned: "Scanned: {date}",
        notScanned: "Not scanned — auto-scan on first launch",
        filesCount: "{count} files",
        scan: "Scan from source",
        applyLocal: "Apply Local",
        exportTitle: "Export / Import",
        exportDesc: "Backup all platform config or restore from JSON file.",
        export: "Export backup",
        import: "Choose import file...",
        previewTitle: "Import preview",
        applyImport: "Apply to source",
        cancel: "Cancel",
        recentTitle: "Recent backups",
        folder: "Backup folder: {path}",
        empty: "No backups yet.",
        emptyFolder: "No backups in backups folder.",
        preview: "View"
      },
      console: {
        title: "Console Log",
        filterTitle: "Filter logs by service",
        allServices: "All services",
        cmdToggle: "External CMD",
        cmdTitle: "On: open CMD mirror window alongside tool logs. Off: logs in tool only.",
        cmdHint: "External CMD: logs still show here; PowerShell mirror window opens (Alt+Tab). Toggle applies immediately — no restart needed.",
        stopAll: "Stop All",
        stopAllTitle: "Stop running services (except locked)",
        clear: "Clear",
        resize: "Drag to resize log panel",
        filterLog: "Filtering logs: {name}",
        logEmptyCmd: "No logs for {name} in buffer. Logs still stream here with external CMD — mirror opens immediately (no re-run needed).",
        logEmpty: "No logs for {name} in buffer. STOP then RUN from tool to stream logs."
      },
      modal: {
        title: "Service configuration",
        type: "Type",
        config: "Config",
        project: "Project",
        url: "Service URL",
        env: "env.js variables",
        save: "Save configuration",
        remove: "Remove"
      },
      btn: {
        run: "Run", stop: "Stop", build: "Build", restart: "Restart", log: "Log",
        settings: "Settings", starting: "Starting", startingTitle: "Starting..."
      },
      lock: {
        locked: "Locked — not stopped by Stop All / exit",
        unlocked: "Lock — keep running on Stop All or exit",
        protectedLocked: "Sensitive service — Run is locked",
        protectedUnlockTitle: "Unlock service",
        protectedUnlockMsg: "{name} should only run for DB migration.\n\nUnlock and allow Run?",
        protectedRunTitle: "Run sensitive service",
        protectedRunMsg: "You are about to run {name}.\n\nUse only for DB migration — may affect data.",
        unlock: "Unlock",
        runConfirm: "Run now",
        cancel: "Cancel"
      },
      log: { viewService: "View this service log" },
      update: {
        title: "Update available",
        now: "Update now",
        check: "Check again",
        later: "Later",
        message: "v{current} → v{latest}{notes}. Click Update now to download, apply and restart.",
        noUrl: "No download URL in manifest",
        downloading: "Updating..."
      },
      confirm: {
        stopAll: "Stop all running services?",
        stopAllTitle: "Stop all",
        stopAllLocked: "{count} locked service(s) will not be stopped: {names}",
        yes: "Confirm",
        no: "No",
        cancel: "Cancel"
      },
      run: { starting: "Starting...", restarting: "Restarting..." },
      label: { scheme: "Scheme", host: "Host", port: "Port", key: "Key", value: "Value" }
    }
  };

  let currentLang = localStorage.getItem(STORAGE_KEY) || "vi";
  let onChange = null;

  function lookup(dict, key) {
    return key.split(".").reduce((o, k) => (o && o[k] !== undefined ? o[k] : undefined), dict);
  }

  function t(key, params = {}) {
    const dict = MESSAGES[currentLang] || MESSAGES.vi;
    let text = lookup(dict, key) ?? lookup(MESSAGES.vi, key) ?? key;
    Object.entries(params).forEach(([k, v]) => {
      text = String(text).replace(new RegExp(`\\{${k}\\}`, "g"), String(v ?? ""));
    });
    return text;
  }

  function getLang() { return currentLang; }

  function setLang(lang) {
    const next = lang === "en" ? "en" : "vi";
    if (next === currentLang) return;
    currentLang = next;
    localStorage.setItem(STORAGE_KEY, next);
    document.documentElement.lang = next;
    applyDom();
    updateLangToggle();
    if (typeof onChange === "function") onChange(next);
  }

  function toggleLang() { setLang(currentLang === "vi" ? "en" : "vi"); }

  function onLangChange(fn) { onChange = fn; }

  function applyDom(root = document) {
    root.querySelectorAll("[data-i18n]").forEach((el) => { el.textContent = t(el.dataset.i18n); });
    root.querySelectorAll("[data-i18n-title]").forEach((el) => { el.title = t(el.dataset.i18nTitle); });
    root.querySelectorAll("[data-i18n-placeholder]").forEach((el) => { el.placeholder = t(el.dataset.i18nPlaceholder); });
    root.querySelectorAll("[data-i18n-aria]").forEach((el) => { el.setAttribute("aria-label", t(el.dataset.i18nAria)); });
    if (root === document) document.title = t("appTitle");
  }

  function updateLangToggle() {
    const btn = document.getElementById("langToggle");
    if (!btn) return;
    const label = btn.querySelector(".lang-label");
    if (label) label.textContent = currentLang === "vi" ? "VI" : "EN";
    btn.title = t("rail.lang");
    btn.classList.toggle("lang-en", currentLang === "en");
  }

  function init() {
    document.documentElement.lang = currentLang;
    applyDom();
    updateLangToggle();
  }

  window.I18n = { t, getLang, setLang, toggleLang, onLangChange, applyDom, init };
})();
