const Bridge = (() => {
  const handlers = new Map();

  function send(action, data = {}) {
    const message = { action, ...data };
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(message);
    } else {
      console.debug("[Bridge mock]", message);
    }
  }

  function on(type, handler) {
    handlers.set(type, handler);
  }

  function init() {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.addEventListener("message", (event) => {
        try {
          const msg = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
          const handler = handlers.get(msg.type);
          if (handler) handler(msg.payload);
        } catch (err) {
          console.error("Bridge parse error", err);
        }
      });
    }
  }

  return { send, on, init };
})();

Bridge.init();
