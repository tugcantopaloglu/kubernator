(function () {
    var content = document.querySelector('main.content');
    if (!content) return;
    var iso = content.getAttribute('data-session-expires');
    if (!iso) return;
    var banner = document.getElementById('session-banner');
    var text = document.getElementById('session-banner-text');
    if (!banner || !text) return;
    var expiresAt = Date.parse(iso);
    if (isNaN(expiresAt)) return;

    function fmt(ms) {
        if (ms <= 0) return 'expired';
        var totalSec = Math.floor(ms / 1000);
        var min = Math.floor(totalSec / 60);
        var sec = totalSec % 60;
        if (min >= 60) {
            var hr = Math.floor(min / 60);
            return hr + 'h ' + (min - hr * 60) + 'm';
        }
        return min + 'm ' + sec.toString().padStart(2, '0') + 's';
    }

    function tick() {
        var remaining = expiresAt - Date.now();
        if (remaining <= 0) {
            text.textContent = 'session expired — sign in again to continue.';
            banner.hidden = false;
            return;
        }
        if (remaining <= 5 * 60 * 1000) {
            text.textContent = 'session ends in ' + fmt(remaining) + '. any unsaved work in editors will be lost on the next request.';
            banner.hidden = false;
        }
    }

    tick();
    setInterval(tick, 30000);
})();
