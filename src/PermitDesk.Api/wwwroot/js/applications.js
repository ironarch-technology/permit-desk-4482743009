(() => {
  if (!PermitDesk.requireSession()) return;
  PermitDesk.mountHeader('/applications.html');

  const { el, chip, dateTime } = PermitDesk;
  const rows = document.getElementById('rows');
  const empty = document.getElementById('empty');
  const filterText = document.getElementById('filter-text');
  const filterState = document.getElementById('filter-state');

  let applications = [];
  let workTypes = {};

  function render() {
    const text = filterText.value.trim().toLowerCase();
    const state = filterState.value;

    const visible = applications.filter((a) => {
      if (state && a.state !== state) return false;
      if (!text) return true;
      return (a.reference || '').toLowerCase().includes(text) || (a.parcelAddress || '').toLowerCase().includes(text);
    });

    rows.replaceChildren(...visible.map((a) => {
      const target = a.state === 'draft' ? `/apply.html?draft=${a.id}` : `/application.html?id=${a.id}`;
      return el('tr', {},
        el('td', {}, el('a', { href: target }, a.reference)),
        el('td', {}, a.parcelAddress || 'Not entered yet'),
        el('td', {}, workTypes[a.workType] || ''),
        el('td', {}, chip(a.state, a.stateLabel)),
        el('td', {}, dateTime(a.updatedAt)),
        el('td', {}, a.state === 'draft'
          ? el('button', { type: 'button', class: 'btn-icon', onclick: () => removeDraft(a) }, '×')
          : null));
    }));

    empty.hidden = visible.length > 0;
  }

  async function removeDraft(application) {
    if (!window.confirm(`Delete draft ${application.reference}?`)) return;
    await PermitDesk.del(`/api/applications/${application.id}`);
    applications = applications.filter((a) => a.id !== application.id);
    render();
  }

  async function load() {
    const [list, types] = await Promise.all([
      PermitDesk.get('/api/applications'),
      PermitDesk.get('/api/reference/work-types'),
    ]);
    applications = list;
    workTypes = Object.fromEntries(types.map((t) => [t.code, t.label]));

    const states = new Map(list.map((a) => [a.state, a.stateLabel]));
    for (const [value, label] of states) filterState.append(el('option', { value }, label));
    render();
  }

  filterText.addEventListener('input', render);
  filterState.addEventListener('change', render);
  load();
})();
