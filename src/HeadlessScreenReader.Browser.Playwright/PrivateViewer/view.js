(() => {
  'use strict';
  let revision = 0, ended = false, paused = false, imageUrl = null;
  const $ = id => document.getElementById(id);
  const clear = () => { $('pixels').hidden = true; $('pixels').removeAttribute('src'); if (imageUrl) URL.revokeObjectURL(imageUrl); imageUrl = null; $('nodes').replaceChildren(); $('binding').textContent = ''; $('limits').textContent = ''; $('empty').hidden = false; };
  const status = (phase, message) => { if ($('state').textContent !== phase) $('state').textContent = phase; if ($('message').textContent !== message) $('message').textContent = message; };
  const intent = action => {
    if (ended || revision >= 1000000) return;
    revision++; clear();
    if (action === 'Pause') paused = true;
    if (action === 'Resume') paused = false;
    if (action === 'End') ended = true;
    $('pause').textContent = paused ? 'Resume viewing' : 'Pause viewing';
    $('pause').disabled = ended; $('refresh').disabled = ended || paused; $('end').disabled = ended;
    status(ended ? 'Ended' : paused ? 'Paused' : 'Loading', ended ? 'Viewing ended. The target received no input.' : paused ? 'Viewing paused. Displayed content cleared.' : 'Waiting for a permitted observation.');
    window.__viewerIntent(action, revision).catch(() => { ended = true; clear(); status('Unavailable', 'The viewing host is unavailable. Close this window.'); for (const id of ['pause','refresh','end']) $(id).disabled = true; });
  };
  $('pause').addEventListener('click', () => intent(paused ? 'Resume' : 'Pause'));
  $('refresh').addEventListener('click', () => intent('Refresh'));
  $('end').addEventListener('click', () => intent('End'));
  window.__renderView = data => {
    if (data.Revision !== revision || ended || paused) return false;
    clear(); status(data.Phase, data.Message);
    if (data.Phase === 'Ended') { ended = true; for (const id of ['pause','refresh','end']) $(id).disabled = true; }
    if (data.Phase !== 'Viewing') return true;
    const bytes = Uint8Array.from(atob(data.Png), c => c.charCodeAt(0));
    imageUrl = URL.createObjectURL(new Blob([bytes], {type:'image/png'})); $('pixels').src = imageUrl; $('pixels').hidden = false; $('empty').hidden = true;
    $('binding').textContent = `Epoch ${data.Epoch} · retained observation ${data.Ordinal} · pixel baseline ${data.Baseline}`;
    $('limits').textContent = data.Truncated ? 'Display limited to 128 nodes and bounded text. Some content is truncated.' : `${data.Lines.length} exposed nodes shown.`;
    if (data.Assisted) $('limits').textContent += ' Prior assistance was explicitly simulated.';
    for (const text of data.Lines) { const li = document.createElement('li'); li.textContent = text; $('nodes').append(li); }
    return true;
  };
})();
