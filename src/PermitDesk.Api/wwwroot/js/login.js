(() => {
  const { el } = PermitDesk;
  const message = document.getElementById('message');
  const form = document.getElementById('login-form');

  if (new URLSearchParams(window.location.search).has('ended')) {
    message.replaceChildren(el('div', { class: 'notice', role: 'status' }, 'Your session has ended. Sign in again to carry on. Saved drafts are kept.'));
  }

  form.addEventListener('submit', async (event) => {
    event.preventDefault();
    const email = document.getElementById('email').value;
    const password = document.getElementById('password').value;

    try {
      const session = await PermitDesk.post('/api/auth/login', { email, password });
      PermitDesk.signIn(session);
      window.location.href = session.user.role === 'technician' ? '/staff.html' : '/applications.html';
    } catch (err) {
      const text = err.status === 429 ? 'Too many attempts. Wait a minute and try again.' : (err.body.error || 'Sign in failed.');
      message.replaceChildren(el('div', { class: 'field-error' }, text));
    }
  });
})();
