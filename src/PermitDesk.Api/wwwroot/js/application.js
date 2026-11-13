(() => {
  if (!PermitDesk.requireSession()) return;
  PermitDesk.mountHeader();

  const { el, chip, money, dateTime, date } = PermitDesk;
  const params = new URLSearchParams(window.location.search);
  const id = Number(params.get('id'));
  const content = document.getElementById('content');
  const notice = document.getElementById('notice');
  const live = document.getElementById('live');
  const heading = document.getElementById('heading');
  const withdrawDialog = document.getElementById('withdraw-dialog');

  const MOVING = ['submitted', 'zoning_check', 'plan_review', 'fees_assessed'];
  let current = null;
  let workTypes = {};
  let inspectionTypes = [];

  const say = (text, error) => notice.replaceChildren(el('div', { class: `notice ${error ? 'notice-error' : ''}`, role: error ? 'alert' : 'status' }, text));

  function summaryCard(a) {
    const rows = [
      ['Property', a.parcelAddress],
      ['Type of work', workTypes[a.workType] || a.workType],
      ['Estimated value', money(a.declaredValuation)],
      ['Description', a.scopeOfWork],
      ['Applicant', `${a.contact.name || ''} (${a.applicantType})`],
      ['Submitted', dateTime(a.submittedAt)],
    ];
    if (a.contractor) rows.push(['Contractor licence', `${a.contractor.licenceNumber}, expires ${date(a.contractor.licenceExpiry)}`]);

    return el('section', { class: 'card', 'aria-labelledby': 'summary-title' },
      el('h2', { id: 'summary-title' }, 'Summary ', chip(a.state, a.stateLabel)),
      el('dl', { class: 'summary' }, rows.flatMap(([term, value]) => [el('dt', {}, term), el('dd', {}, value || '')])));
  }

  function correctionsCard(a) {
    if (a.outstandingCorrections.length === 0) return null;

    const form = el('form', { novalidate: true, onsubmit: submitCorrections },
      a.outstandingCorrections.map((item) => el('div', { class: 'correction' },
        el('div', { class: 'code' }, item.code),
        el('p', {}, item.narrative),
        el('p', { class: 'citation' }, item.citation),
        el('div', { class: 'field' },
          el('label', { for: `response-${item.id}` }, `Your response to ${item.code}`),
          el('textarea', { id: `response-${item.id}`, 'data-item': item.id, required: true, 'aria-describedby': `error-${item.id}` }),
          el('div', { id: `error-${item.id}`, class: 'field-error', role: 'alert' })))),
      el('button', { type: 'submit', class: 'btn btn-primary' }, 'Send responses to plan review'));

    return el('section', { class: 'card', 'aria-labelledby': 'corrections-title' },
      el('h2', { id: 'corrections-title' }, 'Corrections needed'),
      el('p', {}, 'The plan reviewer needs more information before the review can continue. Respond to each item below.'),
      form);
  }

  function historyCard(a) {
    const answered = a.correctionHistory.filter((item) => !a.outstandingCorrections.some((o) => o.id === item.id));
    if (answered.length === 0) return null;

    return el('section', { class: 'card', 'aria-labelledby': 'history-title' },
      el('h2', { id: 'history-title' }, 'Earlier corrections'),
      answered.map((item) => el('div', { class: 'correction' },
        el('div', { class: 'code' }, `${item.code} (round ${item.cycle + 1})`),
        el('p', {}, item.narrative),
        el('p', {}, el('strong', {}, 'Your response: '), item.response || 'None recorded'))));
  }

  function feesCard(a) {
    if (!a.feeQuote || a.state !== 'fees_assessed') return null;
    const quote = a.feeQuote;

    return el('section', { class: 'card', 'aria-labelledby': 'fees-title' },
      el('h2', { id: 'fees-title' }, 'Fees due'),
      el('table', { class: 'fee-lines' },
        el('thead', {}, el('tr', {}, el('th', { scope: 'col' }, 'Fee'), el('th', { scope: 'col', class: 'amount' }, 'Amount'))),
        el('tbody', {}, quote.lines.map((line) => el('tr', {}, el('td', {}, line.label), el('td', { class: 'amount' }, money(line.amount))))),
        el('tfoot', {}, el('tr', {}, el('th', { scope: 'row' }, 'Total'), el('td', { class: 'amount' }, el('strong', {}, money(quote.amount)))))),
      el('p', {}, quote.expired
        ? 'This quote has expired. Choose Pay to get an up to date amount.'
        : `This quote is valid until ${dateTime(quote.expiresAt)}.`),
      el('button', { type: 'button', class: 'btn btn-primary', onclick: pay }, 'Pay with City Cashiering'),
      el('p', { class: 'hint' }, 'You will pay on the City Cashiering site and come back here afterwards.'));
  }

  function permitCard(a) {
    if (!a.permit) return null;

    const typeSelect = el('select', { id: 'inspection-type' },
      inspectionTypes.map((t) => el('option', { value: t.code }, t.label)));
    const slotArea = el('div', { id: 'slot-area' });

    const booked = a.inspections.length === 0
      ? el('p', {}, 'No inspections booked yet.')
      : el('ul', {}, a.inspections.map((i) => {
        const type = inspectionTypes.find((t) => t.code === i.inspectionType);
        return el('li', {}, `${type ? type.label : i.inspectionType}: ${dateTime(i.slotStart)} (${i.status})`);
      }));

    return el('section', { class: 'card', 'aria-labelledby': 'permit-title' },
      el('h2', { id: 'permit-title' }, `Permit ${a.permit.permitNumber}`),
      el('p', {}, `Issued ${date(a.permit.issuedAt)}. Valid until ${date(a.permit.validUntil)}.`),
      el('h3', {}, 'Inspections'),
      booked,
      a.state === 'issued' ? el('div', {},
        el('div', { class: 'field' },
          el('label', { for: 'inspection-type' }, 'Inspection type'),
          typeSelect),
        el('button', { type: 'button', class: 'btn', onclick: () => findSlots(typeSelect.value, slotArea) }, 'Find available times'),
        slotArea) : null);
  }

  function timelineCard(a) {
    return el('section', { class: 'card', 'aria-labelledby': 'timeline-title' },
      el('h2', { id: 'timeline-title' }, 'Status timeline'),
      el('table', { class: 'timeline' },
        el('tbody', {}, a.timeline.map((entry) => el('tr', {},
          el('td', { class: 'when' }, dateTime(entry.occurredAt)),
          el('td', { class: 'dot' }, '●'),
          el('td', { class: 'what' },
            el('strong', {}, entry.label),
            el('span', {}, [entry.reason, entry.actor].filter(Boolean).join(' · '))))))));
  }

  function actionsCard(a) {
    if (a.terminal || a.state === 'issued') return null;
    return el('div', { class: 'actions' },
      el('button', { type: 'button', class: 'btn btn-danger', onclick: () => withdrawDialog.showModal() }, 'Withdraw application'));
  }

  function render(a) {
    document.title = `${a.reference} - Mountport Permit Desk`;
    heading.textContent = `Application ${a.reference}`;
    content.replaceChildren(...[
      summaryCard(a), correctionsCard(a), feesCard(a), permitCard(a), historyCard(a), timelineCard(a), actionsCard(a),
    ].filter(Boolean));
  }

  async function load(announce) {
    const next = await PermitDesk.get(`/api/applications/${id}`);
    const changed = !current || current.state !== next.state || current.timeline.length !== next.timeline.length ||
      current.inspections.map((i) => i.status).join() !== next.inspections.map((i) => i.status).join();
    if (announce && current && current.state !== next.state) live.textContent = `Status is now ${next.stateLabel}.`;
    current = next;
    if (changed) render(next);
  }

  async function submitCorrections(event) {
    event.preventDefault();
    const fields = [...event.target.querySelectorAll('textarea[data-item]')];
    fields.forEach((field) => {
      field.removeAttribute('aria-invalid');
      document.getElementById(`error-${field.dataset.item}`).textContent = '';
    });

    try {
      await PermitDesk.post(`/api/applications/${id}/corrections/responses`, {
        responses: fields.map((field) => ({ itemId: Number(field.dataset.item), response: field.value })),
      });
      say('Your responses have been sent to plan review.');
      await load();
      heading.focus();
    } catch (err) {
      const errors = err.body.errors || {};
      let first = null;
      for (const field of fields) {
        const text = errors[`item-${field.dataset.item}`];
        if (!text) continue;
        field.setAttribute('aria-invalid', 'true');
        document.getElementById(`error-${field.dataset.item}`).textContent = text;
        first = first || field;
      }
      if (first) first.focus();
      else say(err.body.error || 'Your responses could not be sent.', true);
    }
  }

  async function pay() {
    try {
      const result = await PermitDesk.post(`/api/applications/${id}/payment`);
      window.location.href = result.checkoutUrl;
    } catch (err) {
      say(err.body.error || 'Payment could not be started.', true);
      if (err.body.code === 'quote_expired') {
        current = null;
        await load();
      }
    }
  }

  async function findSlots(type, area) {
    area.replaceChildren(el('p', {}, 'Looking for available times...'));
    try {
      const slots = await PermitDesk.get(`/api/applications/${id}/inspections/slots?type=${encodeURIComponent(type)}`);
      if (slots.length === 0) {
        area.replaceChildren(el('p', {}, 'No times are available for that inspection.'));
        return;
      }
      const form = el('form', { onsubmit: (event) => book(event, type) },
        el('fieldset', {},
          el('legend', {}, 'Choose a time'),
          el('ul', { class: 'slots' }, slots.map((slot, index) => el('li', {},
            el('label', {},
              el('input', { type: 'radio', name: 'slot', value: slot.slotId, checked: index === 0 }),
              ` ${dateTime(slot.start)}`))))),
        el('button', { type: 'submit', class: 'btn btn-primary' }, 'Book inspection'));
      area.replaceChildren(form);
    } catch (err) {
      area.replaceChildren(el('p', { class: 'field-error', role: 'alert' }, err.body.error || 'Times could not be loaded.'));
    }
  }

  async function book(event, type) {
    event.preventDefault();
    const slotId = new FormData(event.target).get('slot');
    try {
      await PermitDesk.post(`/api/applications/${id}/inspections`, { slotId, inspectionType: type });
      say('Inspection requested. We will confirm the time shortly.');
      current = null;
      await load();
    } catch (err) {
      say(err.body.error || 'Something went wrong booking your inspection.', true);
    }
  }

  withdrawDialog.addEventListener('close', async () => {
    if (withdrawDialog.returnValue !== 'confirm') return;
    try {
      await PermitDesk.post(`/api/applications/${id}/withdraw`, { reason: document.getElementById('withdraw-reason').value });
      say('Your application has been withdrawn.');
      await load();
      heading.focus();
    } catch (err) {
      say(err.body.error || 'The application could not be withdrawn.', true);
    }
  });

  async function start() {
    const [types, inspections] = await Promise.all([
      PermitDesk.get('/api/reference/work-types'),
      PermitDesk.get('/api/reference/inspection-types'),
    ]);
    workTypes = Object.fromEntries(types.map((t) => [t.code, t.label]));
    inspectionTypes = inspections;

    try {
      await load();
    } catch (err) {
      say(err.status === 404 ? 'We could not find that application.' : 'The application could not be loaded.', true);
      return;
    }

    if (params.has('submitted')) say(`Application ${current.reference} submitted. We will check zoning first, which usually takes about a minute.`);

    setInterval(() => {
      const waitingOnBooking = current && current.inspections.some((i) => i.status === 'pending');
      if (current && (MOVING.includes(current.state) || waitingOnBooking)) load(true).catch(() => {});
    }, 5000);
  }

  start();
})();
