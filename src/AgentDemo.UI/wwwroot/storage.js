export function get(key) {
    try {
        return window.localStorage.getItem(key);
    } catch {
        return null;
    }
}

export function setReducedEffects(on) {
    try {
        document.documentElement.classList.toggle("fx-off-root", !!on);
    } catch {
    }
}

export function set(key, value) {
    try {
        window.localStorage.setItem(key, value);
        return true;
    } catch {
        return false;
    }
}
