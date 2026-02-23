(function () {
  const button = document.getElementById('theater-toggle');
  const shell = document.getElementById('player-theater-shell');
  const backdrop = document.getElementById('theater-backdrop');
  if (!button || !shell || !backdrop || button.disabled) return;

  let isActive = false;
  let savedScrollY = 0;
  let enteredAtMs = 0;

  const enterLabel = button.dataset.labelEnter || 'Theater Mode';
  const exitLabel = button.dataset.labelExit || 'Exit Theater';
  const videoId = button.dataset.videoId || '';

  button.addEventListener('click', () => {
    if (isActive) {
      exitTheaterMode('button');
    } else {
      enterTheaterMode();
    }
  });

  backdrop.addEventListener('click', () => {
    if (isActive) exitTheaterMode('backdrop');
  });

  document.addEventListener('keydown', (event) => {
    if (!isActive) return;
    if (event.key === 'Escape') {
      event.preventDefault();
      exitTheaterMode('escape');
    }
  });

  window.addEventListener('pagehide', () => {
    if (!isActive) return;
    // Ensure scroll/page state is restored when navigating away mid-theater mode.
    cleanupTheaterClasses();
    try { window.scrollTo(0, savedScrollY); } catch {}
    isActive = false;
  });

  function enterTheaterMode() {
    if (isActive) return;
    isActive = true;
    savedScrollY = window.scrollY || window.pageYOffset || 0;
    enteredAtMs = Date.now();

    backdrop.hidden = false;
    backdrop.setAttribute('aria-hidden', 'false');
    shell.classList.add('theater-active', 'theater-entering');
    document.body.classList.add('theater-mode-active');
    button.classList.add('active');
    button.setAttribute('aria-pressed', 'true');
    button.textContent = exitLabel;

    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        shell.classList.remove('theater-entering');
        backdrop.classList.add('show');
      });
    });

    track('theater_mode_enter');
  }

  function exitTheaterMode(reason) {
    if (!isActive) return;
    isActive = false;

    backdrop.classList.remove('show');
    shell.classList.remove('theater-active', 'theater-entering');
    document.body.classList.remove('theater-mode-active');
    button.classList.remove('active');
    button.setAttribute('aria-pressed', 'false');
    button.textContent = enterLabel;

    const durationMs = enteredAtMs > 0 ? Math.max(0, Date.now() - enteredAtMs) : undefined;
    enteredAtMs = 0;

    window.setTimeout(() => {
      if (!isActive) {
        backdrop.hidden = true;
        backdrop.setAttribute('aria-hidden', 'true');
      }
    }, 180);

    try { window.scrollTo(0, savedScrollY); } catch {}

    track('theater_mode_exit', {
      reason: reason || 'unknown',
      duration_ms: durationMs
    });
  }

  function cleanupTheaterClasses() {
    backdrop.classList.remove('show');
    backdrop.hidden = true;
    backdrop.setAttribute('aria-hidden', 'true');
    shell.classList.remove('theater-active', 'theater-entering');
    document.body.classList.remove('theater-mode-active');
    button.classList.remove('active');
    button.setAttribute('aria-pressed', 'false');
    button.textContent = enterLabel;
  }

  function track(eventName, extra) {
    try {
      if (!window.gtag) return;
      window.gtag('event', eventName, Object.assign({
        video_id: videoId,
        page_path: window.location ? window.location.pathname : '',
        page_title: document.title || ''
      }, extra || {}));
    } catch {}
  }
})();
