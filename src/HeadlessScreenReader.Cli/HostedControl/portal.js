(() => {
  'use strict';
  const byId = id => document.getElementById(id);
  const guid = value => typeof value === 'string' && /^[a-f0-9-]{36}$/i.test(value);
  let configuration = null, accessToken = null, workspace = null, selected = null, currentReport = null;
  let epoch = 0, selectionRevision = 0, pendingRequest = null, poll = null, pollCount = 0, authBusy = false;
  const controllers = new Set();
  const callback = new URL(location.href);
  let authCode = callback.searchParams.get('code');
  const callbackError = callback.searchParams.has('error') || callback.hash.length > 0;
  history.replaceState(null, '', location.pathname);
  const verifierKey = 'hsr.pkce.gpnijaxrgtwzwtrqsbcu';

  function text(id, value) { const node = byId(id); if (node.textContent !== value) node.textContent = value; }
  function state(title, message) { text('state-title', title); text('state-message', message); }
  function problem(message) { text('error', message); byId('error').hidden = !message; }
  function clearResult() {
    currentReport = null; byId('result').hidden = true; byId('result-empty').hidden = false;
    byId('facts').replaceChildren(); byId('nodes').replaceChildren(); byId('limitations').replaceChildren();
    byId('download').disabled = true;
  }
  function clearSession() {
    epoch++; selectionRevision++; authBusy = false; accessToken = null; workspace = null; selected = null; pendingRequest = null;
    clearTimeout(poll); for (const controller of controllers) controller.abort(); controllers.clear();
    byId('workspace').hidden = true; byId('account').hidden = false; byId('runs').replaceChildren();
    byId('password').value = ''; text('signed-in', ''); clearResult();
    byId('login').disabled = !configuration; byId('signup').disabled = !configuration;
    byId('run').disabled = false; text('run', 'Run shop fixture');
  }
  function handle(error) {
    if (error.name === 'AbortError') return;
    problem(error.safeMessage || 'The operation could not be confirmed. Check the connection and try again.');
  }
  function aborted() { return new DOMException('Request cancelled', 'AbortError'); }
  async function api(path, body) {
    const revision = epoch, credential = accessToken;
    if (!credential) throw aborted();
    const controller = new AbortController(); controllers.add(controller);
    try {
      const response = await fetch(path, { method: body === undefined ? 'GET' : 'POST', signal: controller.signal,
        headers: { Authorization: 'Bearer ' + credential, ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
        body: body === undefined ? undefined : JSON.stringify(body), cache: 'no-store', credentials: 'omit' });
      let value;
      try { value = await response.json(); } catch { value = {}; }
      if (revision !== epoch) throw aborted();
      if (!response.ok) {
        if (response.status === 401 || response.status === 403 || response.status === 404) {
          clearSession(); state('Sign in', 'Your current session or workspace access is no longer available.');
        }
        const descriptions = {
          CONTROL_AUTHENTICATION_REQUIRED: 'Please sign in again.',
          CONTROL_EMAIL_CONFIRMATION_REQUIRED: 'Confirm your email before signing in here.',
          CONTROL_WORKSPACE_ACCESS_DENIED: 'This account cannot access the workspace. Sign in with an authorized account.',
          CONTROL_REQUEST_CONFLICT: 'This request conflicts with an earlier run. Refresh your runs before continuing.',
          CONTROL_SCAN_NOT_FOUND: 'This run is no longer available to your account.',
          CONTROL_WORKSPACE_NOT_FOUND: 'This workspace is no longer available to your account.'
        };
        let message = descriptions[value.title] || 'The operation could not be confirmed. Check the connection and retry.';
        if (typeof value.supportReference === 'string' && value.supportReference.length === 32 && /^[a-f0-9]+$/.test(value.supportReference))
          message += ' Reference: ' + value.supportReference;
        throw { safeMessage: message };
      }
      return value;
    } finally { controllers.delete(controller); }
  }
  async function provider(path, body) {
    const revision = epoch, controller = new AbortController(); controllers.add(controller);
    try {
    const response = await fetch(configuration.projectUrl + '/auth/v1/' + path, { method: 'POST',
      signal: controller.signal,
      headers: { apikey: configuration.publishableKey, 'Content-Type': 'application/json' },
      body: JSON.stringify(body), cache: 'no-store', credentials: 'omit', redirect: 'error' });
    let value;
    try { value = await response.json(); } catch { throw { safeMessage: 'Sign-in is unavailable. Please try again.' }; }
    if (revision !== epoch) throw aborted();
    if (!response.ok) throw { safeMessage: response.status === 429
      ? 'Too many sign-in attempts. Wait a little before trying again.'
      : 'Sign-in was not completed. Check your email confirmation and account details, then try again.' };
    return value;
    } finally { controllers.delete(controller); }
  }
  async function connected(value) {
    if (typeof value.access_token !== 'string' || value.access_token.length > 8192)
      throw { safeMessage: 'No confirmed session was returned. Please sign in again.' };
    accessToken = value.access_token;
    const identity = await api('/api/session');
    if (!guid(identity.userId) || typeof identity.email !== 'string') throw new Error('Invalid session response');
    state('Opening your workspace', 'Checking your current workspace access.');
    workspace = await api('/api/workspace', {});
    if (!guid(workspace.id) || typeof workspace.name !== 'string') throw new Error('Invalid workspace response');
    byId('account').hidden = true; byId('workspace').hidden = false;
    text('workspace-title', workspace.name); text('signed-in', identity.email); byId('password').value = '';
    await loadRuns(); state('Ready', 'Run the shop fixture or open a previous result.');
  }
  async function signIn(event) {
    event.preventDefault(); if (authBusy || !configuration || !byId('sign-in').reportValidity()) return;
    const password = byId('password').value, email = byId('email').value.trim();
    clearSession(); authBusy = true; problem(''); byId('login').disabled = true; byId('signup').disabled = true;
    const revision = epoch;
    try {
      state('Signing in', 'Checking your account.');
      const value = await provider('token?grant_type=password', { email, password });
      if (revision !== epoch) throw aborted();
      await connected(value);
    } catch (error) { if (revision === epoch) { clearSession(); state('Sign in', 'Use your confirmed account to continue.'); handle(error); } }
    finally { if (revision === epoch) { authBusy = false; byId('login').disabled = false; byId('signup').disabled = false; } }
  }
  function base64url(bytes) { return btoa(String.fromCharCode(...bytes)).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', ''); }
  async function signUp() {
    if (authBusy || !configuration || !byId('sign-in').reportValidity()) return;
    const password = byId('password').value, email = byId('email').value.trim();
    if (password.length < 8) { problem('Use at least eight characters for a new account password.'); return; }
    authBusy = true; const revision = epoch;
    problem(''); byId('signup').disabled = true; byId('login').disabled = true;
    try {
      const verifier = base64url(crypto.getRandomValues(new Uint8Array(32)));
      const challenge = base64url(new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier))));
      if (revision !== epoch) throw aborted();
      localStorage.setItem(verifierKey, JSON.stringify({ verifier, expires: Date.now() + 60 * 60 * 1000 }));
      await provider('signup?redirect_to=' + encodeURIComponent(location.origin),
        { email, password, code_challenge: challenge, code_challenge_method: 's256' });
      byId('password').value = '';
      state('Check your email', 'Open the confirmation email, then return here to sign in.');
    } catch (error) { if (revision === epoch) { localStorage.removeItem(verifierKey); handle(error); } }
    finally { if (revision === epoch) { authBusy = false; byId('password').value = ''; byId('signup').disabled = false; byId('login').disabled = false; } }
  }
  function scopePath() { return '/api/workspaces/' + workspace.id + '/scans'; }
  function execution(value) { return ({ queued: 'Queued', leased: 'Observing', observed: 'Observation stored', blocked: 'Could not complete observation' })[value] || 'Unconfirmed'; }
  async function loadRuns() {
    const data = await api(scopePath());
    if (!Array.isArray(data.items) || data.items.length > 50 || data.items.some(item => !guid(item.id))) throw new Error('Invalid history');
    const focused = document.activeElement?.dataset.run;
    const fragment = document.createDocumentFragment();
    for (const item of data.items) {
      const li = document.createElement('li'), button = document.createElement('button');
      button.type = 'button'; button.className = 'run-row'; button.dataset.run = item.id;
      if (item.id === selected) button.setAttribute('aria-current', 'true');
      const label = document.createElement('span'); label.textContent = new Date(item.createdAt).toLocaleString();
      const status = document.createElement('span'); status.className = 'run-state'; status.textContent = execution(item.state);
      const id = document.createElement('span'); id.className = 'run-id'; id.textContent = 'Run ' + item.id.slice(0, 8);
      button.append(label, status, id); button.addEventListener('click', () => selectRun(item.id).catch(handle));
      li.append(button); fragment.append(li);
    }
    byId('runs').replaceChildren(fragment); byId('runs-empty').hidden = data.items.length > 0; byId('more').hidden = !data.hasMore;
    if (focused) [...byId('runs').querySelectorAll('button')].find(button => button.dataset.run === focused)?.focus();
  }
  function fact(label, value) {
    const term = document.createElement('dt'), detail = document.createElement('dd'); term.textContent = label; detail.textContent = value;
    byId('facts').append(term, detail);
  }
  function renderReport(report) {
    if (!report?.scan || report.scan.id !== selected || report.scan.organizationId !== workspace?.id) {
      clearResult(); throw new Error('Invalid report identity');
    }
    const observation = report.observation;
    if ((report.evidenceState === 'Available') !== !!observation || observation && (!Array.isArray(observation.nodes) || observation.nodes.length > 2048
      || observation.goalVerified !== false || observation.dispatchedInputs !== 0
      || typeof observation.digest !== 'string' || observation.digest.length !== 64 || !/^[a-f0-9]+$/.test(observation.digest)
      || observation.nodes.some(node => typeof node.id !== 'string' || typeof node.role !== 'string' || typeof node.name !== 'string'))) {
      clearResult(); throw new Error('Invalid observation');
    }
    currentReport = report; byId('result-empty').hidden = true; byId('result').hidden = false;
    byId('facts').replaceChildren(); byId('nodes').replaceChildren(); byId('limitations').replaceChildren();
    fact('Execution', execution(report.scan.state)); fact('Evidence', report.evidenceState);
    fact('Goal', observation ? 'Not tested' : 'Not verified');
    fact('Browser input', observation ? String(observation.dispatchedInputs) + ' keys' : 'Not verified');
    if (observation) fact('Evidence copy', new Map([['S3', 'Verified private object storage'], ['Local', 'Server disk'],
      ['LocalFallback', 'Server disk; object copy not found']]).get(observation.evidenceStorage) || 'Storage location unconfirmed');
    const messages = { Pending: 'The worker has not completed this observation yet.',
      NotCollected: 'The observation could not be collected. This is an execution result, not an accessibility judgment.',
      Unavailable: 'The saved observation is currently unavailable. Its availability remains unconfirmed.',
      Invalid: 'The saved evidence could not be verified. It is not displayed.',
      Available: 'This run captured an accessibility observation. It did not traverse or verify an objective.' };
    text('result-message', messages[report.evidenceState] || 'Evidence state is unconfirmed.');
    byId('nodes-title').hidden = !observation; byId('integrity').hidden = !observation;
    byId('download').disabled = !observation;
    if (observation) {
      text('digest', 'Original observation SHA256: ' + observation.digest);
      const fragment = document.createDocumentFragment();
      for (const node of observation.nodes) {
        const item = document.createElement('li'), name = document.createElement('span'), role = document.createElement('span');
        name.className = 'node-name'; name.textContent = node.name || '(unnamed)';
        role.className = 'node-meta'; role.textContent = node.role + ' · node ' + node.id;
        item.append(name, role);
        const states = [];
        if (node.focused) states.push('Focused'); if (node.disabled) states.push('Disabled');
        if (node.checked !== null) states.push('Checked: ' + node.checked);
        if (node.expanded !== null) states.push(node.expanded ? 'Expanded' : 'Collapsed');
        if (node.selected !== null) states.push(node.selected ? 'Selected' : 'Not selected');
        if (node.required !== null) states.push('Required: ' + node.required + ' (' + node.requiredSource + ')');
        if (states.length) { const status = document.createElement('span'); status.className = 'node-state'; status.textContent = states.join(' · '); item.append(status); }
        fragment.append(item);
      }
      byId('nodes').append(fragment);
      for (const limitation of observation.limitations || []) { const item = document.createElement('li'); item.textContent = limitation; byId('limitations').append(item); }
    }
    state(execution(report.scan.state), report.evidenceState === 'Available' ? 'The verified observation is ready to read.' : messages[report.evidenceState] || 'Refresh to check the current result.');
  }
  async function selectRun(id, repeated = false) {
    if (!guid(id) || !workspace) return;
    clearTimeout(poll); if (!repeated) { pollCount = 0; selectionRevision++; clearResult(); text('result-empty', 'Loading the selected run.'); }
    selected = id; const revision = selectionRevision;
    const report = await api(scopePath() + '/' + id);
    if (revision !== selectionRevision || id !== selected) return;
    renderReport(report);
    for (const button of byId('runs').querySelectorAll('button')) {
      if (button.dataset.run === id) button.setAttribute('aria-current', 'true'); else button.removeAttribute('aria-current');
    }
    if (report.scan.state === 'queued' || report.scan.state === 'leased') {
      if (++pollCount < 80) poll = setTimeout(() => selectRun(id, true).catch(handle), 1500);
      else state('Still waiting', 'This run has not completed. Refresh runs to check it again.');
    }
  }
  async function runFixture() {
    if (!workspace) return;
    const revision = epoch; byId('run').disabled = true; problem(''); state('Starting a run', 'Saving this fixture request.');
    pendingRequest ||= crypto.randomUUID();
    try {
      const receipt = await api(scopePath(), { requestId: pendingRequest });
      if (revision !== epoch || !guid(receipt.scanId)) throw aborted();
      pendingRequest = null; text('run', 'Run shop fixture'); await loadRuns(); await selectRun(receipt.scanId);
    } catch (error) { if (revision === epoch && pendingRequest) text('run', 'Retry shop fixture'); handle(error); }
    finally { if (revision === epoch) byId('run').disabled = false; }
  }
  async function initialize() {
    byId('reconnect').hidden = true; problem('');
    try {
      const response = await fetch('/api/config', { cache: 'no-store', credentials: 'omit' });
      if (!response.ok) throw new Error('Configuration unavailable');
      configuration = await response.json();
      if (configuration.projectUrl !== 'https://gpnijaxrgtwzwtrqsbcu.supabase.co' || typeof configuration.publishableKey !== 'string') throw new Error('Invalid configuration');
      byId('login').disabled = false; byId('signup').disabled = false;
      state('Sign in', 'Use your confirmed account, or create an account to begin.');
      if (authCode) {
        let saved; try { saved = JSON.parse(localStorage.getItem(verifierKey)); } catch { saved = null; }
        const code = authCode; authCode = null; localStorage.removeItem(verifierKey);
        if (saved && typeof saved.verifier === 'string' && saved.expires > Date.now()) {
          state('Confirming sign-in', 'Finishing the email confirmation.');
          authBusy = true; byId('login').disabled = true; byId('signup').disabled = true;
          try { await connected(await provider('token?grant_type=pkce', { auth_code: code, code_verifier: saved.verifier })); }
          finally { authBusy = false; byId('login').disabled = false; byId('signup').disabled = false; }
        } else state('Sign in', 'Email confirmation was received. Sign in in this window to continue.');
      } else if (callbackError) problem('The sign-in link was not completed. Sign in here or request a new confirmation.');
    } catch (error) { handle(error); byId('reconnect').hidden = false; }
  }
  byId('sign-in').addEventListener('submit', event => signIn(event).catch(handle));
  byId('signup').addEventListener('click', () => signUp().catch(handle));
  byId('run').addEventListener('click', () => runFixture().catch(handle));
  byId('refresh').addEventListener('click', () => { problem(''); loadRuns().then(() => selected && selectRun(selected)).catch(handle); });
  byId('logout').addEventListener('click', () => { clearSession(); localStorage.removeItem(verifierKey); problem(''); state('Signed out', 'Sign in to open your workspace again.'); });
  byId('reconnect').addEventListener('click', () => initialize().catch(handle));
  byId('download').addEventListener('click', () => {
    if (!currentReport?.observation) return;
    const url = URL.createObjectURL(new Blob([JSON.stringify(currentReport, null, 2)], { type: 'application/json' }));
    const link = document.createElement('a'); link.href = url; link.download = 'reader-run-' + currentReport.scan.id + '.json';
    link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
  });
  initialize().catch(handle);
})();
