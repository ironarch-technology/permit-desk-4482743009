// Stand-in for the Cashiering Service.
//
// Quotes come from a made-up fee schedule. Payment happens on this service's
// own checkout page (a single button here), and the capture is reported to
// Permit Desk by signed webhook.
const { createService, postSigned, id, env } = require('./lib');

const WEBHOOK_URL = env('PERMIT_DESK_WEBHOOK_URL', 'http://api:8080/hooks/cashiering');
const WEBHOOK_SECRET = env('WEBHOOK_SECRET', '');
const PUBLIC_URL = env('PUBLIC_URL', 'http://localhost:7104');
const PORTAL_URL = env('PORTAL_URL', 'http://localhost:8080');
const QUOTE_TTL_MINUTES = Number(env('QUOTE_TTL_MINUTES', '30'));

const BASE_FEE = { deck_patio: 95, reroof: 180, interior_remodel: 240, addition: 410, new_construction: 980, commercial_fitout: 620, demolition: 350 };
const DISTRICT_RATE = { 'North Ridge': 0.05, Eastbank: 0.035, 'Old Harbour': 0.0825, Millworks: 0.02 };

const quotes = new Map();
const payments = new Map();
const cents = (value) => Math.round(value * 100) / 100;

function buildQuote({ applicationRef, workType, valuation, district }) {
  const base = BASE_FEE[workType] ?? 200;
  const valuationFee = cents(valuation * 0.0125);
  const surcharge = cents((base + valuationFee) * (DISTRICT_RATE[district] ?? 0.04));
  const lines = [
    { code: 'BASE', label: 'Permit base fee', amount: base },
    { code: 'VAL', label: 'Valuation fee', amount: valuationFee },
    { code: 'DIST', label: `${district} district surcharge`, amount: surcharge },
    { code: 'TECH', label: 'Records technology fee', amount: 8.35 },
  ];
  const total = cents(lines.reduce((sum, line) => sum + Math.round(line.amount * 100), 0) / 100);
  return {
    quoteId: id('q'),
    applicationRef,
    lines,
    total,
    expiresAt: new Date(Date.now() + QUOTE_TTL_MINUTES * 60 * 1000).toISOString(),
  };
}

async function notify(payment) {
  const event = { paymentId: payment.paymentId, status: 'captured', amount: payment.amount, capturedAt: payment.capturedAt };
  for (let attempt = 1; attempt <= 4; attempt++) {
    try {
      const status = await postSigned(WEBHOOK_URL, WEBHOOK_SECRET, event);
      console.log(`[cashiering] ${payment.paymentId} webhook attempt ${attempt} -> ${status}`);
      if (status < 300) return;
    } catch (err) {
      console.log(`[cashiering] ${payment.paymentId} webhook attempt ${attempt} failed: ${err.message}`);
    }
    await new Promise((resolve) => setTimeout(resolve, attempt * 2000));
  }
}

const page = (title, body) => `<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1"><title>${title} - City of Mountport Cashiering</title>
<style>body{font-family:system-ui,sans-serif;max-width:32rem;margin:4rem auto;padding:0 1rem;line-height:1.5}
button{font-size:1rem;padding:.6rem 1.2rem}</style></head><body><h1>City of Mountport Cashiering</h1>${body}</body></html>`;

createService({
  name: 'cashiering',
  port: Number(env('PORT', '7104')),
  apiKey: env('API_KEY', ''),
  publicPaths: ['/checkout/'],
  routes: [
    {
      method: 'POST',
      path: /^\/quotes$/,
      handler: ({ body, send }) => {
        if (!body.applicationRef || !body.workType || !(Number(body.valuation) > 0)) return send(422, { error: 'missing_fields' });
        const quote = buildQuote({ ...body, valuation: Number(body.valuation) });
        quotes.set(quote.quoteId, quote);
        send(201, quote);
      },
    },
    {
      method: 'POST',
      path: /^\/payments$/,
      handler: ({ body, send }) => {
        const quote = quotes.get(body.quoteId);
        if (!quote) return send(404, { error: 'unknown_quote' });
        if (new Date(quote.expiresAt) <= new Date()) return send(410, { error: 'quote_expired' });
        if (Math.abs(Number(body.amount) - quote.total) > 0.005) return send(422, { error: 'amount_mismatch', expected: quote.total });

        const paymentId = id('pay');
        payments.set(paymentId, { paymentId, quoteId: quote.quoteId, applicationRef: quote.applicationRef, amount: quote.total, status: 'pending' });
        send(201, { paymentId, checkoutUrl: `${PUBLIC_URL}/checkout/${paymentId}` });
      },
    },
    {
      method: 'GET',
      path: /^\/checkout\/([\w-]+)$/,
      handler: ({ params, send }) => {
        const payment = payments.get(params[0]);
        if (!payment) return send(404, page('Not found', '<p>We could not find that payment.</p>'));
        if (payment.status === 'captured') {
          return send(200, page('Payment received', `<p>Payment received for ${payment.applicationRef}.</p><p><a href="${PORTAL_URL}/applications.html">Return to the permit portal</a></p>`));
        }
        send(200, page('Pay permit fees', `<p>Permit fees for <strong>${payment.applicationRef}</strong></p>
<p>Amount due: <strong>$${payment.amount.toFixed(2)}</strong></p>
<p>This is a test checkout. No card details are collected.</p>
<form method="post" action="/checkout/${payment.paymentId}/capture"><button type="submit">Pay $${payment.amount.toFixed(2)}</button></form>`));
      },
    },
    {
      method: 'POST',
      path: /^\/checkout\/([\w-]+)\/capture$/,
      handler: ({ params, send }) => {
        const payment = payments.get(params[0]);
        if (!payment) return send(404, page('Not found', '<p>We could not find that payment.</p>'));
        if (payment.status !== 'captured') {
          payment.status = 'captured';
          payment.capturedAt = new Date().toISOString();
          notify(payment);
        }
        send(303, '', { Location: `/checkout/${payment.paymentId}` });
      },
    },
  ],
});
