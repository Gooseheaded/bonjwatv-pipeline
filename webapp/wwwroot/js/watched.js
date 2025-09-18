// Client-side watched cache + UI helpers
(function(){
  const STORAGE_KEY = 'bwkt_watched_v1';
  const STALE_MS = 12 * 60 * 60 * 1000; // 12h

  function loadCache() {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return { ids: new Set(), ts: 0 };
      const obj = JSON.parse(raw);
      const ids = new Set(Array.isArray(obj.ids) ? obj.ids : []);
      const ts = typeof obj.ts === 'number' ? obj.ts : 0;
      return { ids, ts };
    } catch { return { ids: new Set(), ts: 0 }; }
  }

  function saveCache(ids) {
    const arr = Array.from(ids);
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ ids: arr, ts: Date.now() }));
  }

  function markWatched(videoId) {
    const cache = loadCache();
    cache.ids.add(videoId);
    saveCache(cache.ids);
  }

  async function hydrateFromServerIfStale() {
    const cache = loadCache();
    const stale = Date.now() - cache.ts > STALE_MS || cache.ids.size === 0;
    if (!stale) return cache;
    try {
      const res = await fetch('/account/watched', { credentials: 'same-origin' });
      if (res.ok) {
        const data = await res.json();
        const ids = new Set(Array.isArray(data.ids) ? data.ids : []);
        saveCache(ids);
        return { ids, ts: Date.now() };
      }
    } catch {}
    return cache; // fallback
  }

  // Force-refresh from server (used after marking to ensure immediate consistency)
  async function refreshFromServer() {
    try {
      const res = await fetch('/account/watched', { credentials: 'same-origin' });
      if (res.ok) {
        const data = await res.json();
        const ids = new Set(Array.isArray(data.ids) ? data.ids : []);
        saveCache(ids);
        annotateCards();
        return { ids, ts: Date.now() };
      }
    } catch {}
    return loadCache();
  }

  function annotateCards() {
    const { ids } = loadCache();
    document.querySelectorAll('.video-card[data-video-id]').forEach(card => {
      const id = card.getAttribute('data-video-id');
      const watched = ids.has(id);
      card.classList.toggle('watched', watched);
      const badge = card.querySelector('.watched-badge');
      if (badge) {
        badge.classList.toggle('d-none', !watched);
      }
    });
  }

  // Update overlays in other tabs when localStorage changes
  window.addEventListener('storage', (e) => {
    if (e.key === STORAGE_KEY) {
      try { annotateCards(); } catch {}
    }
  });

  // Expose minimal API
  window.bwktWatched = {
    markWatched,
    annotateCards,
    hydrateFromServerIfStale,
    refreshFromServer
  };
})();
