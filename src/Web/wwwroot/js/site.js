// The small behaviours every page shares: numbers that count up on arrival, buttons that show they are working and
// cannot be pressed twice, a confirm step before anything hard to undo, toasts, tips that can be put away, "/" to search,
// a password that can be shown, and drawings that rest while off screen. Everything here is an extra: each page works
// without it.
(() => {
    const root = document.documentElement;
    const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
    const icon = paths => `<svg class="icon" viewBox="0 0 24 24" aria-hidden="true">${paths}</svg>`;
    const icons = {
        alert: '<path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/>',
        help: '<circle cx="12" cy="12" r="10"/><path d="M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3"/><line x1="12" y1="17" x2="12.01" y2="17"/>',
        x: '<line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>',
        eye: '<path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>',
        eyeOff: '<path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94"/><path d="M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19"/><line x1="1" y1="1" x2="23" y2="23"/>'
    };

    // ---------- Numbers count up on first paint ----------
    const countUp = element => {
        const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
        let node;
        while ((node = walker.nextNode())) {
            const match = node.nodeValue.match(/\d[\d,]*/);
            if (!match) {
                continue;
            }

            const target = Number(match[0].replace(/,/g, ""));
            if (!Number.isFinite(target) || target < 2) {
                return;
            }

            const before = node.nodeValue.slice(0, match.index);
            const after = node.nodeValue.slice(match.index + match[0].length);
            const grouped = match[0].includes(",") || target >= 1000;
            const text = value => before + (grouped ? value.toLocaleString("en-US") : String(value)) + after;
            const textNode = node;
            const start = performance.now();
            const duration = Math.min(1100, 500 + target * 4);
            const step = now => {
                const t = Math.min(1, (now - start) / duration);
                textNode.nodeValue = text(Math.round(target * (1 - Math.pow(1 - t, 3))));
                if (t < 1) {
                    requestAnimationFrame(step);
                }
            };
            textNode.nodeValue = text(0);
            requestAnimationFrame(step);

            return;
        }
    };
    if (!reduced && root.classList.contains("js-enter")) {
        document.querySelectorAll(".stat-value, .flow-count, .cash-card .value, [data-count]").forEach(countUp);
    }

    // ---------- Drawings move only while on screen; parts below the fold rise in the first time they are seen ----------
    if ("IntersectionObserver" in window) {
        const motion = new IntersectionObserver(entries => entries.forEach(entry => {
            entry.target.classList.toggle("paused", !entry.isIntersecting);
            if (entry.isIntersecting) {
                entry.target.classList.add("revealed");
            }
        }));
        document.querySelectorAll("[data-motion]").forEach(element => motion.observe(element));

        if (!reduced) {
            const reveal = new IntersectionObserver(entries => entries.forEach(entry => {
                if (entry.isIntersecting) {
                    entry.target.classList.add("revealed");
                    reveal.unobserve(entry.target);
                }
            }), { rootMargin: "0px 0px -8% 0px" });
            document.querySelectorAll("[data-reveal]").forEach(element => {
                if (element.getBoundingClientRect().top > innerHeight) {
                    element.classList.add("reveal-wait");
                    reveal.observe(element);
                }
            });
        }
    }

    // ---------- The top bar gets a line once the page scrolls ----------
    const topbar = document.querySelector(".topbar");
    if (topbar) {
        const onScroll = () => topbar.classList.toggle("scrolled", scrollY > 4);
        addEventListener("scroll", onScroll, { passive: true });
        onScroll();
    }

    // ---------- "/" jumps to the search box ----------
    addEventListener("keydown", event => {
        if (event.key !== "/" || event.ctrlKey || event.metaKey || event.altKey) {
            return;
        }

        const typing = event.target.closest?.("input, textarea, select, [contenteditable]");
        const search = document.querySelector(".topbar-search input");
        if (!typing && search && search.offsetParent !== null) {
            event.preventDefault();
            search.focus();
            search.select();
        }
    });

    // ---------- A confirm step before anything hard to undo ----------
    let dialog;
    const ask = (message, title, ok, danger) => new Promise(resolve => {
        if (!dialog) {
            dialog = document.createElement("dialog");
            dialog.className = "confirm";
            dialog.innerHTML = `
                <form method="dialog">
                    <div class="confirm-body">
                        <span class="stat-icon"></span>
                        <div><h2></h2><p></p></div>
                    </div>
                    <div class="confirm-actions">
                        <button class="btn btn-outline" value="no">Go back</button>
                        <button class="btn" value="yes"></button>
                    </div>
                </form>`;
            document.body.append(dialog);
        }

        const badge = dialog.querySelector(".stat-icon");
        badge.className = "stat-icon " + (danger ? "red" : "amber");
        badge.innerHTML = icon(danger ? icons.alert : icons.help);
        dialog.querySelector("h2").textContent = title;
        dialog.querySelector("p").textContent = message;
        const yes = dialog.querySelector("[value=yes]");
        yes.textContent = ok;
        yes.className = "btn " + (danger ? "btn-danger-solid" : "btn-primary");
        dialog.returnValue = "";
        dialog.addEventListener("close", () => resolve(dialog.returnValue === "yes"), { once: true });
        dialog.showModal();
        yes.focus();
    });

    document.addEventListener("submit", event => {
        const form = event.target;
        const source = event.submitter?.dataset.confirm !== undefined ? event.submitter : form;
        if (source.dataset.confirm === undefined || form.dataset.confirmed === "yes") {
            delete form.dataset.confirmed;
            return;
        }

        event.preventDefault();
        event.stopImmediatePropagation();
        const submitter = event.submitter;
        ask(
            source.dataset.confirm,
            source.dataset.confirmTitle || "Are you sure?",
            source.dataset.confirmOk || "Yes, go ahead",
            source.dataset.confirmTone !== "calm").then(yes => {
                if (yes) {
                    form.dataset.confirmed = "yes";
                    form.requestSubmit(submitter?.form === form ? submitter : undefined);
                }
            });
    }, true);

    // ---------- Buttons show they are working, and a form is sent once ----------
    document.addEventListener("submit", event => {
        const form = event.target;
        if (event.defaultPrevented || (form.method || "").toLowerCase() !== "post" || form.hasAttribute("data-no-busy")) {
            return;
        }

        if (form.dataset.sending === "yes") {
            event.preventDefault();
            return;
        }

        form.dataset.sending = "yes";
        const button = event.submitter?.classList.contains("btn") ? event.submitter : null;
        if (button) {
            // A button's own name and value must still be sent, so it is only styled, not disabled
            button.classList.add("is-busy");
            button.setAttribute("aria-busy", "true");
        }

        setTimeout(() => {
            delete form.dataset.sending;
            button?.classList.remove("is-busy");
            button?.removeAttribute("aria-busy");
        }, 10000);
    });
    addEventListener("pageshow", event => {
        if (event.persisted) {
            document.querySelectorAll("form[data-sending]").forEach(form => delete form.dataset.sending);
            document.querySelectorAll(".is-busy").forEach(button => button.classList.remove("is-busy"));
        }
    });

    // ---------- Toasts: success fades away by itself, a problem stays until closed ----------
    const toasts = document.querySelector(".toasts");
    if (toasts) {
        document.body.append(toasts);
        for (const toast of toasts.querySelectorAll(".toast")) {
            const close = () => {
                toast.classList.add("leaving");
                setTimeout(() => toast.remove(), 300);
            };
            toast.querySelector(".toast-close")?.addEventListener("click", close);
            const timer = toast.querySelector(".toast-timer");
            if (timer && reduced) {
                // Without motion the timer bar does not run: close on the clock instead
                setTimeout(close, 8000);
            } else {
                timer?.addEventListener("animationend", close);
            }
        }
    }

    // ---------- Tips can be put away; the choice is remembered on this browser ----------
    const remembered = key => {
        try {
            return localStorage.getItem(key) === "1";
        } catch {
            return false;
        }
    };
    for (const tip of document.querySelectorAll(".tip[data-tip]")) {
        const key = "tip:" + tip.dataset.tip;
        if (remembered(key)) {
            tip.classList.add("dismissed");
            continue;
        }

        const close = document.createElement("button");
        close.type = "button";
        close.className = "tip-close";
        close.title = "Hide this tip";
        close.setAttribute("aria-label", "Hide this tip");
        close.innerHTML = icon(icons.x);
        close.addEventListener("click", () => {
            tip.animate([{ opacity: 1, transform: "none" }, { opacity: 0, transform: "translateY(-6px)" }], { duration: 220, easing: "ease-out" })
                .finished.then(() => tip.classList.add("dismissed"));
            try {
                localStorage.setItem(key, "1");
            } catch {
                // Private windows may refuse: the tip simply shows again next time
            }
        });
        tip.append(close);
    }

    // ---------- Show the password while typing ----------
    for (const input of document.querySelectorAll("input[type=password][data-reveal]")) {
        const wrap = document.createElement("div");
        wrap.className = "password-field";
        input.before(wrap);
        wrap.append(input);
        const toggle = document.createElement("button");
        toggle.type = "button";
        toggle.className = "password-toggle";
        toggle.setAttribute("aria-label", "Show password");
        toggle.innerHTML = icon(icons.eye);
        toggle.addEventListener("click", () => {
            const shown = input.type === "text";
            input.type = shown ? "password" : "text";
            toggle.innerHTML = icon(shown ? icons.eye : icons.eyeOff);
            toggle.setAttribute("aria-label", shown ? "Show password" : "Hide password");
            input.focus();
        });
        wrap.append(toggle);
    }

    // ---------- Copy a tracking code ----------
    document.addEventListener("click", async event => {
        const button = event.target.closest("[data-copy]");
        if (!button) {
            return;
        }

        try {
            await navigator.clipboard.writeText(button.dataset.copy);
            const was = button.title;
            button.title = "Copied";
            button.classList.add("copied");
            setTimeout(() => {
                button.title = was;
                button.classList.remove("copied");
            }, 1500);
        } catch {
            // Clipboard refused (not a secure page): nothing to do
        }
    });

    // ---------- Close the phone menu when a link in it is followed ----------
    const navOpen = document.getElementById("nav-open");
    document.querySelector(".sidebar")?.addEventListener("click", event => {
        if (navOpen && event.target.closest("a")) {
            navOpen.checked = false;
        }
    });
    addEventListener("keydown", event => {
        if (event.key === "Escape" && navOpen?.checked) {
            navOpen.checked = false;
        }
    });
})();
