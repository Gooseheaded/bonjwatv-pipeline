(function () {
  const trigger = document.getElementById('open-correction');
  const modalEl = document.getElementById('correctionModal');
  if (!trigger || !modalEl) return;
  const videoId = trigger.dataset.videoId;
  const subtitleVersion = parseInt(trigger.dataset.subtitleVersion || '1', 10) || 1;
  let modalInstance = null;
  const cueContainer = document.getElementById('correction-cues');
  const timestampEl = document.getElementById('correction-timestamp');
  const errorEl = document.getElementById('correction-error');
  const successEl = document.getElementById('correction-success');
  const form = document.getElementById('correction-form');
  const notesInput = document.getElementById('correction-notes');

  trigger.addEventListener('click', () => {
    const currentTime = (window.bwktSubtitles && typeof window.bwktSubtitles.getCurrentTime === 'function')
      ? window.bwktSubtitles.getCurrentTime()
      : 0;
    hydrateCueList(currentTime);
    hideMessages();
    if (timestampEl) timestampEl.textContent = formatTimestamp(currentTime);
    if (notesInput) notesInput.value = '';
    const modal = getModalInstance();
    if (modal) {
      modal.show();
    } else {
      console.warn('[corrections] Bootstrap modal API unavailable');
    }
  });

  function getModalInstance() {
    if (!modalEl) return null;
    if (modalInstance && typeof modalInstance.show === 'function') {
      return modalInstance;
    }
    if (window.bootstrap && bootstrap.Modal) {
      modalInstance = bootstrap.Modal.getOrCreateInstance(modalEl);
      return modalInstance;
    }
    return null;
  }

  function hydrateCueList(currentTime) {
    if (!cueContainer) return;
    cueContainer.innerHTML = '';
    const cues = (window.bwktSubtitles && typeof window.bwktSubtitles.getCuesAround === 'function')
      ? window.bwktSubtitles.getCuesAround(currentTime, 3)
      : [];
    if (!Array.isArray(cues) || cues.length === 0) {
      const info = document.createElement('p');
      info.className = 'text-muted';
      info.textContent = 'No subtitles detected near the current timestamp.';
      cueContainer.appendChild(info);
      return;
    }
    cues.forEach(c => {
      const wrapper = document.createElement('div');
      wrapper.className = 'mb-3';
      wrapper.dataset.sequence = c.sequence;
      wrapper.dataset.original = c.text;
      wrapper.dataset.start = c.start;
      wrapper.dataset.end = c.end;

      const label = document.createElement('label');
      label.className = 'form-label fw-semibold';
      label.textContent = `#${c.sequence} (${formatTimestamp(c.start)} → ${formatTimestamp(c.end)})`;
      wrapper.appendChild(label);

      const textarea = document.createElement('textarea');
      textarea.className = 'form-control';
      textarea.rows = 3;
      textarea.value = c.text;
      wrapper.appendChild(textarea);

      cueContainer.appendChild(wrapper);
    });
  }

  function collectPayload() {
    const payload = {
      videoId,
      subtitleVersion,
      timestampSeconds: parseFloat((window.bwktSubtitles && typeof window.bwktSubtitles.getCurrentTime === 'function')
        ? window.bwktSubtitles.getCurrentTime()
        : 0),
      windowStartSeconds: 0,
      windowEndSeconds: 0,
      cues: [],
      notes: notesInput ? notesInput.value : ''
    };
    const startRange = Math.max(0, payload.timestampSeconds - 1.5);
    payload.windowStartSeconds = startRange;
    payload.windowEndSeconds = payload.timestampSeconds + 1.5;
    const entries = cueContainer ? cueContainer.querySelectorAll('[data-sequence]') : [];
    entries.forEach(entry => {
      const textarea = entry.querySelector('textarea');
      if (!textarea) return;
      const updated = textarea.value || '';
      const original = entry.dataset.original || '';
      if (updated.trim() === original.trim()) return;
      payload.cues.push({
        sequence: parseInt(entry.dataset.sequence || '0', 10),
        startSeconds: parseFloat(entry.dataset.start || '0'),
        endSeconds: parseFloat(entry.dataset.end || '0'),
        originalText: original,
        updatedText: updated
      });
    });
    return payload;
  }

  function hideMessages() {
    if (errorEl) errorEl.classList.add('d-none');
    if (successEl) successEl.classList.add('d-none');
  }

  if (form) {
    form.addEventListener('submit', async (evt) => {
      evt.preventDefault();
      hideMessages();
      const payload = collectPayload();
      if (!payload || payload.cues.length === 0) {
        showError('Please edit at least one subtitle line before submitting.');
        return;
      }
      try {
        const res = await fetch('/corrections', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload)
        });
        if (res.ok) {
          showSuccess('Correction submitted for review.');
          const modal = getModalInstance();
          if (modal && typeof modal.hide === 'function') {
            setTimeout(() => {
              try {
                modal.hide();
              } catch (err) {
                console.warn('[corrections] unable to hide modal after submit', err);
              }
            }, 1200);
          }
        } else {
          const data = await res.json().catch(() => ({}));
          showError(data?.error || 'Unable to submit correction right now.');
        }
      } catch (err) {
        console.error('[corrections] submit failed', err);
        showError('Network error while submitting correction.');
      }
    });
  }

  function showError(message) {
    if (!errorEl) return;
    errorEl.textContent = message;
    errorEl.classList.remove('d-none');
  }

  function showSuccess(message) {
    if (!successEl) return;
    successEl.textContent = message;
    successEl.classList.remove('d-none');
  }

  function formatTimestamp(seconds) {
    const s = Math.max(0, seconds || 0);
    const hrs = Math.floor(s / 3600);
    const mins = Math.floor((s % 3600) / 60);
    const secs = Math.floor(s % 60);
    return `${hrs.toString().padStart(2, '0')}:${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
  }
})();
