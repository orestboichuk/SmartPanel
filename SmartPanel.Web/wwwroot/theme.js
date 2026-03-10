window.smartPanelTheme = {
    getTheme: function () {
        const saved = localStorage.getItem("smartpanel-theme");
        if (saved === "light" || saved === "dark") {
            return saved;
        }

        return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
    },
    applyTheme: function (theme) {
        document.documentElement.setAttribute("data-theme", theme);
    },
    saveTheme: function (theme) {
        localStorage.setItem("smartpanel-theme", theme);
    }
};
