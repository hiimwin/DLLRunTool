(() => {
  const t = (key, params) => window.I18n.t(key, params);

  const state = {
    platformId: "loyalty",
    platforms: [],
    category: "BE",
    view: "dashboard",
    dashboardSig: "",
    dashboardStatusSig: "",
    services: { backEnd: [], frontEnd: [] },
    modalServiceId: null,
    modalDetail: null,
    theme: localStorage.getItem("mcp-theme") || "dark",
    workspace: null,
    logFilterServiceId: "",
    logHistory: [],
    appVersion: "",
    updateInfo: null,
    lastBackupPreview: null,
    lastGlobalConfig: null
  };

  const $ = (id) => document.getElementById(id);

  const els = {
    platformSwitcher: $("platformSwitcher"),
    dashboardList: $("dashboardList"),
    dashboardView: $("dashboardView"),
    workspaceView: $("workspaceView"),
    globalView: $("globalView"),
    backupView: $("backupView"),
    workspaceBanner: $("workspaceBanner"),
    workspaceBannerText: $("workspaceBannerText"),
    btnOpenWorkspace: $("btnOpenWorkspace"),
    workspacePathList: $("workspacePathList"),
    workspacePathsFile: $("workspacePathsFile"),
    workspaceMissing: $("workspaceMissing"),
    workspaceMissingList: $("workspaceMissingList"),
    btnSaveWorkspace: $("btnSaveWorkspace"),
    backupPlatformName: $("backupPlatformName"),
    backupBeCount: $("backupBeCount"),
    backupFeCount: $("backupFeCount"),
    backupConfigCount: $("backupConfigCount"),
    backupFolderPath: $("backupFolderPath"),
    recentBackupList: $("recentBackupList"),
    btnExportConfig: $("btnExportConfig"),
    btnImportConfig: $("btnImportConfig"),
    globalSubtitle: $("globalSubtitle"),
    globalBeConfig: $("globalBeConfig"),
    globalFeConfig: $("globalFeConfig"),
    globalFeBindingsHint: $("globalFeBindingsHint"),
    globalScopeBadge: $("globalScopeBadge"),
    contextBar: $("contextBar"),
    contextBarHint: $("contextBarHint"),
    dashboardDesc: $("dashboardDesc"),
    globalScheme: $("globalScheme"),
    globalHost: $("globalHost"),
    globalConnectionString: $("globalConnectionString"),
    globalEnvContainer: $("globalEnvContainer"),
    globalAddEnv: $("globalAddEnv"),
    btnSaveGlobal: $("btnSaveGlobal"),
    consoleOutput: $("consoleOutput"),
    btnClearLog: $("btnClearLog"),
    btnStopAll: $("btnStopAll"),
    btnReloadServices: $("btnReloadServices"),
    chkShowConsole: $("chkShowConsole"),
    logFilter: $("logFilter"),
    logFilterBtn: $("logFilterBtn"),
    logFilterLabel: $("logFilterLabel"),
    logFilterMenu: $("logFilterMenu"),
    consoleSection: $("consoleSection"),
    consoleResizeHandle: $("consoleResizeHandle"),
    consoleModeHint: $("consoleModeHint"),
    themeToggle: $("themeToggle"),
    langToggle: $("langToggle"),
    settingsModal: $("settingsModal"),
    modalTitle: $("modalTitle"),
    modalTypeBadge: $("modalTypeBadge"),
    modalConfigPath: $("modalConfigPath"),
    modalProjectPath: $("modalProjectPath"),
    modalFeBindingsHint: $("modalFeBindingsHint"),
    modalBeConfig: $("modalBeConfig"),
    modalFeConfig: $("modalFeConfig"),
    modalScheme: $("modalScheme"),
    modalHost: $("modalHost"),
    modalPort: $("modalPort"),
    modalConnectionString: $("modalConnectionString"),
    modalEnvContainer: $("modalEnvContainer"),
    modalAddEnv: $("modalAddEnv"),
    modalClose: $("modalClose"),
    modalSave: $("modalSave"),
    localDefaultsStatus: $("localDefaultsStatus"),
    localDefaultsFiles: $("localDefaultsFiles"),
    btnScanLocal: $("btnScanLocal"),
    btnApplyLocal: $("btnApplyLocal"),
    importPreview: $("importPreview"),
    importPreviewMeta: $("importPreviewMeta"),
    importPreviewList: $("importPreviewList"),
    btnApplyImport: $("btnApplyImport"),
    btnCancelImport: $("btnCancelImport"),
    appVersionBadge: $("appVersionBadge"),
    updateBanner: $("updateBanner"),
    updateBannerMessage: $("updateBannerMessage"),
    btnDownloadUpdate: $("btnDownloadUpdate"),
    btnCheckUpdate: $("btnCheckUpdate"),
    btnDismissUpdate: $("btnDismissUpdate")
  };

  let pendingImportPath = null;

  function applyTheme() {
    document.body.setAttribute("data-theme", state.theme);
    localStorage.setItem("mcp-theme", state.theme);
  }

  function refreshI18nDynamic() {
    I18n.applyDom();
    updateNavContext();
    if (state.view === "backup" && state.lastBackupPreview) renderBackupPreview(state.lastBackupPreview);
    if (state.view === "global" && state.lastGlobalConfig) renderGlobalConfig(state.lastGlobalConfig);
    if (state.workspace) renderWorkspaceBanner(state.workspace);
    updateLogServiceFilter();
    updateConsoleModeHint(!!(els.chkShowConsole && els.chkShowConsole.checked));
    if (state.updateInfo?.isUpdateAvailable) showUpdateBanner(state.updateInfo);
    state.dashboardSig = "";
    renderDashboard();
    renderConsoleLogs();
  }

  function showUpdateBanner(info) {
    if (!els.updateBanner || !info?.isUpdateAvailable) return;

    const dismissed = localStorage.getItem("mcp-update-dismissed-version");
    if (dismissed && dismissed === info.latestVersion) return;

    state.updateInfo = info;
    if (els.updateBannerMessage) {
      els.updateBannerMessage.textContent = t("update.message", {
        current: info.currentVersion,
        latest: info.latestVersion,
        notes: info.releaseNotes ? ` — ${info.releaseNotes}` : ""
      });
    }
    if (els.btnDownloadUpdate) {
      els.btnDownloadUpdate.disabled = !info.downloadUrl;
      els.btnDownloadUpdate.title = info.downloadUrl ? "" : t("update.noUrl");
    }
    els.updateBanner.classList.remove("hidden");
  }

  function hideUpdateBanner(dismissForVersion) {
    if (dismissForVersion && state.updateInfo?.latestVersion) {
      localStorage.setItem("mcp-update-dismissed-version", state.updateInfo.latestVersion);
    }
    els.updateBanner?.classList.add("hidden");
  }

  function getActiveServices() {
    return state.category === "BE" ? state.services.backEnd : state.services.frontEnd;
  }

  function findServiceInState(serviceId) {
    return state.services.backEnd.find((s) => s.id === serviceId)
      || state.services.frontEnd.find((s) => s.id === serviceId);
  }

  function isServiceBusy(svc) {
    return !!(svc && (svc.isStarting || svc.isBuilding));
  }

  function setLocalServiceFlags(serviceId, patch) {
    const svc = findServiceInState(serviceId);
    if (svc) Object.assign(svc, patch);
    patchDashboardStatuses(getActiveServices());
  }

  function renderRunStopButton(svc) {
    if (svc.isStarting) {
      return `<button class="row-btn success is-busy" disabled title="${escapeAttr(t("btn.startingTitle"))}">
        <svg class="spin" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 2v4M12 18v4M4.93 4.93l2.83 2.83M16.24 16.24l2.83 2.83M2 12h4M18 12h4M4.93 19.07l2.83-2.83M16.24 7.76l2.83-2.83"/></svg>
        ${escapeHtml(t("btn.starting"))}
      </button>`;
    }
    if (svc.isRunning) {
      return `<button class="row-btn danger" data-action="stop" data-id="${escapeAttr(svc.id)}">
        <svg viewBox="0 0 24 24" fill="currentColor"><rect x="6" y="6" width="12" height="12" rx="1"/></svg>
        ${escapeHtml(t("btn.stop"))}
      </button>`;
    }
    const busy = isServiceBusy(svc) ? " disabled" : "";
    return `<button class="row-btn success${busy ? " is-busy" : ""}" data-action="run" data-id="${escapeAttr(svc.id)}"${busy}>
      <svg viewBox="0 0 24 24" fill="currentColor"><path d="M8 5v14l11-7z"/></svg>
      ${escapeHtml(t("btn.run"))}
    </button>`;
  }

  function renderBuildButton(svc) {
    const label = escapeHtml(t("btn.build"));
    if (svc.isExe) {
      return `<span class="row-btn row-btn-spacer secondary" aria-hidden="true">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z"/></svg>
        ${label}
      </span>`;
    }
    const busy = isServiceBusy(svc);
    return `<button class="row-btn secondary${busy ? " is-busy" : ""}" data-action="build" data-id="${escapeAttr(svc.id)}" title="${escapeAttr(t("btn.build"))}"${busy ? " disabled" : ""}>
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z"/></svg>
      ${label}
    </button>`;
  }

  function renderPlatforms() {
    els.platformSwitcher.innerHTML = "";
    state.platforms.forEach((p) => {
      const btn = document.createElement("button");
      btn.className = `platform-btn${p.id === state.platformId ? " active" : ""}`;
      btn.textContent = p.name;
      btn.dataset.platformId = p.id;
      btn.onclick = () => selectPlatform(p.id);
      els.platformSwitcher.appendChild(btn);
    });
  }

  function selectPlatform(platformId) {
    if (state.platformId === platformId) return;
    state.platformId = platformId;
    renderPlatforms();
    Bridge.send("selectPlatform", { platformId });
    if (state.view === "global") loadGlobalConfig();
    if (state.view === "backup") loadBackupPreview();
  }

  function renderDashboard() {
    const list = getActiveServices();
    const signature = list.map((s) => s.id).join(",");
    const statusSig = list.map((s) => `${s.id}:${s.isRunning ? 1 : 0}:${s.isLocked ? 1 : 0}:${s.isStarting ? 1 : 0}:${s.isBuilding ? 1 : 0}`).join(",");
    const hasRows = !!els.dashboardList.querySelector("[data-service-id]");

    if (signature === state.dashboardSig && hasRows) {
      if (statusSig !== state.dashboardStatusSig) {
        state.dashboardStatusSig = statusSig;
        patchDashboardStatuses(list);
      }
      return;
    }

    state.dashboardSig = signature;
    state.dashboardStatusSig = statusSig;
    els.dashboardList.innerHTML = "";

    if (list.length === 0) {
      els.dashboardList.innerHTML = `<div class="empty-dashboard">${escapeHtml(t("dashboard.empty"))}</div>`;
      return;
    }

    list.forEach((svc) => {
      const displayName = svc.dllName || svc.name;
      const row = document.createElement("div");
      row.className = "service-row";
      row.dataset.serviceId = svc.id;
      row.innerHTML = `
        <span class="status-led${svc.isRunning ? " running" : ""}${svc.isStarting && !svc.isRunning ? " starting" : ""}"></span>
        <div class="service-row-info">
          <div class="service-row-name">${escapeHtml(displayName)}</div>
          <div class="service-row-sub">${escapeHtml(svc.name)}${svc.url ? " · " + escapeHtml(svc.url) : ""}${svc.isStarting ? " · " + escapeHtml(t("dashboard.starting")) : ""}</div>
          <div class="build-progress hidden" data-build-progress="${escapeAttr(svc.id)}">
            <div class="build-progress-track">
              <div class="build-progress-fill"></div>
            </div>
            <span class="build-progress-text">0%</span>
          </div>
        </div>
        <div class="service-row-actions">
          ${renderBuildButton(svc)}
          ${renderRunStopButton(svc)}
          <button class="row-btn secondary${isServiceBusy(svc) ? " is-busy" : ""}" data-action="restart" data-id="${escapeAttr(svc.id)}" title="${escapeAttr(t("btn.restart"))}"${isServiceBusy(svc) ? " disabled" : ""}>
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M23 4v6h-6"/><path d="M1 20v-6h6"/><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"/></svg>
            ${escapeHtml(t("btn.restart"))}
          </button>
          <button class="row-btn secondary" data-action="logs" data-id="${escapeAttr(svc.id)}" title="${escapeAttr(t("log.viewService"))}">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M4 6h16M4 12h16M4 18h10"/></svg>
            ${escapeHtml(t("btn.log"))}
          </button>
          <button class="row-btn icon-only lock-btn${svc.isLocked ? " locked" : ""}" data-action="lock" data-id="${escapeAttr(svc.id)}" title="${escapeAttr(svc.isLocked ? t("lock.locked") : t("lock.unlocked"))}">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              ${svc.isLocked
                ? `<rect x="5" y="11" width="14" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/>`
                : `<path d="M7 11V7a5 5 0 0 1 9.9-1"/><rect x="5" y="11" width="14" height="10" rx="2"/>`}
            </svg>
          </button>
          ${svc.isExe
            ? `<span class="row-btn row-btn-spacer icon-only" aria-hidden="true">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="3"/><path d="M12 1v2M12 21v2M4.22 4.22l1.42 1.42M18.36 18.36l1.42 1.42M1 12h2M21 12h2M4.22 19.78l1.42-1.42M18.36 5.64l1.42-1.42"/></svg>
              </span>`
            : `<button class="row-btn icon-only" data-action="settings" data-id="${escapeAttr(svc.id)}" title="${escapeAttr(t("btn.settings"))}">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="3"/><path d="M12 1v2M12 21v2M4.22 4.22l1.42 1.42M18.36 18.36l1.42 1.42M1 12h2M21 12h2M4.22 19.78l1.42-1.42M18.36 5.64l1.42-1.42"/></svg>
              </button>`}
        </div>
      `;
      els.dashboardList.appendChild(row);
    });

    els.dashboardList.querySelectorAll("[data-action]").forEach(wireRowButton);
    updateLogServiceFilter();
  }

  function patchDashboardStatuses(list) {
    list.forEach((svc) => {
      const row = els.dashboardList.querySelector(`[data-service-id="${svc.id}"]`);
      if (!row) return;
      const led = row.querySelector(".status-led");
      if (led) {
        led.classList.toggle("running", !!svc.isRunning);
        led.classList.toggle("starting", !!svc.isStarting && !svc.isRunning);
      }

      const sub = row.querySelector(".service-row-sub");
      if (sub) {
        const base = `${svc.name}${svc.url ? " · " + svc.url : ""}`;
        sub.textContent = svc.isStarting ? `${base} · ${t("dashboard.starting")}` : base;
      }

      const actions = row.querySelector(".service-row-actions");
      if (actions && actions.children.length >= 2) {
        actions.children[0].outerHTML = renderBuildButton(svc);
        actions.children[1].outerHTML = renderRunStopButton(svc);
        const restart = actions.querySelector('[data-action="restart"]');
        if (restart) {
          restart.disabled = isServiceBusy(svc);
          restart.classList.toggle("is-busy", isServiceBusy(svc));
        }
        actions.querySelectorAll("[data-action]").forEach(wireRowButton);
      }

      const lockBtn = actions?.querySelector('[data-action="lock"]');
      if (lockBtn) {
        lockBtn.classList.toggle("locked", !!svc.isLocked);
        lockBtn.title = svc.isLocked ? t("lock.locked") : t("lock.unlocked");
        const svg = lockBtn.querySelector("svg");
        if (svg) {
          svg.innerHTML = svc.isLocked
            ? `<rect x="5" y="11" width="14" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/>`
            : `<path d="M7 11V7a5 5 0 0 1 9.9-1"/><rect x="5" y="11" width="14" height="10" rx="2"/>`;
        }
      }
    });
  }

  function wireRowButton(btn) {
    if (!btn) return;
    btn.onclick = (e) => {
      e.stopPropagation();
      handleRowAction(btn.dataset.action, btn.dataset.id);
    };
  }

  function handleRowAction(action, serviceId) {
    const svc = findServiceInState(serviceId);
    if (action !== "stop" && action !== "lock" && action !== "logs" && action !== "settings" && isServiceBusy(svc)) {
      return;
    }

    const payload = { serviceId, platformId: state.platformId };
    switch (action) {
      case "run":
        setLogFilterValue(serviceId, findServiceName(serviceId));
        setLocalServiceFlags(serviceId, { isStarting: true });
        setRunProgress({ serviceId, active: true, label: t("run.starting") });
        Bridge.send("run", payload);
        break;
      case "stop": Bridge.send("stop", payload); break;
      case "build": Bridge.send("build", payload); break;
      case "restart":
        setLocalServiceFlags(serviceId, { isStarting: true });
        setRunProgress({ serviceId, active: true, label: t("run.restarting") });
        Bridge.send("restart", payload);
        break;
      case "settings":
        state.modalServiceId = serviceId;
        Bridge.send("selectService", payload);
        break;
      case "logs": {
        const name = findServiceName(serviceId);
        setLogFilterValue(serviceId, name);
        appendLog({
          serviceId,
          level: "info",
          message: t("console.filterLog", { name })
        });
        break;
      }
      case "lock": {
        const svc = getActiveServices().find((s) => s.id === serviceId);
        Bridge.send("toggleServiceLock", {
          serviceId,
          platformId: state.platformId,
          locked: !(svc && svc.isLocked)
        });
        break;
      }
    }
  }

  function setLogFilterValue(serviceId, label) {
    state.logFilterServiceId = serviceId || "";
    if (els.logFilterLabel) {
      els.logFilterLabel.textContent = serviceId ? label : t("console.allServices");
    }
    if (els.logFilterMenu) {
      els.logFilterMenu.querySelectorAll(".log-filter-item").forEach((item) => {
        item.classList.toggle("active", item.dataset.id === (serviceId || ""));
      });
    }
    renderConsoleLogs();
  }

  function closeLogFilterMenu() {
    els.logFilterMenu?.classList.add("hidden");
    els.logFilterBtn?.setAttribute("aria-expanded", "false");
  }

  function toggleLogFilterMenu() {
    if (!els.logFilterMenu || !els.logFilterBtn) return;
    const isHidden = els.logFilterMenu.classList.toggle("hidden");
    els.logFilterBtn.setAttribute("aria-expanded", isHidden ? "false" : "true");
  }

  function matchesLogFilter(payload, filterId) {
    if (!filterId) return true;
    if (!payload.serviceId) return true;
    return payload.serviceId === filterId;
  }

  function pushLogHistory(payload) {
    state.logHistory.push(payload);
  }

  function createLogLineElement(payload) {
    const line = document.createElement("div");
    line.className = `log-line ${payload.level || "info"}`;
    const msg = payload.message || "";
    if (msg.toLowerCase().includes("stopped") || msg.includes("đã dừng") || msg.includes("đã thoát")) {
      line.classList.add("status-stopped");
    }
    if (msg.toLowerCase().includes("running") || msg.includes("đang chạy")) {
      line.classList.add("status-running");
    }
    const svcTag = payload.serviceName
      ? `<span class="log-svc">[${escapeHtml(payload.serviceName)}]</span> `
      : (payload.serviceId ? `<span class="log-svc">[${escapeHtml(findServiceName(payload.serviceId))}]</span> ` : "");
    line.innerHTML = `<span class="log-time">[${payload.timestamp || ""}]</span>${svcTag}${escapeHtml(msg)}`;
    return line;
  }

  function renderLogFilterEmptyHint(filterId) {
    const name = findServiceName(filterId);
    const cmdMode = !!(els.chkShowConsole && els.chkShowConsole.checked);
    const hint = document.createElement("div");
    hint.className = "log-line info log-filter-empty";
    hint.textContent = cmdMode
      ? t("console.logEmptyCmd", { name })
      : t("console.logEmpty", { name });
    return hint;
  }

  function renderConsoleLogs() {
    if (!els.consoleOutput) return;

    const filterId = state.logFilterServiceId || "";
    const entries = state.logHistory.filter((e) => matchesLogFilter(e, filterId));

    els.consoleOutput.innerHTML = "";

    if (filterId && entries.length === 0) {
      els.consoleOutput.appendChild(renderLogFilterEmptyHint(filterId));
      return;
    }

    entries.forEach((entry) => {
      els.consoleOutput.appendChild(createLogLineElement(entry));
    });
    els.consoleOutput.scrollTop = els.consoleOutput.scrollHeight;
  }

  function findServiceName(serviceId) {
    const all = [...(state.services.backEnd || []), ...(state.services.frontEnd || [])];
    const svc = all.find((s) => s.id === serviceId);
    return svc ? svc.name : serviceId;
  }

  function updateLogServiceFilter() {
    if (!els.logFilterMenu) return;
    const all = [...(state.services.backEnd || []), ...(state.services.frontEnd || [])];
    const current = state.logFilterServiceId || "";
    els.logFilterMenu.innerHTML =
      `<button type="button" class="log-filter-item${current === "" ? " active" : ""}" data-id="">${escapeHtml(t("console.allServices"))}</button>` +
      all.map((s) =>
        `<button type="button" class="log-filter-item${current === s.id ? " active" : ""}" data-id="${escapeAttr(s.id)}">${escapeHtml(s.name)}</button>`
      ).join("");

    els.logFilterMenu.querySelectorAll(".log-filter-item").forEach((btn) => {
      btn.onclick = (e) => {
        e.stopPropagation();
        const id = btn.dataset.id || "";
        setLogFilterValue(id, id ? btn.textContent : t("console.allServices"));
        closeLogFilterMenu();
      };
    });

    if (current && all.some((s) => s.id === current)) {
      setLogFilterValue(current, findServiceName(current));
    } else if (!current) {
      setLogFilterValue("", t("console.allServices"));
    }
  }

  function updateNavContext() {
    const showCategory = state.view === "dashboard" || state.view === "global";
    const isFe = state.category === "FE";

    if (els.contextBar) {
      els.contextBar.classList.toggle("hidden", !showCategory);
    }

    if (els.contextBarHint) {
      if (!showCategory) {
        els.contextBarHint.textContent = "";
      } else if (state.view === "dashboard") {
        els.contextBarHint.textContent = isFe ? t("hint.dashboardFe") : t("hint.dashboardBe");
      } else {
        els.contextBarHint.textContent = isFe ? t("hint.globalFe") : t("hint.globalBe");
      }
    }

    if (els.dashboardDesc) {
      els.dashboardDesc.textContent = isFe ? t("dashboard.descFe") : t("dashboard.descBe");
    }

    if (els.globalScopeBadge) {
      els.globalScopeBadge.textContent = isFe ? t("category.fe") : t("category.be");
      els.globalScopeBadge.classList.toggle("fe", isFe);
    }

    if (els.consoleSection) {
      els.consoleSection.classList.toggle("hidden", state.view !== "dashboard");
    }
  }

  function switchView(view) {
    state.view = view;
    document.querySelectorAll(".view-tab").forEach((t) => {
      t.classList.toggle("active", t.dataset.view === view);
    });
    els.dashboardView.classList.toggle("hidden", view !== "dashboard");
    els.workspaceView.classList.toggle("hidden", view !== "workspace");
    els.globalView.classList.toggle("hidden", view !== "global");
    els.backupView.classList.toggle("hidden", view !== "backup");
    if (view === "global") loadGlobalConfig();
    if (view === "backup") loadBackupPreview();
    if (view === "workspace") loadWorkspacePaths();
    updateNavContext();
  }

  function loadWorkspacePaths() {
    Bridge.send("loadWorkspacePaths");
  }

  function renderWorkspaceBanner(workspace) {
    if (!workspace) {
      els.workspaceBanner.classList.add("hidden");
      return;
    }

    const issues = workspace.rootIssues || [];
    if (issues.length === 0) {
      els.workspaceBanner.classList.add("hidden");
      return;
    }

    els.workspaceBannerText.textContent = t("workspace.banner", { count: issues.length });
    els.workspaceBanner.classList.remove("hidden");
  }

  function renderWorkspacePaths(payload) {
    state.workspace = payload;
    renderWorkspaceBanner(payload);

    els.workspacePathsFile.textContent = payload.pathsFile
      ? t("workspace.configFile", { path: payload.pathsFile })
      : "";

    const paths = payload.paths || {};
    const defs = payload.definitions || [];
    els.workspacePathList.innerHTML = "";

    defs.forEach((def) => {
      const row = document.createElement("div");
      row.className = "workspace-path-row";
      const value = paths[def.key] || "";
      const issue = (payload.rootIssues || []).find((i) => i.key === def.key);
      row.innerHTML = `
        <div class="workspace-path-info">
          <label for="ws-${escapeAttr(def.key)}">${escapeHtml(def.label)}</label>
          <span class="workspace-path-hint">${escapeHtml(def.hint)}</span>
          ${issue ? `<span class="workspace-path-error">${escapeHtml(issue.message)}</span>` : ""}
        </div>
        <div class="workspace-path-input">
          <input type="text" id="ws-${escapeAttr(def.key)}" data-key="${escapeAttr(def.key)}" value="${escapeAttr(value)}" placeholder="${escapeAttr(t("workspace.pathPlaceholder"))}" />
          <button class="btn secondary small btn-browse-ws" data-key="${escapeAttr(def.key)}">${escapeHtml(t("workspace.browse"))}</button>
        </div>
      `;
      els.workspacePathList.appendChild(row);
    });

    els.workspacePathList.querySelectorAll(".btn-browse-ws").forEach((btn) => {
      btn.onclick = () => Bridge.send("browseWorkspaceFolder", { pathKey: btn.dataset.key });
    });

    const missing = payload.missingServices || [];
    if (missing.length > 0) {
      els.workspaceMissing.classList.remove("hidden");
      els.workspaceMissingList.innerHTML = missing.map((m) => `<li>${escapeHtml(m)}</li>`).join("");
    } else {
      els.workspaceMissing.classList.add("hidden");
      els.workspaceMissingList.innerHTML = "";
    }
  }

  function collectWorkspacePaths() {
    const paths = {};
    els.workspacePathList.querySelectorAll("input[data-key]").forEach((input) => {
      paths[input.dataset.key] = input.value.trim();
    });
    return paths;
  }

  function loadBackupPreview() {
    Bridge.send("getBackupPreview", { platformId: state.platformId });
  }

  function renderBackupPreview(payload) {
    state.lastBackupPreview = payload;
    els.backupPlatformName.textContent = payload.platformName || "—";
    els.backupBeCount.textContent = payload.backEndCount ?? 0;
    els.backupFeCount.textContent = payload.frontEndCount ?? 0;
    els.backupConfigCount.textContent = payload.configFileCount ?? 0;
    els.backupFolderPath.textContent = payload.backupsFolder ? t("backup.folder", { path: payload.backupsFolder }) : "";

    if (payload.hasLocalDefaults) {
      els.localDefaultsStatus.textContent = t("backup.scanned", { date: payload.localDefaultsScannedAt || "—" });
      els.localDefaultsFiles.textContent = t("backup.filesCount", { count: payload.localDefaultsFileCount || 0 });
    } else {
      els.localDefaultsStatus.textContent = t("backup.notScanned");
      els.localDefaultsFiles.textContent = t("backup.filesCount", { count: 0 });
    }

    const recent = payload.recentBackups || [];
    els.recentBackupList.innerHTML = "";
    if (recent.length === 0) {
      els.recentBackupList.innerHTML = `<div class="empty-dashboard">${escapeHtml(t("backup.emptyFolder"))}</div>`;
      return;
    }

    recent.forEach((item) => {
      const row = document.createElement("div");
      row.className = "recent-item";
      const sizeKb = ((item.sizeBytes || 0) / 1024).toFixed(1);
      row.innerHTML = `
        <div class="recent-item-info">
          <div class="recent-item-name">${escapeHtml(item.fileName)}</div>
          <div class="recent-item-meta">${escapeHtml(item.exportedAt || "")} · ${sizeKb} KB · ${escapeHtml(item.platformId || "")}</div>
        </div>
        <button class="btn ghost small btn-preview-recent">${escapeHtml(t("backup.preview"))}</button>
      `;
      row.querySelector(".btn-preview-recent").onclick = () => {
        Bridge.send("previewImport", { platformId: state.platformId, filePath: item.fullPath });
      };
      els.recentBackupList.appendChild(row);
    });
  }

  function showImportPreview(preview) {
    pendingImportPath = preview.filePath;
    els.importPreview.classList.remove("hidden");
    els.importPreviewMeta.textContent =
      `${preview.fileName} · ${preview.serviceCount} services · ${preview.configFileCount} files` +
      (preview.isLocalDefaults ? " · Local Defaults" : ` · từ ${preview.backupPlatformName}`);
    els.importPreviewList.innerHTML = "";
    (preview.services || []).forEach((svc) => {
      const li = document.createElement("li");
      li.innerHTML = `<strong>${escapeHtml(svc.name)}</strong> (${escapeHtml(svc.type)}) — ${escapeHtml((svc.files || []).join(", "))}`;
      els.importPreviewList.appendChild(li);
    });
  }

  function hideImportPreview() {
    pendingImportPath = null;
    els.importPreview.classList.add("hidden");
    els.importPreviewList.innerHTML = "";
  }

  function loadGlobalConfig() {
    Bridge.send("loadGlobalConfig", { platformId: state.platformId, category: state.category });
  }

  function renderGlobalConfig(payload) {
    state.lastGlobalConfig = payload;
    const config = payload.config || payload;
    const isFe = state.category === "FE";
    els.globalBeConfig.classList.toggle("hidden", isFe);
    els.globalFeConfig.classList.toggle("hidden", !isFe);
    els.globalSubtitle.textContent = isFe ? t("global.subtitleFe") : t("global.subtitleBe");

    if (isFe) {
      renderEnvContainer(els.globalEnvContainer, config.envVars || {});
      renderFeBindingsHint(payload.feBindings || [], els.globalFeBindingsHint);
    } else {
      if (els.globalFeBindingsHint) els.globalFeBindingsHint.classList.add("hidden");
      els.globalScheme.value = config.scheme || "https";
      els.globalHost.value = config.host || "localhost";
      els.globalConnectionString.value = config.connectionString || "";
    }
  }

  function renderFeBindingsHint(bindings, target = els.globalFeBindingsHint) {
    if (!target) return;
    if (!bindings || bindings.length === 0) {
      target.classList.add("hidden");
      target.textContent = "";
      return;
    }

    const parts = bindings.map((b) => {
      const src = b.sourceService ? ` ← ${b.sourceService}` : "";
      return `${b.envKey} = ${b.value}${src}`;
    });
    target.textContent = t("global.feHint", { parts: parts.join(" · ") });
    target.classList.remove("hidden");
  }

  function openModal(detail) {
    state.modalDetail = detail;
    state.modalServiceId = detail.id;
    els.modalTitle.textContent = detail.name;
    const isFe = detail.type === "FE";
    if (els.modalTypeBadge) {
      els.modalTypeBadge.textContent = isFe ? t("category.fe") : t("category.be");
      els.modalTypeBadge.classList.toggle("fe", isFe);
    }
    if (els.modalConfigPath) els.modalConfigPath.textContent = detail.configPath || "—";
    if (els.modalProjectPath) els.modalProjectPath.textContent = detail.projectPath || "—";
    els.modalBeConfig.classList.toggle("hidden", isFe);
    els.modalFeConfig.classList.toggle("hidden", !isFe);
    if (isFe) {
      renderEnvContainer(els.modalEnvContainer, detail.envVars || {});
      renderFeBindingsHint(detail.feBindings || [], els.modalFeBindingsHint);
    } else {
      if (els.modalFeBindingsHint) els.modalFeBindingsHint.classList.add("hidden");
      els.modalScheme.value = detail.scheme || "https";
      els.modalHost.value = detail.host || "localhost";
      els.modalPort.value = detail.port || "";
      els.modalConnectionString.value = detail.connectionString || "";
    }
    els.settingsModal.classList.remove("hidden");
  }

  function closeModal() {
    els.settingsModal.classList.add("hidden");
    state.modalServiceId = null;
    state.modalDetail = null;
  }

  function renderEnvContainer(container, envVars) {
    container.innerHTML = "";
    const entries = Object.entries(envVars);
    if (entries.length === 0) {
      addEnvRow(container, "base_url", "");
      return;
    }
    entries.forEach(([k, v]) => addEnvRow(container, k, v));
  }

  function addEnvRow(container, key = "", value = "") {
    const row = document.createElement("div");
    row.className = "env-row";
    row.innerHTML = `
      <label><span>${escapeHtml(t("label.key"))}</span><input type="text" class="env-key" value="${escapeAttr(key)}" /></label>
      <label><span>${escapeHtml(t("label.value"))}</span><input type="text" class="env-val" value="${escapeAttr(value)}" /></label>
      <button class="remove-env" title="${escapeAttr(t("modal.remove"))}">×</button>
    `;
    row.querySelector(".remove-env").onclick = () => row.remove();
    container.appendChild(row);
  }

  function collectEnvFrom(container) {
    const envVars = {};
    container.querySelectorAll(".env-row").forEach((row) => {
      const key = row.querySelector(".env-key").value.trim();
      const val = row.querySelector(".env-val").value.trim();
      if (key) envVars[key] = val;
    });
    return envVars;
  }

  function collectModalConfig() {
    if (!state.modalDetail) return null;
    if (state.modalDetail.type === "FE") {
      return { envVars: collectEnvFrom(els.modalEnvContainer) };
    }
    return {
      scheme: els.modalScheme.value,
      host: els.modalHost.value.trim(),
      port: els.modalPort.value.trim(),
      connectionString: els.modalConnectionString.value.trim()
    };
  }

  function collectGlobalConfig() {
    if (state.category === "FE") {
      return { envVars: collectEnvFrom(els.globalEnvContainer) };
    }
    return {
      scheme: els.globalScheme.value,
      host: els.globalHost.value.trim(),
      connectionString: els.globalConnectionString.value.trim()
    };
  }

  function setBuildProgress(payload) {
    const row = els.dashboardList.querySelector(`[data-service-id="${payload.serviceId}"]`);
    if (!row) return;
    const box = row.querySelector("[data-build-progress]");
    if (!box) return;
    box.classList.remove("run-mode");
    const fill = box.querySelector(".build-progress-fill");
    const text = box.querySelector(".build-progress-text");
    const pct = Math.max(0, Math.min(100, payload.percent ?? 0));

    if (payload.active) {
      box.classList.remove("hidden");
      if (fill) {
        fill.classList.remove("indeterminate");
        fill.style.width = `${pct}%`;
      }
      if (text) text.textContent = `${pct}%${payload.label ? " · " + payload.label : ""}`;
    } else {
      if (fill) fill.style.width = `${pct}%`;
      if (text) text.textContent = payload.label || `${pct}%`;
      setTimeout(() => box.classList.add("hidden"), payload.percent === 100 ? 2000 : 800);
    }
  }

  function setRunProgress(payload) {
    const row = els.dashboardList.querySelector(`[data-service-id="${payload.serviceId}"]`);
    if (!row) return;
    const box = row.querySelector("[data-build-progress]");
    if (!box) return;
    const fill = box.querySelector(".build-progress-fill");
    const text = box.querySelector(".build-progress-text");

    if (payload.active) {
      box.classList.add("run-mode");
      box.classList.remove("hidden");
      if (fill) {
        fill.classList.add("indeterminate");
        fill.style.width = "35%";
      }
      if (text) text.textContent = payload.label || t("run.starting");
    } else {
      box.classList.remove("run-mode");
      if (fill) fill.classList.remove("indeterminate");
      setTimeout(() => {
        if (!box.classList.contains("run-mode")) box.classList.add("hidden");
      }, 600);
    }
  }

  function appendLog(payload) {
    const entry = {
      level: "info",
      timestamp: "",
      ...payload
    };
    pushLogHistory(entry);

    const filterId = state.logFilterServiceId || "";
    if (!matchesLogFilter(entry, filterId)) {
      return;
    }

    if (!els.consoleOutput) return;

    const emptyHint = els.consoleOutput.querySelector(".log-filter-empty");
    if (emptyHint) emptyHint.remove();

    els.consoleOutput.appendChild(createLogLineElement(entry));
    els.consoleOutput.scrollTop = els.consoleOutput.scrollHeight;
  }

  function escapeHtml(str) {
    const d = document.createElement("div");
    d.textContent = str;
    return d.innerHTML;
  }

  function escapeAttr(str) {
    return String(str).replace(/"/g, "&quot;");
  }

  document.querySelectorAll(".category-tab").forEach((tab) => {
    tab.addEventListener("click", () => {
      document.querySelectorAll(".category-tab").forEach((t) => t.classList.remove("active"));
      tab.classList.add("active");
      state.category = tab.dataset.category;
      updateNavContext();
      renderDashboard();
      if (state.view === "global") loadGlobalConfig();
    });
  });

  document.querySelectorAll(".view-tab").forEach((tab) => {
    tab.addEventListener("click", () => switchView(tab.dataset.view));
  });

  els.themeToggle.onclick = () => {
    state.theme = state.theme === "dark" ? "light" : "dark";
    applyTheme();
  };

  if (els.langToggle) {
    els.langToggle.onclick = () => I18n.toggleLang();
  }
  I18n.onLangChange(() => refreshI18nDynamic());
  I18n.init();

  els.btnClearLog.onclick = () => {
    state.logHistory = [];
    els.consoleOutput.innerHTML = "";
  };
  if (els.btnReloadServices) {
    els.btnReloadServices.onclick = () => Bridge.send("reloadServices");
  }
  els.btnStopAll.onclick = () => {
    const lockedRunning = getActiveServices().filter((s) => s.isRunning && s.isLocked);
    let msg = t("confirm.stopAll");
    if (lockedRunning.length > 0) {
      msg += `\n\n${t("confirm.stopAllLocked", {
        count: lockedRunning.length,
        names: lockedRunning.map((s) => s.name).join(", ")
      })}`;
    }
    if (confirm(msg)) {
      Bridge.send("stopAll");
    }
  };
  function updateConsoleModeHint(showExternal) {
    if (!els.consoleModeHint) return;
    if (showExternal) {
      els.consoleModeHint.textContent = t("console.cmdHint");
      els.consoleModeHint.classList.remove("hidden");
    } else {
      els.consoleModeHint.textContent = "";
      els.consoleModeHint.classList.add("hidden");
    }
  }

  function applyConsoleHeight(px) {
    const height = Math.max(140, Math.min(window.innerHeight * 0.75, px));
    if (els.consoleSection) {
      els.consoleSection.style.setProperty("--console-height", `${height}px`);
    }
    localStorage.setItem("mcp-console-height", String(Math.round(height)));
  }

  function initConsoleResize() {
    const saved = parseInt(localStorage.getItem("mcp-console-height") || "320", 10);
    if (!Number.isNaN(saved)) applyConsoleHeight(saved);

    const handle = els.consoleResizeHandle;
    const section = els.consoleSection;
    if (!handle || !section) return;

    let dragging = false;
    let startY = 0;
    let startHeight = 0;

    const onMove = (clientY) => {
      const delta = startY - clientY;
      applyConsoleHeight(startHeight + delta);
    };

    handle.addEventListener("mousedown", (e) => {
      dragging = true;
      startY = e.clientY;
      startHeight = section.getBoundingClientRect().height;
      section.classList.add("is-resizing");
      e.preventDefault();
    });

    window.addEventListener("mousemove", (e) => {
      if (!dragging) return;
      onMove(e.clientY);
    });

    window.addEventListener("mouseup", () => {
      if (!dragging) return;
      dragging = false;
      section.classList.remove("is-resizing");
    });
  }

  if (els.chkShowConsole) {
    els.chkShowConsole.onchange = () => {
      updateConsoleModeHint(els.chkShowConsole.checked);
      Bridge.send("saveRunSettings", { showConsoleWindow: els.chkShowConsole.checked });
    };
  }
  if (els.logFilterBtn) {
    els.logFilterBtn.onclick = (e) => {
      e.stopPropagation();
      toggleLogFilterMenu();
    };
  }
  document.addEventListener("click", () => closeLogFilterMenu());
  if (els.logFilter) {
    els.logFilter.addEventListener("click", (e) => e.stopPropagation());
  }
  els.globalAddEnv.onclick = () => addEnvRow(els.globalEnvContainer, "", "");
  els.modalAddEnv.onclick = () => addEnvRow(els.modalEnvContainer, "", "");
  els.modalClose.onclick = closeModal;
  els.settingsModal.onclick = (e) => { if (e.target === els.settingsModal) closeModal(); };

  els.modalSave.onclick = () => {
    if (!state.modalServiceId) return;
    Bridge.send("saveConfig", {
      serviceId: state.modalServiceId,
      platformId: state.platformId,
      config: collectModalConfig()
    });
    closeModal();
  };

  els.btnSaveGlobal.onclick = () => {
    Bridge.send("saveGlobalConfig", {
      platformId: state.platformId,
      category: state.category,
      config: collectGlobalConfig()
    });
  };

  els.btnExportConfig.onclick = () => {
    Bridge.send("exportConfig", { platformId: state.platformId });
  };

  els.btnImportConfig.onclick = () => {
    hideImportPreview();
    Bridge.send("previewImport", { platformId: state.platformId });
  };

  els.btnScanLocal.onclick = () => {
    Bridge.send("scanLocalDefaults", { platformId: state.platformId });
  };

  els.btnApplyLocal.onclick = () => {
    Bridge.send("applyLocalDefaults", { platformId: state.platformId });
  };

  els.btnApplyImport.onclick = () => {
    if (!pendingImportPath) return;
    Bridge.send("applyImport", { platformId: state.platformId, filePath: pendingImportPath });
    hideImportPreview();
  };

  els.btnCancelImport.onclick = hideImportPreview;

  els.btnOpenWorkspace.onclick = () => switchView("workspace");

  if (els.btnDownloadUpdate) {
    els.btnDownloadUpdate.onclick = () => {
      if (!state.updateInfo?.downloadUrl) return;
      els.btnDownloadUpdate.disabled = true;
      els.btnDownloadUpdate.textContent = t("update.downloading");
      Bridge.send("applyUpdate", { filePath: state.updateInfo.downloadUrl });
    };
  }
  if (els.btnCheckUpdate) {
    els.btnCheckUpdate.onclick = () => Bridge.send("checkUpdate");
  }
  if (els.btnDismissUpdate) {
    els.btnDismissUpdate.onclick = () => hideUpdateBanner(true);
  }

  els.btnSaveWorkspace.onclick = () => {
    Bridge.send("saveWorkspacePaths", { paths: collectWorkspacePaths() });
  };

  Bridge.on("init", (payload) => {
    state.platformId = payload.activePlatformId;
    state.platforms = payload.platforms || [];
    state.appVersion = payload.appVersion || "";
    if (payload.appTitle) document.title = payload.appTitle;
    if (els.appVersionBadge && state.appVersion) {
      els.appVersionBadge.textContent = `v${state.appVersion}`;
    }
    if (payload.workspace) {
      state.workspace = payload.workspace;
      renderWorkspaceBanner(payload.workspace);
    }
    if (payload.runSettings && els.chkShowConsole) {
      els.chkShowConsole.checked = !!payload.runSettings.showConsoleWindow;
      updateConsoleModeHint(els.chkShowConsole.checked);
    }
    renderPlatforms();
    applyTheme();
    initConsoleResize();
    updateNavContext();
  });

  Bridge.on("updateAvailable", (info) => {
    showUpdateBanner(info);
  });

  Bridge.on("platformChanged", (payload) => {
    state.platformId = payload.platformId;
    renderPlatforms();
  });

  Bridge.on("services", (payload) => {
    if (payload.platformId) state.platformId = payload.platformId;
    state.services = { backEnd: payload.backEnd || [], frontEnd: payload.frontEnd || [] };
    state.dashboardSig = "";
    state.dashboardStatusSig = "";
    renderPlatforms();
    renderDashboard();
  });

  Bridge.on("serviceDetail", (detail) => openModal(detail));

  Bridge.on("globalConfig", (payload) => renderGlobalConfig(payload));

  Bridge.on("backupPreview", renderBackupPreview);

  Bridge.on("workspacePaths", renderWorkspacePaths);

  Bridge.on("workspaceFolderPicked", (payload) => {
    if (payload.workspace) renderWorkspaceBanner(payload.workspace);
    if (payload.pathKey) {
      const input = els.workspacePathList.querySelector(`input[data-key="${payload.pathKey}"]`);
      if (input && payload.path) input.value = payload.path;
    }
    Bridge.send("loadWorkspacePaths");
  });

  Bridge.on("importPreview", showImportPreview);

  Bridge.on("importResult", () => {
    hideImportPreview();
    loadBackupPreview();
  });

  Bridge.on("runSettings", (payload) => {
    if (els.chkShowConsole) {
      els.chkShowConsole.checked = !!payload.showConsoleWindow;
      updateConsoleModeHint(els.chkShowConsole.checked);
    }
  });

  Bridge.on("log", appendLog);

  Bridge.on("buildProgress", setBuildProgress);
  Bridge.on("runProgress", setRunProgress);

  applyTheme();
  initConsoleResize();
})();
