export default function (view, params) {
  if (view.dataset.mfInitialized === '1') return;
  view.dataset.mfInitialized = '1';
  const api = typeof ApiClient !== 'undefined' ? ApiClient : window.ApiClient;
  const q = (name) => view.querySelector('[data-mf="' + name + '"]');
  const state = { status: {}, sources: [], detail: null, source: '', tab: 'search' };
  let mineTimer = null;
  let mineLoading = false;
  let searchGeneration = 0;
  let detailGeneration = 0;
  let adminTimer = null;
  let adminPage = 1;
  let adminBusy = false;
  let adminUsers = [];
  let previousFocus = null;
  let viewActive = true;
  let notificationTimer = null;
  let preferencesDirty = false;

  function url(path, query) {
    let value = api.getUrl('MediaForgeRequests/' + path);
    if (query) value += '?' + new URLSearchParams(query).toString();
    return value;
  }
  async function call(path, options) {
    const opts = options || {};
    const request = { url: url(path, opts.query), type: opts.method || 'GET', dataType: 'json' };
    if (opts.body !== undefined) {
      request.headers = { 'Content-Type': 'application/json' };
      request.data = JSON.stringify(opts.body);
    }
    try { return await api.fetch(request); }
    catch (error) { throw new Error(await readErrorMessage(error)); }
  }
  async function readErrorMessage(error) {
    const response = error && error.response ? error.response : error;
    const responseJson = error && error.responseJSON || response && response.responseJSON;
    if (responseJson && typeof responseJson.error === 'string' && responseJson.error.trim()) return responseJson.error;
    if (response && typeof response.clone === 'function') {
      try {
        const payload = await response.clone().json();
        if (payload && typeof payload.error === 'string' && payload.error.trim()) return payload.error;
      } catch (_) { /* the response was empty or not JSON */ }
    }
    return error && (error.message || error.statusText) || response && response.statusText || 'Anfrage fehlgeschlagen.';
  }
  function notice(message, error) {
    q('notice').innerHTML = '';
    if (!message) return;
    const box = document.createElement('div'); box.className = 'mf-notice' + (error ? ' mf-error' : ''); box.textContent = message; q('notice').appendChild(box);
  }
  function switchTab(name) {
    state.tab = name;
    if (name !== 'mine' && mineTimer) { clearTimeout(mineTimer); mineTimer = null; }
    view.querySelectorAll('.mf-tab').forEach((b) => b.classList.toggle('active', b.dataset.tab === name));
    view.querySelectorAll('.mf-panel').forEach((p) => p.classList.toggle('active', p.dataset.panel === name));
    if (name === 'mine') loadMine();
    if (adminTimer) { clearTimeout(adminTimer); adminTimer = null; }
    if (name === 'admin') loadAdmin();
    if (name === 'notifications') loadNotifications();
  }
  view.querySelectorAll('.mf-tab').forEach((b) => b.addEventListener('click', () => switchTab(b.dataset.tab)));

  async function boot() {
    try {
      state.status = await call('Status');
      q('mode').textContent = state.status.mode === 'automatic' ? 'Direkter Download' : 'Freigabe durch Admin';
      if (!state.status.configured) notice('Das Plugin ist noch nicht mit MediaForge verbunden.', true);
      else if (state.status.maintenance) notice(state.status.maintenanceMessage || 'Anfragen sind derzeit deaktiviert.', true);
      const data = await call('Sources');
      state.sources = Array.isArray(data.sources) ? data.sources : [];
      state.sources.forEach((item) => {
        const option = document.createElement('option'); option.value = item.id; option.textContent = item.label; q('source').appendChild(option);
      });
      await loadDiscover();
      await detectAdmin();
      await loadNotifications();
    } catch (error) { notice(error.message, true); }
  }
  async function loadDiscover(retry) {
    const host = q('discover');
    try {
      const data = await call('Discover');
      const definitions = [['new', 'Neu'], ['popular', 'Beliebt'], ['movies', 'Filme']];
      const total = definitions.reduce((count, row) => count + ((data.rows && data.rows[row[0]]) || []).length, 0);
      if (!total && !retry) {
        setTimeout(() => loadDiscover(true), 2500);
        return;
      }
      host.innerHTML = '';
      definitions.forEach((definition) => {
        const items = (data.rows && data.rows[definition[0]]) || [];
        if (!items.length) return;
        const section = document.createElement('section'); section.className = 'mf-discoverrow';
        const heading = document.createElement('h3'); heading.className = 'mf-discoverhead'; heading.textContent = definition[1];
        const grid = document.createElement('div'); grid.className = 'mf-discovergrid';
        items.forEach((item) => grid.appendChild(createMediaCard(item, item.source, item.source_label)));
        section.append(heading, grid); host.appendChild(section);
      });
      if (!host.children.length) host.innerHTML = '<div class="mf-empty">Zurzeit sind keine Empfehlungen verfügbar.</div>';
    } catch (error) {
      host.innerHTML = '';
      const box = document.createElement('div'); box.className = 'mf-notice mf-error'; box.textContent = 'Startansicht: ' + error.message; host.appendChild(box);
    }
  }
  async function detectAdmin() {
    try {
      const data = await call('Admin/Overview');
      const tab = view.querySelector('[data-tab="admin"]'); tab.style.display = '';
      renderRequests(q('admin'), data.items, true);
      adminUsers = await call('Admin/Users');
      adminUsers.forEach(user => { for (const name of ['admin-user', 'rule-user']) { const option = document.createElement('option'); option.value = user.id; option.textContent = user.username; q(name).appendChild(option); } });
      state.sources.forEach(source => { const option = document.createElement('option'); option.value = source.id; option.textContent = source.label; q('admin-source').appendChild(option); });
      syncRule();
    } catch (_) { /* normal users receive 403 */ }
  }

  function syncSearchMode() {
    const searching = q('query').value.trim().length > 0;
    q('discover').hidden = searching;
    q('results').hidden = !searching;
    if (!searching) q('results').innerHTML = '';
  }
  q('query').addEventListener('input', () => { searchGeneration++; q('results').innerHTML = ''; syncSearchMode(); });
  q('search-form').addEventListener('submit', async (event) => {
    event.preventDefault();
    const generation = ++searchGeneration;
    const keyword = q('query').value.trim();
    const selectedSource = q('source').value;
    const maximum = Math.max(1, Math.min(32, Number(state.status.maxSearchSources) || 8));
    const sources = selectedSource === 'all'
      ? state.sources.slice(0, maximum)
      : state.sources.filter((item) => item.id === selectedSource);
    syncSearchMode(); notice('');
    const host = q('results'); host.innerHTML = '';
    const pending = document.createElement('div'); pending.className = 'mf-empty'; pending.dataset.mfSearchPending = '1'; pending.textContent = 'Weitere Quellen werden durchsucht…'; host.appendChild(pending);
    if (!sources.length) { pending.remove(); notice('Keine freigegebene Quelle verfügbar.', true); return; }
    let resultCount = 0; let errorCount = 0;
    await Promise.all(sources.map(async (item) => {
      let groups;
      try {
        const data = await call('Search', { query: { query: keyword, source: item.id } });
        groups = data.groups || [];
      } catch (error) {
        groups = [{ source: item.id, label: item.label, error: error.message }];
      }
      if (generation !== searchGeneration) return;
      const rendered = appendResults(host, groups, pending);
      resultCount += rendered.count; errorCount += rendered.errors;
    }));
    if (generation !== searchGeneration) return;
    pending.remove();
    if (!resultCount && !errorCount) host.innerHTML = '<div class="mf-empty">Keine Treffer gefunden.</div>';
  });

  function appendResults(host, groups, before) {
    let count = 0; let errors = 0;
    groups.forEach((group) => {
      const results = group.data && Array.isArray(group.data.results) ? group.data.results : [];
      results.forEach((item) => {
        host.insertBefore(createMediaCard(item, group.source, group.label), before); count++;
      });
      if (group.error) { errors++; const box = document.createElement('div'); box.className = 'mf-notice mf-error'; box.textContent = group.label + ': ' + group.error; host.insertBefore(box, before); }
    });
    return { count, errors };
  }

  function createMediaCard(item, sourceId, sourceLabel) {
    const card = document.createElement('article'); card.className = 'mf-card'; card.tabIndex = 0;
    const rawUrl = item.url || item.link || item.series_url;
    if (item.poster_url) {
      const cover = document.createElement('img'); cover.className = 'mf-cover'; cover.loading = 'lazy'; cover.alt = '';
      card.appendChild(cover); loadCover(cover, item.poster_url);
    } else if (rawUrl) {
      const cover = document.createElement('img'); cover.className = 'mf-cover'; cover.loading = 'lazy'; cover.alt = '';
      card.appendChild(cover);
      call('Series', { query: { url: rawUrl } })
        .then((detail) => detail.poster_url ? loadCover(cover, detail.poster_url) : cover.remove())
        .catch(() => cover.remove());
    }
    const body = document.createElement('div'); body.className = 'mf-cardbody';
    const title = document.createElement('div'); title.className = 'mf-cardtitle'; title.textContent = item.title || item.name || 'Unbekannter Titel';
    const source = document.createElement('div'); source.className = 'mf-source'; source.textContent = (sourceLabel || sourceId || '') + (item.year ? ' · ' + item.year : '');
    body.append(title, source); card.appendChild(body);
    const open = () => openDetail(item, sourceId); card.addEventListener('click', open); card.addEventListener('keydown', (event) => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); open(); } });
    return card;
  }

  async function loadCover(image, posterUrl) {
    let objectUrl = '';
    try {
      const response = await api.fetch({ url: url('Image', { url: posterUrl }), type: 'GET' });
      if (!response || typeof response.blob !== 'function') throw new Error('Ungültige Bildantwort');
      const blob = await response.blob();
      if (!blob.type.startsWith('image/')) throw new Error('Ungültiger Bildtyp');
      objectUrl = URL.createObjectURL(blob); image.src = objectUrl;
      image.addEventListener('load', () => URL.revokeObjectURL(objectUrl), { once: true });
      image.addEventListener('error', () => { URL.revokeObjectURL(objectUrl); image.remove(); }, { once: true });
    } catch (_) {
      if (objectUrl) URL.revokeObjectURL(objectUrl);
      image.remove();
    }
  }

  async function openDetail(item, source) {
    const rawUrl = item.url || item.link || item.series_url;
    if (!rawUrl) return notice('Der Treffer enthält keine MediaForge-URL.', true);
    const generation = ++detailGeneration;
    previousFocus = document.activeElement; q('close').focus(); state.source = source; state.detail = null; q('overlay').style.display = 'flex'; q('detail-title').textContent = item.title || item.name || 'Laden…'; q('description').textContent = 'Vorhandene Staffeln und Episoden werden geprüft…'; q('plan').innerHTML = '<div class="mf-empty">MediaForge prüft den Bestand…</div>'; q('request').disabled = true;
    setOptions(q('language'), [state.status.defaultLanguage || 'German Dub'], state.status.defaultLanguage);
    setOptions(q('provider'), [state.status.defaultProvider || 'VOE'], state.status.defaultProvider);
    try {
      let type = item.media_type === 'movie' ? 'movie' : 'series';
      if (!item.media_type) {
        const sourceObj = state.sources.find((s) => s.id === source);
        if (sourceObj && Array.isArray(sourceObj.media_types) && sourceObj.media_types.length === 1 && sourceObj.media_types[0] === 'movie') {
          type = 'movie';
        }
      }
      const payload = { title: item.title || item.name || 'Unbekannter Titel', seriesUrl: rawUrl, source, mediaType: type };
      const plan = await call('Requests/Plan', { method: 'POST', body: payload });
      if (generation !== detailGeneration) return;
      state.detail = Object.assign(payload, { title: plan.title || payload.title, plan });
      q('detail-title').textContent = state.detail.title;
      q('description').textContent = plan.description || 'Keine Beschreibung verfügbar.';
      const languages = Array.isArray(plan.languages) && plan.languages.length ? plan.languages : [state.status.defaultLanguage || 'German Dub'];
      setOptions(q('language'), languages, state.status.defaultLanguage);
      const syncProviders = () => {
        const available = plan.providers && Array.isArray(plan.providers[q('language').value]) ? plan.providers[q('language').value] : [];
        setOptions(q('provider'), available.length ? available : [state.status.defaultProvider || 'VOE'], state.status.defaultProvider);
      };
      q('language').onchange = syncProviders; syncProviders();
      q('plan').innerHTML = '';
      const summary = document.createElement('div'); summary.className = 'mf-plan ' + (plan.missing_count ? '' : 'complete');
      if (plan.missing_count) {
        summary.textContent = plan.is_movie
          ? 'Der Film fehlt und kann angefragt werden.'
          : plan.missing_count + ' von ' + plan.total_count + ' Episoden fehlen. Es werden ausschließlich diese fehlenden Episoden angefragt.';
        q('request').disabled = false;
      } else {
        summary.textContent = plan.is_movie
          ? 'Der Film ist bereits vorhanden und wird nicht erneut eingereiht.'
          : 'Alle ' + plan.total_count + ' Episoden sind bereits vorhanden. Es wird nichts eingereiht.';
      }
      q('plan').appendChild(summary);
      q('request').textContent = !plan.missing_count && !plan.is_movie ? 'Zukünftige Folgen abonnieren' : 'Anfragen';
      q('request').disabled = !plan.missing_count && plan.is_movie;
      if (plan.missing_count) {
        const matching = await call('Requests/Matching', { method: 'POST', body: { ...payload, language: q('language').value, provider: q('provider').value } });
        if (generation === detailGeneration && matching.exists) q('request').textContent = 'Ebenfalls interessiert';
      }
      q('close').focus();
    } catch (error) { if (generation === detailGeneration) { q('description').textContent = error.message; q('plan').innerHTML = ''; } }
  }
  function setOptions(select, values, preferred) { const clean = Array.from(new Set(values.filter(Boolean))); select.innerHTML = ''; clean.forEach((value) => { const option = document.createElement('option'); option.value = value; option.textContent = value; select.appendChild(option); }); if (clean.includes(preferred)) select.value = preferred; }
  q('request').addEventListener('click', async () => {
    if (!state.detail || !state.detail.plan || (state.detail.plan.is_movie && !state.detail.plan.missing_count)) return;
    const detail = state.detail;
    const generation = detailGeneration;
    q('request').disabled = true;
    try {
      const payload = { title: detail.title, seriesUrl: detail.seriesUrl, source: detail.source, mediaType: detail.plan.is_movie ? 'movie' : 'series', language: q('language').value, provider: q('provider').value, upscale: q('upscale').checked, subscribeOnly: !detail.plan.missing_count && !detail.plan.is_movie };
      const result = await call('Requests/Participation', { method: 'POST', body: payload });
      const message = ['queued', 'available', 'shared'].includes(result.status) ? 'Die Anfrage wurde übernommen. Download und Autosync werden getrennt angezeigt.' : 'Die Anfrage wurde zur Freigabe gespeichert.';
      if (generation === detailGeneration) { closeDetail(); notice(message); switchTab('mine'); } else { notice(message); }
    } catch (error) { notice(error.message, true); } finally { if (generation === detailGeneration) q('request').disabled = false; }
  });
  function closeDetail() { detailGeneration++; state.detail = null; q('overlay').style.display = 'none'; if (previousFocus && previousFocus.isConnected) previousFocus.focus(); }
  q('close').addEventListener('click', closeDetail); q('cancel').addEventListener('click', closeDetail); q('overlay').addEventListener('click', (e) => { if (e.target === q('overlay')) closeDetail(); });

  async function loadMine() {
    if (mineLoading) return;
    mineLoading = true;
    if (!q('mine').children.length) q('mine').textContent = 'Laden…';
    try {
      const items = await call('Requests/Mine');
      let progress = [];
      if (items.some((item) => item.status === 'queued' && item.mediaForgeQueueId)) {
        try { progress = (await call('Requests/Progress')).items || []; } catch (_) { /* request list remains available */ }
      }
      const byQueue = new Map(progress.map((item) => [item.queue_id, item]));
      renderRequests(q('mine'), items, false, byQueue);
      if (mineTimer) clearTimeout(mineTimer);
      const hasActiveDownload = items.some((item) => item.status === 'queued')
        || progress.some((item) => item.status === 'queued' || item.status === 'running');
      if (viewActive && view.isConnected && state.tab === 'mine') {
        mineTimer = setTimeout(loadMine, 5000);
      }
    } catch (error) { q('mine').textContent = error.message; }
    finally { mineLoading = false; }
  }
  async function loadAdmin() {
    if (adminBusy) return;
    if (adminTimer) { clearTimeout(adminTimer); adminTimer = null; }
    adminBusy = true;
    try {
      const query = { page: adminPage, pageSize: 30 };
      for (const [field, name] of [['query', 'admin-query'], ['userId', 'admin-user'], ['status', 'admin-status'], ['source', 'admin-source'], ['since', 'admin-since']]) if (q(name).value) query[field] = q(name).value;
      const data = await call('Admin/Overview', { query });
      renderRequests(q('admin'), data.items, true, null, data.participants);
      q('admin-counts').textContent = data.pending + ' offen · ' + data.downloading + ' Downloads · ' + data.errors + ' Fehler · ' + data.autosyncPending + ' Autosync-Übernahmen';
      q('page-label').textContent = 'Seite ' + data.page + ' · ' + data.total + ' Anfragen';
      q('page-prev').disabled = adminPage <= 1; q('page-next').disabled = adminPage * data.pageSize >= data.total;
    } catch (error) { notice(error.message, true); }
    finally { adminBusy = false; if (viewActive && view.isConnected && state.tab === 'admin') adminTimer = setTimeout(loadAdmin, 5000); }
  }
  function button(label, action, danger) {
    const b = document.createElement('button'); b.type = 'button'; b.className = 'mf-btn ' + (danger ? 'danger' : 'secondary'); b.textContent = label;
    b.onclick = async () => { b.disabled = true; try { await action(); } catch (error) { notice(error.message, true); } finally { b.disabled = false; } }; return b;
  }
  function renderRequests(host, items, admin, progressByQueue, participants) {
    Array.from(host.childNodes).filter(node => node.nodeType !== 1).forEach(node => node.remove());
    const wanted = new Set((items || []).map(item => String(item.id)));
    Array.from(host.children).forEach(node => { if (!wanted.has(node.dataset.id)) node.remove(); });
    if (!items || !items.length) { host.textContent = 'Keine Anfragen vorhanden.'; return; }
    items.forEach(item => {
      const progress = progressByQueue && progressByQueue.get(item.mediaForgeQueueId);
      const people = participants && participants[item.id] || [];
      const signature = JSON.stringify([item, progress, people]);
      const existing = Array.from(host.children).find(node => node.dataset.id === String(item.id));
      if (existing && existing.dataset.signature === signature) return;
      const checked = existing && existing.querySelector('input[data-select]')?.checked;
      const expanded = existing && existing.querySelector('details')?.open;
      const card = document.createElement('article'); card.className = 'mf-request'; card.dataset.id = item.id; card.dataset.signature = signature;
      const top = document.createElement('div'); top.className = 'mf-requesttop';
      const left = document.createElement('div');
      if (admin && ['pending', 'failed'].includes(item.status)) { const check = document.createElement('input'); check.type = 'checkbox'; check.dataset.select = item.id; check.checked = !!checked; check.setAttribute('aria-label', item.title + ' auswählen'); left.appendChild(check); }
      const title = document.createElement('strong'); title.textContent = item.title; left.appendChild(title);
      const meta = document.createElement('div'); meta.className = 'mf-meta'; meta.textContent = (admin ? item.username + ' · ' : '') + (item.selectionLabel || 'Serien-Abo') + ' · ' + item.language + ' · ' + new Date(item.createdUtc).toLocaleString(); left.appendChild(meta);
      if (admin && people.length > 1) { const names = document.createElement('div'); names.className = 'mf-meta'; names.textContent = 'Beteiligte: ' + people.map(p => p.username).join(', '); left.appendChild(names); }
      const pill = document.createElement('span'); pill.className = 'mf-pill ' + item.status; pill.textContent = progressLabel(progress) || statusLabel(item.status); top.append(left, pill); card.appendChild(top);
      const percent = progress ? progress.percent : item.progress;
      if (percent != null) { const bar = document.createElement('progress'); bar.max = 100; bar.value = Math.max(0, Math.min(100, Number(percent) || 0)); bar.setAttribute('aria-label', 'Downloadfortschritt'); bar.style.width = '100%'; card.appendChild(bar); }
      if (item.autosyncRequested) { const sync = document.createElement('div'); sync.className = 'mf-notice'; sync.textContent = item.autosyncJobId ? (item.autosyncRestricted ? 'Vorhandenes Autosync-Abo pausiert oder eingeschränkt; Einstellungen beibehalten.' : 'Autosync eingerichtet – neue Folgen werden automatisch nachgezogen.') : (item.autosyncError || 'Autosync wird nach der Freigabe eingerichtet.'); card.appendChild(sync); }
      if (item.error) { const error = document.createElement('div'); error.className = 'mf-notice mf-error'; error.textContent = item.error; card.appendChild(error); }
      const history = document.createElement('details'); history.open = !!expanded; const summary = document.createElement('summary'); summary.textContent = 'Verlauf'; history.appendChild(summary);
      (item.history || []).forEach(event => { const line = document.createElement('div'); line.className = 'mf-meta'; line.textContent = new Date(event.utc).toLocaleString() + ' · ' + statusLabel(event.kind) + (admin ? ' · ' + event.actor : '') + (event.detail ? ' · ' + event.detail : ''); history.appendChild(line); }); card.appendChild(history);
      const actions = document.createElement('div'); actions.className = 'mf-actions';
      if (admin && ['pending', 'failed'].includes(item.status)) { actions.append(button('Freigeben / erneut prüfen', () => decide(item.id, 'Approve')), button('Ablehnen', () => decide(item.id, 'Reject'), true)); }
      if (admin && item.autosyncRequested && !item.autosyncJobId && !['pending','processing','uncertain'].includes(item.status)) actions.appendChild(button('Nur Autosync erneut versuchen', () => recover(item.id, 'autosync')));
      if (admin && item.status === 'uncertain') { actions.append(button('Übergabe abgleichen', () => recover(item.id, 'reconcile')), button('Erneut senden…', async () => { if (window.confirm('MediaForge könnte diesen Download bereits angenommen haben. Erneutes Senden kann doppelte Downloads erzeugen. Trotzdem erneut senden?')) await recover(item.id, 'reconcile', true); }, true)); }
      if (admin && ['partial','cancelled'].includes(item.status)) actions.appendChild(button('Fehlende Inhalte erneut prüfen', () => recover(item.id, 'missing')));
      if (!admin && item.status === 'pending') actions.appendChild(button('Beteiligung zurückziehen', () => withdrawRequest(item.id), true));
      if (!admin && item.status === 'available') actions.appendChild(button('In Jellyfin öffnen', async () => { const data = await call('Requests/' + item.id + '/Library'); if (!data.itemId) throw new Error('Kein für dich zugänglicher Bibliothekseintrag gefunden.'); window.location.hash = '#/details?id=' + encodeURIComponent(data.itemId) + (typeof api.serverId === 'function' ? '&serverId=' + encodeURIComponent(api.serverId()) : ''); }));
      card.appendChild(actions); if (existing) existing.replaceWith(card); else host.appendChild(card);
    });
  }
  async function decide(id, action) {
    const reason = action === 'Reject' ? window.prompt('Ablehnungsgrund (optional):', '') : '';
    if (reason === null) return;
    await call('Admin/Requests/' + id + '/' + action, { method: 'POST', body: action === 'Reject' ? { reason } : {} }); await loadAdmin();
  }
  async function recover(id, action, confirmPossibleDuplicate = false) { await call('Admin/Requests/' + id + '/Recovery', { method: 'POST', body: { action, confirmPossibleDuplicate } }); await loadAdmin(); }
  async function withdrawRequest(id) { if (!window.confirm('Deine noch nicht freigegebene Beteiligung zurückziehen?')) return; await call('Requests/' + id, { method: 'DELETE' }); await loadMine(); }
  async function loadNotifications() {
    clearTimeout(notificationTimer);
    try {
      const data = await call('Notifications'); q('unread').textContent = data.unread ? '(' + data.unread + ')' : '';
      const host = q('notifications'); host.replaceChildren();
      if (!data.items.length) host.textContent = 'Keine Mitteilungen vorhanden.';
      data.items.forEach(item => { const card = document.createElement('article'); card.className = 'mf-request'; const text = document.createElement('div'); text.textContent = item.message; const date = document.createElement('div'); date.className = 'mf-meta'; date.textContent = new Date(item.createdUtc).toLocaleString(); card.append(text, date); if (!item.readUtc) card.appendChild(button('Als gelesen markieren', async () => { await call('Notifications/Read', { method: 'POST', body: { id: item.id } }); await loadNotifications(); })); host.appendChild(card); });
      if (!preferencesDirty) { q('notify-decisions').checked = data.preferences.decisions; q('notify-availability').checked = data.preferences.availability; q('notify-episodes').value = data.preferences.newEpisodes; }
    } catch (error) { notice(error.message, true); }
    finally { if (viewActive && view.isConnected) notificationTimer = setTimeout(loadNotifications, 30000); }
  }
  function syncRule() { const user = adminUsers.find(u => u.id === q('rule-user').value); if (!user) return; q('rule-mode').value = user.rule.approvalMode; q('rule-limit').value = user.rule.maxOpenRequests || ''; q('rule-subscribe').checked = user.rule.allowSubscriptions; }
  q('rule-user').addEventListener('change', syncRule);
  q('notification-form').addEventListener('change', () => { preferencesDirty = true; });
  q('rule-form').addEventListener('submit', async event => { event.preventDefault(); try { const rule = { approvalMode: q('rule-mode').value, maxOpenRequests: q('rule-limit').value ? Number(q('rule-limit').value) : null, allowSubscriptions: q('rule-subscribe').checked }; await call('Admin/Users/' + q('rule-user').value + '/Rule', { method: 'PUT', body: rule }); adminUsers.find(u => u.id === q('rule-user').value).rule = rule; notice('Benutzerregel gespeichert.'); } catch (error) { notice(error.message, true); } });
  q('notification-form').addEventListener('submit', async event => { event.preventDefault(); try { await call('Notifications/Preferences', { method: 'PUT', body: { decisions: q('notify-decisions').checked, availability: q('notify-availability').checked, newEpisodes: q('notify-episodes').value } }); notice('Benachrichtigungen gespeichert.'); } catch (error) { notice(error.message, true); } });
  q('read-all').addEventListener('click', async () => { try { await call('Notifications/Read', { method: 'POST', body: { id: 'all' } }); await loadNotifications(); } catch (error) { notice(error.message, true); } });
  q('admin-filter').addEventListener('submit', event => { event.preventDefault(); adminPage = 1; loadAdmin(); });
  q('page-prev').onclick = () => { adminPage = Math.max(1, adminPage - 1); loadAdmin(); }; q('page-next').onclick = () => { adminPage++; loadAdmin(); };
  async function batch(action) { const ids = Array.from(q('admin').querySelectorAll('input[data-select]:checked')).map(n => Number(n.dataset.select)); if (!ids.length) return notice('Bitte Anfragen auswählen.', true); const reason = action === 'reject' ? window.prompt('Ablehnungsgrund:', '') : ''; if (reason === null) return; q('batch-approve').disabled = true; q('batch-reject').disabled = true; try { const result = await call('Admin/Batch', { method: 'POST', body: { ids, action, reason } }); const failures = result.results.filter(r => !r.ok); notice(failures.length ? failures.map(r => '#' + r.id + ': ' + (r.error || r.status)).join(' · ') : 'Auswahl verarbeitet.', !!failures.length); await loadAdmin(); } catch (error) { notice(error.message, true); } finally { q('batch-approve').disabled = false; q('batch-reject').disabled = false; } }
  q('batch-approve').onclick = () => batch('approve'); q('batch-reject').onclick = () => batch('reject');
  q('diagnostics').onclick = async () => { try { const data = await call('Admin/Diagnostics'); const host = q('diagnostic-result'); host.hidden = false; host.textContent = 'Plugin: ' + data.pluginVersion + ' · Verbindung: ' + (data.connection.healthy ? 'OK' : 'nicht verfügbar') + ' · Modul: ' + (data.module?.version || 'unbekannt') + ' · Fähigkeiten: ' + (data.module?.capabilities || []).join(', ') + ' · Berechtigungen: ' + JSON.stringify(data.module?.permissions || {}) + ((data.module?.capabilities || []).includes('autosync') ? '' : ' · MediaForge-Modul für Autosync aktualisieren.'); } catch (error) { notice(error.message, true); } };
  view.addEventListener('keydown', event => {
    if (q('overlay').style.display !== 'flex') return;
    if (event.key === 'Escape') { event.preventDefault(); closeDetail(); }
    if (event.key === 'Tab') { const nodes = Array.from(q('overlay').querySelectorAll('button:not(:disabled),select,input')).filter(n => !n.hidden); const first = nodes[0], last = nodes[nodes.length - 1]; if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); } else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); } }
  });
  view.addEventListener('viewhide', () => { viewActive = false; clearTimeout(mineTimer); clearTimeout(adminTimer); clearTimeout(notificationTimer); });
  view.addEventListener('viewshow', () => { viewActive = true; if (state.tab === 'mine') loadMine(); if (state.tab === 'admin') loadAdmin(); loadNotifications(); });
  function progressLabel(progress) { if (!progress) return ''; return ({ queued: 'Wartet auf Download', running: 'Wird heruntergeladen', completed: 'Download fertig', partial: 'Teilweise fertig', failed: 'Download fehlgeschlagen', cancelled: 'In MediaForge abgebrochen' })[progress.status] || ''; }
  function progressDetail(progress) { const phase = ({ download: 'Download', ffmpeg: 'Verarbeitung' })[progress.phase] || 'Download'; const episodes = progress.total_episodes > 1 ? ' · ' + progress.current_episode + '/' + progress.total_episodes + ' Episoden' : ''; return phase + ': ' + Math.round(Number(progress.percent) || 0) + '%' + episodes; }
  function statusLabel(status) { return ({ approved: 'Freigegeben', running: 'Download läuft', requested: 'Angefragt', shared: 'Gemeinsame Anfrage', uncertain: 'Übergabe unklar', 'autosync-ready': 'Autosync eingerichtet', pending: 'Ausstehend', processing: 'Wird übergeben', queued: 'In MediaForge', completed: 'Download fertig', available: 'Bereits in Jellyfin vorhanden', partial: 'Teilweise fertig', cancelled: 'Außerhalb von Jellyfin abgebrochen', rejected: 'Abgelehnt', withdrawn: 'Zurückgezogen', failed: 'Fehlgeschlagen' })[status] || status; }
  q('refresh-mine').addEventListener('click', loadMine); q('refresh-admin').addEventListener('click', loadAdmin);
  boot();
}
