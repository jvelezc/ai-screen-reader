(() => {
  'use strict';
  const items = JSON.parse(document.getElementById('evidence-data').textContent);
  const byId = new Map(items.map(item => [item.id, item]));
  const player = document.getElementById('recording');
  const mediaStatus = document.getElementById('media-status');
  const label = document.getElementById('selected-label');
  const detail = document.getElementById('selected-detail');
  const interval = document.getElementById('selected-interval');
  const identity = document.getElementById('selected-identity');
  const identityDisclosure = document.getElementById('identity-disclosure');
  let selected = null;
  let available = false;
  let failed = !player;
  const usable = cue => cue && (cue.coverage === 'Covered' || cue.coverage === 'Partial')
    && Number.isFinite(cue.startSeconds) && Number.isFinite(cue.endSeconds)
    && cue.startSeconds >= 0 && cue.endSeconds > cue.startSeconds;
  const overlaps = (a, b) => usable(a) && usable(b) && a.startSeconds <= b.endSeconds && b.startSeconds <= a.endSeconds;
  function seekSelected() {
    const cue = selected && selected.cue;
    if (!usable(cue)) {
      interval.textContent = 'Recording interval: ' + (cue ? cue.coverage : 'Unknown') + '. No video position is inferred.';
      return;
    }
    interval.textContent = cue.coverage + ' recording interval: ' + cue.startSeconds.toFixed(3) + '–'
      + cue.endSeconds.toFixed(3) + ' seconds, frames ' + cue.firstFrame + '–' + cue.lastFrame + '.';
    if (failed) { interval.textContent += ' Playback is unavailable; the retained timing evidence remains visible.'; return; }
    if (!available) { interval.textContent += ' Waiting for local video metadata.'; return; }
    if (cue.startSeconds >= player.duration) {
      interval.textContent += ' The loaded clip does not cover this position. Inspect its manifest.';
      return;
    }
    try {
      player.currentTime = cue.startSeconds;
      if (cue.endSeconds > player.duration + 0.04)
        interval.textContent += ' The loaded clip covers only part of this retained interval.';
    } catch {
      interval.textContent += ' Seeking failed. Use the original recording and structured evidence links.';
    }
  }
  function select(id) {
    const item = byId.get(id);
    if (!item) return;
    selected = item;
    if (player) player.pause();
    label.textContent = item.label;
    detail.textContent = item.detail;
    identity.textContent = item.identity || '';
    identityDisclosure.hidden = !item.identity;
    document.querySelectorAll('[data-evidence]').forEach(button =>
      button.setAttribute('aria-pressed', String(button.dataset.evidence === id)));
    document.querySelectorAll('[data-timeline-event]').forEach(row => {
      const eventId = row.dataset.timelineEvent;
      const related = item.kind === 'Observation' ? overlaps(item.cue, byId.get(eventId)?.cue) : item.eventIds.includes(eventId);
      row.classList.toggle('in-range', related);
      if (eventId === id) row.setAttribute('aria-current', 'true');
      else row.removeAttribute('aria-current');
    });
    seekSelected();
  }
  document.addEventListener('click', event => {
    const trigger = event.target.closest('button[data-evidence]');
    if (trigger) select(trigger.dataset.evidence);
  });
  if (player) {
    const playbackStatus = () => {
      if (failed || !available) return;
      mediaStatus.textContent = player.seeking ? 'Seeking the original recording…'
        : player.ended ? 'Original recording ended. Use the native controls to replay.'
        : player.paused ? 'Original recording ready and paused. Use the native controls to play.'
        : 'Playing the original recording. Use the native controls to pause.';
    };
    const ready = () => {
      if (!Number.isFinite(player.duration) || player.duration <= 0) return;
      available = true;
      failed = false;
      playbackStatus();
      if (selected) seekSelected();
    };
    player.addEventListener('loadedmetadata', ready);
    for (const event of ['seeking', 'seeked', 'play', 'playing', 'pause', 'ended'])
      player.addEventListener(event, playbackStatus);
    player.addEventListener('error', () => {
      failed = true;
      available = false;
      player.hidden = true;
      mediaStatus.textContent = 'Original video could not be loaded. Check the media files beside this report and its structured manifest.';
      if (selected) seekSelected();
    });
    if (player.readyState >= 1) ready();
  }
  const initial = items.find(item => item.kind === 'Observation') || items[0];
  if (initial) select(initial.id);
})();
