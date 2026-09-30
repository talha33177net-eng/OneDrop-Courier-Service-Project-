// Live dashboards: the parts of a page marked data-live="name" are read again whenever the operator's data changes.
// The server only says "changed"; the page fetches itself through its normal, authorised request and swaps the parts.
(() => {
    const status = document.querySelector("[data-live-status]");
    if (!document.querySelector("[data-live]") || !window.signalR) {
        return;
    }

    const show = (state, text) => {
        if (status) {
            status.dataset.state = state;
            status.textContent = text;
        }
    };

    let timer = null;
    let busy = false;
    let again = false;

    const refresh = async () => {
        if (busy) {
            again = true;
            return;
        }

        busy = true;
        try {
            const response = await fetch(location.href, { credentials: "same-origin", headers: { "X-Live": "1" } });
            if (!response.ok || response.redirected) {
                return;
            }

            const fresh = new DOMParser().parseFromString(await response.text(), "text/html");
            for (const part of document.querySelectorAll("[data-live]")) {
                const next = fresh.querySelector(`[data-live="${part.dataset.live}"]`);
                if (!next) {
                    continue;
                }

                // Keep what the reader had unfolded
                for (const open of part.querySelectorAll("details[open][id]")) {
                    next.querySelector(`details[id="${open.id}"]`)?.setAttribute("open", "");
                }

                part.replaceWith(next);
            }

            show("live", "Live · updated " + new Date().toLocaleTimeString([], { hour: "numeric", minute: "2-digit" }));
        } finally {
            busy = false;
            if (again) {
                again = false;
                refresh();
            }
        }
    };

    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/operations")
        .withAutomaticReconnect()
        .build();
    connection.on("changed", () => {
        clearTimeout(timer);
        timer = setTimeout(refresh, 300);
    });
    connection.onreconnecting(() => show("waiting", "Reconnecting…"));
    connection.onreconnected(() => {
        show("live", "Live");
        refresh();
    });
    connection.onclose(() => show("off", "Not live: reload the page"));
    connection.start().then(() => show("live", "Live"), () => show("off", "Not live: reload the page"));
})();
