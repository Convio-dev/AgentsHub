// Bridge RPC Communication Helper
  // Bridge RPC Communication Helper
  function callBridge(action, payload = {}) {
    return new Promise((resolve, reject) => {
      if (!window.chrome || !window.chrome.webview) {
        console.warn('WebView2 environment not detected.');
        resolve({});
        return;
      }
      const requestId = 'req_' + Math.random().toString(36).substring(2, 9);
      const timeout = setTimeout(() => {
        state.pendingRequests.delete(requestId);
        reject(new Error(`Request ${action} timed out.`));
      }, 30000);

      state.pendingRequests.set(requestId, { resolve, reject, timeout });
      window.chrome.webview.postMessage({ action, ...payload, requestId });
    });
  }

window.callBridge = callBridge;
var callBridge = window.callBridge;
