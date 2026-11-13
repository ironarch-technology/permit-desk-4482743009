(() => {
  if (!PermitDesk.requireSession()) return;
  PermitDesk.mountHeader('/apply.html');

  const { el, money } = PermitDesk;
  const FIELDS = ['parcelAddress', 'workType', 'declaredValuation', 'scopeOfWork', 'applicantType',
    'contactName', 'contactEmail', 'contactPhone', 'contractorLicenceNumber', 'contractorLicenceExpiry'];

  const form = document.getElementById('apply-form');
  const formErrors = document.getElementById('form-errors');
  const input = (name) => document.getElementById(name);
  const suggestions = document.getElementById('suggestions');
  const contractorFields = document.getElementById('contractor-fields');
  const saveStatus = document.getElementById('save-status');
  const submitButton = document.getElementById('submit-button');

  let draftId = Number(new URLSearchParams(window.location.search).get('draft')) || null;
  let workTypes = [];
  // One key per visit to this form, so a double click or a retry after a
  // dropped connection cannot create a second application.
  const submissionKey = crypto.randomUUID();

  function values() {
    const valuation = input('declaredValuation').value;
    return {
      parcelAddress: input('parcelAddress').value,
      workType: input('workType').value || null,
      declaredValuation: valuation === '' ? null : Number(valuation),
      scopeOfWork: input('scopeOfWork').value,
      applicantType: input('applicantType').value,
      contactName: input('contactName').value,
      contactEmail: input('contactEmail').value,
      contactPhone: input('contactPhone').value,
      contractorLicenceNumber: input('contractorLicenceNumber').value || null,
      contractorLicenceExpiry: input('contractorLicenceExpiry').value || null,
    };
  }

  function clearErrors() {
    formErrors.replaceChildren();
    form.querySelectorAll('.field-error').forEach((node) => node.remove());
    form.querySelectorAll('.has-error').forEach((node) => node.classList.remove('has-error'));
  }

  function showErrors(body) {
    clearErrors();
    const errors = body.errors || {};
    formErrors.replaceChildren(el('div', { class: 'form-errors' }, body.error || 'Some details need attention.'));

    for (const [name, text] of Object.entries(errors)) {
      const field = input(name);
      if (!field) continue;
      field.classList.add('has-error');
      field.closest('.field').append(el('div', { class: 'field-error' }, text));
    }
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  function updateValuationHint() {
    const type = workTypes.find((t) => t.code === input('workType').value);
    document.getElementById('valuation-hint').textContent = type
      ? `${type.label} permits cover work valued from ${money(type.minValuation)} to ${money(type.maxValuation)}.`
      : '';
  }

  function updateContractorFields() {
    contractorFields.hidden = input('applicantType').value !== 'contractor';
  }

  let searchTimer = null;
  function searchParcels() {
    clearTimeout(searchTimer);
    const term = input('parcelAddress').value.trim();
    if (term.length < 3) {
      suggestions.hidden = true;
      return;
    }
    searchTimer = setTimeout(async () => {
      const matches = await PermitDesk.get(`/api/parcels/search?q=${encodeURIComponent(term)}`);
      suggestions.replaceChildren(...matches.map((parcel) =>
        el('li', {}, el('button', {
          type: 'button',
          onclick: () => {
            input('parcelAddress').value = parcel.address;
            suggestions.hidden = true;
          },
        }, `${parcel.address}, ${parcel.district}`))));
      suggestions.hidden = matches.length === 0;
    }, 250);
  }

  async function saveDraft() {
    clearErrors();
    try {
      const saved = draftId
        ? await PermitDesk.put(`/api/applications/${draftId}`, values())
        : await PermitDesk.post('/api/applications', values());
      draftId = saved.id;
      window.history.replaceState(null, '', `/apply.html?draft=${draftId}`);
      saveStatus.textContent = `Draft ${saved.reference} saved.`;
    } catch (err) {
      showErrors(err.body);
    }
  }

  async function submit(event) {
    event.preventDefault();
    submitButton.disabled = true;
    try {
      const application = await PermitDesk.post('/api/submissions', { ...values(), draftId, submissionKey });
      window.location.href = `/application.html?id=${application.id}&submitted=1`;
    } catch (err) {
      showErrors(err.status === 429 ? { error: 'Too many requests. Wait a minute and try again.' } : err.body);
      submitButton.disabled = false;
    }
  }

  async function load() {
    workTypes = await PermitDesk.get('/api/reference/work-types');
    for (const type of workTypes) input('workType').append(el('option', { value: type.code }, type.label));

    if (draftId) {
      const draft = await PermitDesk.get(`/api/applications/${draftId}`);
      const flat = { ...draft, ...draft.contact, contactName: draft.contact.name, contactEmail: draft.contact.email, contactPhone: draft.contact.phone };
      if (draft.contractor) {
        flat.contractorLicenceNumber = draft.contractor.licenceNumber;
        flat.contractorLicenceExpiry = draft.contractor.licenceExpiry;
      }
      for (const name of FIELDS) {
        if (flat[name] !== null && flat[name] !== undefined) input(name).value = flat[name];
      }
    } else {
      const me = await PermitDesk.get('/api/auth/me');
      input('contactName').value = me.name || '';
      input('contactEmail').value = me.email || '';
      input('contactPhone').value = me.phone || '';
    }

    updateValuationHint();
    updateContractorFields();
  }

  input('parcelAddress').addEventListener('input', searchParcels);
  input('workType').addEventListener('change', updateValuationHint);
  input('applicantType').addEventListener('change', updateContractorFields);
  document.getElementById('save-button').addEventListener('click', saveDraft);
  form.addEventListener('submit', submit);
  load();
})();
