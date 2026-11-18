'use strict';
// Windows shell shim. Mirrors the injected user scripts and the WebKit bridge
// of the macOS shell: engine events arrive over SSE and are dispatched to
// window.weiboReceive; UI requests go through window.weiboBrowserHarness.
(function () {
    window.weiboErrors = [];
    window.addEventListener('error', e => window.weiboErrors.push(String(e.message)));
    window.addEventListener('unhandledrejection', e => window.weiboErrors.push(String(e.reason)));
    if (typeof window.weiboAppInfo?.version !== 'string') window.weiboAppInfo = { version: document.documentElement.getAttribute('data-app-version') || '' };

    const pending = [];
    let source = null;
    function deliver(event) {
        if (typeof window.weiboReceive !== 'function') { pending.push(event); return; }
        try { window.weiboReceive(event); }
        catch (error) { window.weiboErrors.push(String(error && error.message || error)); }
    }
    const drain = setInterval(() => {
        if (typeof window.weiboReceive !== 'function' || !pending.length) return;
        while (pending.length) deliver(pending.shift());
    }, 10);
    setTimeout(() => clearInterval(drain), 30000);

    window.weiboBrowserHarness = function (value) {
        return fetch('/bridge', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(value) }).catch(error => {
            window.weiboErrors.push(String(error && error.message || error));
        });
    };
    function connect() {
        source = new EventSource('/events');
        source.onmessage = m => { try { deliver(JSON.parse(m.data)); } catch {} };
        source.onerror = () => { /* EventSource reconnects automatically */ };
    }
    connect();
})();
