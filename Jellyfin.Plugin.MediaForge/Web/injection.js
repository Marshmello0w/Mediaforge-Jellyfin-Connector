(function () {
  'use strict';
  if (window.__mediaForgeNavigationStarted) return;
  window.__mediaForgeNavigationStarted = true;
  const MENU_ID = 'mediaforge-requests-sidebar';
  const MODAL_ID = 'mediaforge-requests-modal';
  let pendingCount = 0;
  let sessionKey = '';
  let generation = 0;
  let pollTimer = null;
  let polling = false;
  let suspended = false;
  function api() { return typeof ApiClient !== 'undefined' ? ApiClient : window.ApiClient; }
  function currentSession() {
    const client = api();
    try {
      const user = client && client.getCurrentUserId && client.getCurrentUserId();
      return user ? String(client.serverId ? client.serverId() : client.getUrl('')) + ':' + user : '';
    } catch (_) { return ''; }
  }
  function renderBadge() {
    const active = !suspended && !document.hidden && sessionKey && sessionKey === currentSession();
    document.querySelectorAll('.mainDrawerButton, #' + MENU_ID).forEach(button => {
      let badge = button.querySelector('.mf-pending-badge');
      if (!active || pendingCount < 1) {
        if (badge) badge.remove();
        button.classList.remove('mf-pending-anchor');
        return;
      }
      button.classList.add('mf-pending-anchor');
      if (!badge) {
        badge = document.createElement('span'); badge.className = 'mf-pending-badge';
        badge.setAttribute('role', 'status'); badge.setAttribute('aria-live', 'polite');
        button.appendChild(badge);
      }
      const label = pendingCount === 1 ? '1 Anfrage wartet auf Freigabe' : pendingCount + ' Anfragen warten auf Freigabe';
      const text = pendingCount > 99 ? '99+' : String(pendingCount);
      if (badge.textContent !== text) badge.textContent = text;
      if (badge.getAttribute('aria-label') !== label) { badge.setAttribute('aria-label', label); badge.title = label; }
    });
  }
  function checkSession() {
    const key = currentSession();
    if (key === sessionKey) return false;
    sessionKey = key; generation++; pendingCount = 0; renderBadge();
    return true;
  }
  async function refreshCount() {
    clearTimeout(pollTimer); pollTimer = null;
    checkSession();
    if (polling || suspended || document.hidden) return;
    if (!sessionKey) { renderBadge(); pollTimer = setTimeout(refreshCount, 30000); return; }
    const client = api(); const key = sessionKey; const revision = generation;
    polling = true;
    let timeout;
    try {
      // The count-only endpoint is administrator-protected on the server.
      // No other users' requests, titles or identities enter this script.
      const result = await Promise.race([
        client.fetch({ url: client.getUrl('MediaForgeRequests/Admin/PendingCount'), type: 'GET', dataType: 'json', cache: false }),
        new Promise((_, reject) => { timeout = setTimeout(() => reject(new Error('timeout')), 15000); })
      ]);
      if (revision === generation && key === currentSession())
        pendingCount = Number.isSafeInteger(result.count) && result.count > 0 ? result.count : 0;
    } catch (_) {
      if (revision === generation) pendingCount = 0; // Includes logout / non-admin 403.
    } finally {
      clearTimeout(timeout); polling = false;
      checkSession(); renderBadge();
      if (!suspended && !document.hidden) pollTimer = setTimeout(refreshCount, revision === generation ? 30000 : 0);
    }
  }
  function inject() {
    if (document.getElementById(MENU_ID)) return;
    const sidebar = document.querySelector('.mainDrawer-scrollContainer, .mainDrawer .scrollContainer'); if (!sidebar || !api()) return;
    const entry = document.createElement('a'); entry.id = MENU_ID; entry.href = '#'; entry.setAttribute('is', 'emby-linkbutton'); entry.setAttribute('data-itemid', 'mediaforge-requests'); entry.className = 'navMenuOption lnkMediaFolder'; entry.innerHTML = '<span class="material-icons navMenuOptionIcon playlist_add" aria-hidden="true"></span><span class="navMenuOptionText">Anfragen</span>';
    entry.addEventListener('click', function (event) { event.preventDefault(); event.stopPropagation(); const backdrop = document.querySelector('.mainDrawer-backdrop'); if (backdrop) backdrop.click(); open(); });
    const custom = sidebar.querySelector('.customMenuOptions');
    const libraries = sidebar.querySelector('.libraryMenuOptions');
    const admin = sidebar.querySelector('.adminMenuOptions');
    if (custom) custom.appendChild(entry);
    else if (libraries) sidebar.insertBefore(entry, libraries);
    else if (admin) sidebar.insertBefore(entry, admin);
    else sidebar.appendChild(entry);
  }
  async function open() {
    const old = document.getElementById(MODAL_ID); if (old) old.remove();
    const overlay = document.createElement('div'); overlay.id = MODAL_ID; overlay.style.cssText = 'position:fixed;inset:0;z-index:999;background:#181818;overflow:auto;'; overlay.innerHTML = '<div style="position:sticky;top:0;z-index:5;display:flex;justify-content:flex-end;padding:.5rem;background:#111"><button type="button" aria-label="Schließen" style="border:0;background:transparent;color:#fff;font-size:2rem;cursor:pointer">×</button></div><div data-content><div style="padding:3rem;text-align:center">Laden…</div></div>';
    overlay.querySelector('button').onclick = () => overlay.remove(); document.body.appendChild(overlay);
    const client = api(); const content = overlay.querySelector('[data-content]');
    try {
      const html = await client.fetch({ url: client.getUrl('MediaForgeRequests/Page'), type: 'GET', dataType: 'text' }); const doc = new DOMParser().parseFromString(html, 'text/html'); const page = doc.querySelector('[data-role="page"]'); content.innerHTML = page ? page.innerHTML : html;
      const module = await import(client.getUrl('MediaForgeRequests/PageScript') + '?v=' + Date.now()); if (module.default) module.default(content, { sidebar: true });
    } catch (error) { content.textContent = 'MediaForge Requests konnte nicht geladen werden.'; }
  }
  function start() {
    const style = document.createElement('style');
    style.textContent = '.mainDrawerButton.mf-pending-anchor{position:relative;overflow:visible}.mf-pending-badge{position:absolute;top:1px;right:0;display:inline-flex;align-items:center;justify-content:center;min-width:18px;height:18px;padding:0 3px;box-sizing:border-box;border-radius:999px;background:#c62828;color:#fff;font:700 11px/18px system-ui,sans-serif;pointer-events:none;z-index:1}';
    document.head.appendChild(style);
    style.textContent += '#' + MENU_ID + ' .mf-pending-badge{position:static;flex-shrink:0;margin-inline-start:.5em;vertical-align:middle}';
    const observer = new MutationObserver(() => { inject(); if (checkSession()) refreshCount(); renderBadge(); });
    observer.observe(document.body, { childList: true, subtree: true });
    const refresh = () => { checkSession(); renderBadge(); refreshCount(); };
    document.addEventListener('visibilitychange', refresh);
    document.addEventListener('mediaforge:requests-changed', () => { generation++; refresh(); });
    window.addEventListener('hashchange', refresh);
    window.addEventListener('focus', refresh);
    window.addEventListener('pagehide', () => { suspended = true; generation++; pendingCount = 0; clearTimeout(pollTimer); observer.disconnect(); renderBadge(); });
    window.addEventListener('pageshow', () => { suspended = false; observer.observe(document.body, { childList: true, subtree: true }); refresh(); });
    inject(); refresh();
  }
  let attempts = 0; const timer = setInterval(function () { if (api() || attempts++ > 100) { clearInterval(timer); if (api()) document.readyState === 'loading' ? document.addEventListener('DOMContentLoaded', start) : start(); } }, 200);
})();
