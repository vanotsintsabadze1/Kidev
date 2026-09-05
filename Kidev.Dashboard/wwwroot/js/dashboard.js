(() => {
    "use strict";
    const root = document.documentElement;
    root.classList.add("js");

    const themeButtons = document.querySelectorAll("[data-theme-toggle]");
    function updateThemeButtons() {
        const light = root.dataset.theme === "light";
        themeButtons.forEach(button => {
            button.hidden = false;
            button.querySelector("[data-button-label]").textContent = light ? "Dark theme" : "Light theme";
            button.setAttribute("aria-label", `Switch to ${light ? "dark" : "light"} theme`);
        });
    }
    themeButtons.forEach(button => button.addEventListener("click", () => {
        root.dataset.theme = root.dataset.theme === "light" ? "dark" : "light";
        try {
            localStorage.setItem("kidev.theme", root.dataset.theme);
        } catch {
            // The current page still supports theme switching without persistence.
        }
        updateThemeButtons();
    }));
    updateThemeButtons();

    const navButton = document.querySelector("[data-nav-toggle]");
    const navigation = document.querySelector("#navigation");
    if (navButton && navigation) {
        navButton.hidden = false;
        const setNavigation = open => {
            document.body.classList.toggle("nav-open", open);
            navButton.setAttribute("aria-expanded", String(open));
            navButton.querySelector("[data-button-label]").textContent = open ? "Close menu" : "Menu";
        };
        navButton.addEventListener("click", () => {
            const open = navButton.getAttribute("aria-expanded") !== "true";
            setNavigation(open);
            if (open) navigation.querySelector(".is-current, a")?.focus();
        });
        document.addEventListener("keydown", event => {
            if (event.key === "Escape" && document.body.classList.contains("nav-open")) {
                setNavigation(false);
                navButton.focus();
            }
        });
        matchMedia("(min-width: 768px)").addEventListener("change", () => setNavigation(false));
    }

    // Factory owns snapshot polling and must never enter the page-reload loop.
    if (document.querySelector("[data-factory]")) return;

    // Only snapshot pages expose refresh controls. Account and settings forms never reload.
    const controls = document.querySelector("[data-refresh-controls]");
    const refreshButton = document.querySelector("[data-refresh]");
    const autoButton = document.querySelector("[data-auto-refresh]");
    const refreshStatus = document.querySelector("[data-refresh-status]");
    if (!controls || !refreshButton || !autoButton || !refreshStatus) return;
    controls.hidden = false;

    let autoRefresh = false;
    let dirty = false;
    let submitting = false;
    let nextRefresh = Date.now() + 15000;
    try {
        autoRefresh = sessionStorage.getItem("kidev.autoRefresh") === "true";
    } catch {
        // Auto-refresh remains usable for this page without session storage.
    }

    function pauseReason() {
        if (submitting) return "Navigation in progress";
        if (dirty) return "Filters changed; apply or clear to refresh";
        if (document.hidden) return "Tab hidden";
        if (document.activeElement?.closest("form, input, select, textarea, [contenteditable='true']")) return "Form in use";
        if (document.querySelector("details[open]")) return "Execution details open";
        if (document.body.classList.contains("nav-open")) return "Navigation open";
        if (window.getSelection()?.toString()) return "Text selected";
        return "";
    }

    function updateRefreshStatus() {
        autoButton.textContent = autoRefresh ? "Pause auto-refresh" : "Auto-refresh: off";
        autoButton.setAttribute("aria-pressed", String(autoRefresh));
        const reason = pauseReason();
        refreshStatus.textContent = reason ? `Refresh paused: ${reason}` : autoRefresh ? "Auto-refresh every 15s" : "Manual refresh";
    }

    autoButton.addEventListener("click", () => {
        autoRefresh = !autoRefresh;
        nextRefresh = Date.now() + 15000;
        try {
            sessionStorage.setItem("kidev.autoRefresh", String(autoRefresh));
        } catch {
            // Persistence is optional; never prevent the pause control from working.
        }
        updateRefreshStatus();
    });

    function refresh() {
        if (submitting || dirty) {
            updateRefreshStatus();
            return;
        }
        submitting = true;
        refreshButton.disabled = true;
        refreshButton.querySelector("[data-button-label]").textContent = "Refreshing...";
        refreshStatus.textContent = "Requesting a new snapshot...";
        window.location.reload();
    }
    refreshButton.addEventListener("click", refresh);
    document.querySelectorAll("form").forEach(form => {
        form.addEventListener("input", () => { dirty = true; updateRefreshStatus(); });
        form.addEventListener("change", () => { dirty = true; updateRefreshStatus(); });
        form.addEventListener("submit", () => { submitting = true; updateRefreshStatus(); });
    });
    document.addEventListener("visibilitychange", () => {
        nextRefresh = Date.now() + 15000;
        updateRefreshStatus();
    });
    document.addEventListener("focusin", updateRefreshStatus);
    document.addEventListener("focusout", () => setTimeout(updateRefreshStatus, 0));
    document.querySelectorAll("details").forEach(details => details.addEventListener("toggle", updateRefreshStatus));
    window.addEventListener("pageshow", () => {
        submitting = false;
        refreshButton.disabled = false;
        refreshButton.querySelector("[data-button-label]").textContent = "Refresh";
        nextRefresh = Date.now() + 15000;
        updateRefreshStatus();
    });
    setInterval(() => {
        if (!autoRefresh || pauseReason()) {
            nextRefresh = Date.now() + 15000;
            updateRefreshStatus();
            return;
        }
        if (Date.now() >= nextRefresh) refresh();
    }, 1000);
    updateRefreshStatus();
})();
