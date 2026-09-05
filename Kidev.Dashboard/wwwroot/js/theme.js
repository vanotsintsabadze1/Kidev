(() => {
    "use strict";
    try {
        const theme = localStorage.getItem("kidev.theme");
        if (theme === "light" || theme === "dark") {
            document.documentElement.dataset.theme = theme;
        }
    } catch {
        // Storage may be unavailable in restricted browser contexts.
    }
})();
