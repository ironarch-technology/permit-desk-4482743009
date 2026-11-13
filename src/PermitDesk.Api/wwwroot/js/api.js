// Shared helpers for every portal page: session, API calls, small DOM utilities.
const PermitDesk = (() => {
  const TOKEN_KEY = 'permitdesk.token';
  const USER_KEY = 'permitdesk.user';

  const token = () => sessionStorage.getItem(TOKEN_KEY);
  const user = () => JSON.parse(sessionStorage.getItem(USER_KEY) || 'null');

  function signIn(session) {
    sessionStorage.setItem(TOKEN_KEY, session.token);
    sessionStorage.setItem(USER_KEY, JSON.stringify(session.user));
  }

  function signOut(reason) {
    sessionStorage.removeItem(TOKEN_KEY);
    sessionStorage.removeItem(USER_KEY);
    window.location.href = reason ? `/index.html?${reason}=1` : '/index.html';
  }

  function requireSession() {
    if (!token()) {
      window.location.href = '/index.html';
      return false;
    }
    return true;
  }

  async function request(method, path, body) {
    const headers = {};
    if (token()) headers.Authorization = `Bearer ${token()}`;
    if (body !== undefined) headers['Content-Type'] = 'application/json';

    const response = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });

    if (response.status === 401 && !path.startsWith('/api/auth/login')) {
      signOut('ended');
      throw { status: 401, body: {} };
    }

    let payload = null;
    const text = await response.text();
    if (text) {
      try { payload = JSON.parse(text); } catch { payload = { error: text }; }
    }

    if (!response.ok) throw { status: response.status, body: payload || {} };
    return payload;
  }

  // el('p', { class: 'muted' }, 'text', childNode)
  function el(tag, attrs, ...children) {
    const node = document.createElement(tag);
    for (const [key, value] of Object.entries(attrs || {})) {
      if (value === null || value === undefined || value === false) continue;
      if (key.startsWith('on')) node.addEventListener(key.slice(2), value);
      else if (key === 'class') node.className = value;
      else node.setAttribute(key, value === true ? '' : value);
    }
    for (const child of children.flat()) {
      if (child === null || child === undefined || child === false) continue;
      node.append(child.nodeType ? child : document.createTextNode(String(child)));
    }
    return node;
  }

  const CHIP_CLASS = {
    draft: 'chip-draft',
    submitted: 'chip-progress',
    zoning_check: 'chip-progress',
    zoning_hold: 'chip-progress',
    plan_review: 'chip-progress',
    corrections_required: 'chip-action',
    fees_assessed: 'chip-action',
    issued: 'chip-done',
    denied: 'chip-closed',
    withdrawn: 'chip-closed',
    expired: 'chip-closed',
  };

  const chip = (state, label) => el('span', { class: `chip ${CHIP_CLASS[state] || 'chip-draft'}` }, label);

  const money = (value) =>
    value === null || value === undefined ? '' : Number(value).toLocaleString('en-US', { style: 'currency', currency: 'USD' });

  const dateTime = (value) =>
    value ? new Date(value).toLocaleString('en-US', { dateStyle: 'medium', timeStyle: 'short' }) : '';

  const date = (value) => (value ? new Date(value).toLocaleDateString('en-US', { dateStyle: 'medium' }) : '');

  function header(active) {
    const current = user();
    const link = (href, text) => el('a', { href, 'aria-current': active === href ? 'page' : null }, text);
    return el('header', { class: 'site-header' },
      el('div', { class: 'title' }, 'Permit Desk', el('small', {}, 'City of Mountport Development Services')),
      el('nav', { 'aria-label': 'Main' },
        link('/applications.html', 'My applications'),
        link('/apply.html', 'Apply for a permit'),
        current && current.role === 'technician' ? link('/staff.html', 'Counter') : null,
        el('button', { type: 'button', onclick: () => signOut() }, `Sign out ${current ? current.name : ''}`)));
  }

  function mountHeader(active) {
    document.body.prepend(header(active));
    document.body.prepend(el('a', { class: 'skip-link', href: '#main' }, 'Skip to main content'));
  }

  return {
    token, user, signIn, signOut, requireSession, el, chip, money, dateTime, date, mountHeader,
    get: (path) => request('GET', path),
    post: (path, body) => request('POST', path, body === undefined ? {} : body),
    put: (path, body) => request('PUT', path, body),
    del: (path) => request('DELETE', path),
  };
})();
