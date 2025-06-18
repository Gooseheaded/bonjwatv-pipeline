// Minimal English-only SRT parser and subtitle overlay
function parseSrt(data) {
    const cues = [];
    // Match index, allow two timestamp styles (HH:MM:SS,mmm or seconds.mmm), then text; support CRLF or LF
    const pattern = /\d+\s+((?:\d{2}:\d{2}:\d{2},\d{3})|(?:\d+(?:\.\d+)?))\s*-->\s*((?:\d{2}:\d{2}:\d{2},\d{3})|(?:\d+(?:\.\d+)?))\s*([\s\S]*?)(?=(?:\r?\n){2}|$)/g;
    let match;
    while ((match = pattern.exec(data)) !== null) {
        cues.push({
            start: toSeconds(match[1]),
            end: toSeconds(match[2]),
            text: match[3].trim().replace(/\r?\n/g, '<br>')
        });
    }
    return cues;
}

function toSeconds(ts) {
    // Allow plain seconds.milliseconds (e.g. "0.000") or HH:MM:SS,mmm
    if (/^\d+(?:\.\d+)?$/.test(ts)) {
        return parseFloat(ts);
    }
    const parts = ts.split(/[:,]/).map(Number);
    const [h, m, s, ms] = parts;
    return h * 3600 + m * 60 + s + ms / 1000;
}

/**
 * Fetch text from URL or data URI.
 */
function fetchText(url) {
    if (url.startsWith('data:')) {
        const [meta, data] = url.substring(5).split(',', 2);
        if (meta.endsWith(';base64')) {
            return Promise.resolve(atob(data));
        } else {
            return Promise.resolve(decodeURIComponent(data));
        }
    }
    return fetch(url).then(res => {
        if (!res.ok) throw new Error(`Failed to load subtitles: ${res.status} ${res.statusText}`);
        return res.text();
    });
}

function initSubtitles(srtUrl, playerId, containerId) {
    let cues = [];
    let player;
    let currentIndex = -1;
    const container = document.getElementById(containerId);

    fetchText(srtUrl)
        .then(text => {
            try {
                cues = parseSrt(text);
                if (!cues.length) {
                    if (container) container.innerText =
                        'No subtitles cues parsed. Text preview:\n' + text.slice(0, 200);
                    return;
                }
                waitForPlayerAPI();
            } catch (err) {
                console.error('[subtitles] Error parsing SRT:', err);
                if (container) container.innerText = 'Error parsing subtitles: ' + err.message;
            }
        })
        .catch(err => {
            console.error('Error loading SRT:', err);
            if (container) container.innerText = 'Error loading subtitles: ' + err.message;
        });

    function waitForPlayerAPI() {
        if (window.YT && YT.Player) {
            try {
                // Instantiate YouTube player using existing iframe (enablejsapi=1)
                player = new YT.Player(playerId, { events: { onReady: () => requestAnimationFrame(render) } });
            } catch (err) {
                console.error('[subtitles] Error initializing YouTube player:', err);
                if (container) container.innerText = 'Error initializing player: ' + err.message;
                setTimeout(waitForPlayerAPI, 100);
            }
        } else {
            setTimeout(waitForPlayerAPI, 100);
        }
    }

    function render() {
        if (player && cues.length && container) {
            const time = player.getCurrentTime();
            const idx = cues.findIndex(c => time >= c.start && time <= c.end);
            if (idx !== currentIndex) {
                currentIndex = idx;
                container.innerHTML = idx >= 0 ? cues[idx].text : '';
            }
        }
        requestAnimationFrame(render);
    }
}

// Export for Node.js-based parser tests
if (typeof module !== 'undefined' && module.exports) {
    module.exports = { parseSrt, toSeconds };
}