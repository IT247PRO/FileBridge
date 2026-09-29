// Keeps the dashboard current without a full page reload. Plain polling, not SignalR/websockets: the Admin
// tier can run as multiple nodes behind a load balancer, and a push-based approach would need a backplane
// to fan out across them -- not worth it here since a few seconds of staleness costs nothing on a dashboard.
// No inline script (CSP script-src 'self').
(function () {
    'use strict';
    const container = document.getElementById('dashboard-body');
    if (!container) return;

    const REFRESH_MS = 5000;
    let inFlight = false;

    async function refresh() {
        if (document.hidden || inFlight) return;
        inFlight = true;
        try {
            const r = await fetch('/Home/Refresh', { credentials: 'same-origin' });
            if (r.ok) container.innerHTML = await r.text();
        } catch (e) { /* transient network hiccup; next tick tries again */ }
        finally { inFlight = false; }
    }

    setInterval(refresh, REFRESH_MS);
})();
