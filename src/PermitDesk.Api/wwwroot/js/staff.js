(function () {
  if (!PermitDesk.requireSession()) return;
  PermitDesk.mountHeader('/staff.html');

  var results = document.getElementById('results');
  var count = document.getElementById('result-count');
  var form = document.getElementById('search-form');

  function contact(row) {
    var parts = [row.applicantName, row.contactEmail, row.contactPhone];
    if (row.contractorLicenceNumber) parts.push('Licence ' + row.contractorLicenceNumber);
    return parts.filter(Boolean).join('<br>');
  }

  function actions(row) {
    var html = '';
    if (row.state === 'zoning_hold') {
      html += '<button type="button" class="btn" data-action="pass" data-id="' + row.id + '">Zoning OK</button> ';
      html += '<button type="button" class="btn btn-danger" data-action="fail" data-id="' + row.id + '">Not permitted</button> ';
    }
    if (['denied', 'withdrawn', 'expired', 'issued'].indexOf(row.state) === -1) {
      html += '<button type="button" class="btn" data-action="withdraw" data-id="' + row.id + '">Withdraw</button>';
    }
    return html;
  }

  function render(rows) {
    results.innerHTML = rows.map(function (row) {
      return '<tr>' +
        '<td><a href="/application.html?id=' + row.id + '">' + row.reference + '</a></td>' +
        '<td>' + row.stateLabel + '</td>' +
        '<td>' + (row.parcelAddress || '') + '</td>' +
        '<td>' + contact(row) + '</td>' +
        '<td>' + (row.scopeOfWork || '') + '</td>' +
        '<td>' + PermitDesk.money(row.declaredValuation) + '</td>' +
        '<td>' + actions(row) + '</td>' +
        '</tr>';
    }).join('');
    count.textContent = rows.length + (rows.length === 1 ? ' application' : ' applications');
  }

  function search() {
    var query = new URLSearchParams();
    ['parcel', 'applicant', 'state', 'from', 'to'].forEach(function (name) {
      var value = document.getElementById(name).value;
      if (value) query.set(name, value);
    });
    return PermitDesk.get('/api/staff/applications?' + query.toString()).then(render);
  }

  results.addEventListener('click', function (event) {
    var button = event.target.closest('button[data-action]');
    if (!button) return;

    var id = button.getAttribute('data-id');
    var action = button.getAttribute('data-action');
    var request;

    if (action === 'withdraw') {
      var reason = window.prompt('Why is this application being withdrawn?');
      if (!reason) return;
      request = PermitDesk.post('/api/staff/applications/' + id + '/withdraw', { reason: reason });
    } else {
      var note = window.prompt(action === 'pass'
        ? 'Record how the parcel conflict was settled:'
        : 'Record why the work is not permitted:');
      if (!note) return;
      request = PermitDesk.post('/api/staff/applications/' + id + '/zoning-resolution', { outcome: action, note: note });
    }

    request.then(search).catch(function (err) {
      window.alert((err.body && err.body.error) || 'That did not work.');
    });
  });

  form.addEventListener('submit', function (event) {
    event.preventDefault();
    search();
  });

  search();
})();
