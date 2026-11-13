// Stand-in for the Plan Review Service.
//
// Real reviews take hours to days. Here a "reviewer" decides after a few
// seconds, using fixed rules so a local run is predictable:
//   - first pass: corrections required
//   - large projects get a second round of corrections
//   - large demolitions are denied
//   - otherwise approved
const { createService, id, env } = require('./lib');

const DECISION_MS = Number(env('DECISION_MS', '6000'));
const reviews = new Map();

const CORRECTIONS = {
  'EGR-102': {
    code: 'EGR-102',
    narrative: 'Show the egress path and door swing for every habitable room affected by the work.',
    citation: 'Mountport Building Code 10.02.102',
  },
  'STR-210': {
    code: 'STR-210',
    narrative: 'Provide structural calculations for new or altered load-bearing members, stamped by a structural engineer.',
    citation: 'Mountport Building Code 16.04.210',
  },
  'ENR-330': {
    code: 'ENR-330',
    narrative: 'Provide the energy compliance worksheet for the altered building envelope.',
    citation: 'Mountport Energy Code 13.03.330',
  },
};

function decide({ cycle, workType, valuation }) {
  if (workType === 'demolition' && valuation > 150000) {
    return { outcome: 'denied', reason: 'Demolition of this scale requires a heritage review before a permit can be considered.' };
  }
  if (cycle === 0) {
    return { outcome: 'corrections_required', items: [CORRECTIONS['EGR-102'], CORRECTIONS['STR-210']] };
  }
  if (cycle === 1 && valuation >= 250000) {
    return { outcome: 'corrections_required', items: [CORRECTIONS['STR-210'], CORRECTIONS['ENR-330']] };
  }
  return { outcome: 'approved', reason: 'Plans conform to the Mountport Building Code.' };
}

createService({
  name: 'review',
  port: Number(env('PORT', '7102')),
  apiKey: env('API_KEY', ''),
  routes: [
    {
      method: 'POST',
      path: /^\/reviews$/,
      handler: ({ body, send }) => {
        if (!body.applicationRef || !body.workType) return send(422, { error: 'missing_fields' });
        const reviewId = id('rv');
        reviews.set(reviewId, { status: 'in_review' });
        setTimeout(() => {
          const decision = decide({ cycle: Number(body.cycle || 0), workType: body.workType, valuation: Number(body.valuation || 0) });
          reviews.set(reviewId, { status: 'decided', ...decision });
          console.log(`[review] ${reviewId} (${body.applicationRef}, cycle ${body.cycle}) -> ${decision.outcome}`);
        }, DECISION_MS);
        send(201, { reviewId });
      },
    },
    {
      method: 'GET',
      path: /^\/reviews\/([\w-]+)$/,
      handler: ({ params, send }) => {
        const review = reviews.get(params[0]);
        return review ? send(200, review) : send(404, { error: 'unknown_review' });
      },
    },
  ],
});
