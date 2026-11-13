// Stand-in for the Parcel & Zoning Service.
//
// Checks are asynchronous. Real checks take 20-60 seconds; this one answers in
// a few seconds so a local run is watchable. Results are pushed to Permit Desk
// by webhook, and a share of those webhooks is dropped on purpose because the
// real service loses them too.
const { createService, postSigned, id, between, env } = require('./lib');
const parcels = require('./data/parcels.json');

const WEBHOOK_URL = env('PERMIT_DESK_WEBHOOK_URL', 'http://api:8080/hooks/zoning');
const WEBHOOK_SECRET = env('WEBHOOK_SECRET', '');
const DROP_RATE = Number(env('WEBHOOK_DROP_RATE', '0.25'));
const MIN_DELAY = Number(env('CHECK_MIN_MS', '4000'));
const MAX_DELAY = Number(env('CHECK_MAX_MS', '9000'));
// Nightly parcel index rebuild, "HH:MM" UTC, ten minutes long.
const REBUILD_AT = env('INDEX_REBUILD_UTC', '02:00');

const checks = new Map();
let rebuildUntil = 0;

function rebuilding() {
  if (Date.now() < rebuildUntil) return true;
  const [hour, minute] = REBUILD_AT.split(':').map(Number);
  const now = new Date();
  const start = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate(), hour, minute);
  return now.getTime() >= start && now.getTime() < start + 10 * 60 * 1000;
}

function decide(parcelNumber, workType) {
  const parcel = parcels[parcelNumber];
  if (!parcel) return { result: 'fail', reason: `Parcel ${parcelNumber} is not in the parcel index` };
  if (parcel.conflict) return { result: 'indeterminate', reason: parcel.conflict };
  if (parcel.zoning === 'OS') {
    return { result: 'fail', reason: 'Building work is not permitted in the Open Space district' };
  }
  if (workType === 'commercial_fitout' && parcel.zoning.startsWith('R')) {
    return { result: 'fail', reason: `Commercial use is not permitted in residential district ${parcel.zoning}` };
  }
  return { result: 'pass', reason: `Permitted under ${parcel.zoning}` };
}

const unavailable = (send) => send(503, { error: 'index_rebuild_in_progress' }, { 'Retry-After': '120' });

createService({
  name: 'zoning',
  port: Number(env('PORT', '7101')),
  apiKey: env('API_KEY', ''),
  publicPaths: ['/admin/'],
  routes: [
    {
      method: 'POST',
      path: /^\/checks$/,
      handler: ({ body, send }) => {
        if (rebuilding()) return unavailable(send);
        const { parcelNumber, workType, scopeOfWork } = body;
        if (!parcelNumber || !workType || !scopeOfWork) return send(422, { error: 'missing_fields' });
        if (scopeOfWork.length > 4000) return send(422, { error: 'scope_too_long', limit: 4000 });

        const handle = id('zc');
        checks.set(handle, { handle, status: 'pending', result: null, reason: null });

        setTimeout(async () => {
          const outcome = decide(parcelNumber, workType);
          checks.set(handle, { handle, status: 'complete', ...outcome });
          if (Math.random() < DROP_RATE) {
            console.log(`[zoning] ${handle} complete (${outcome.result}), webhook dropped`);
            return;
          }
          try {
            const status = await postSigned(WEBHOOK_URL, WEBHOOK_SECRET, { handle, ...outcome });
            console.log(`[zoning] ${handle} complete (${outcome.result}), webhook -> ${status}`);
          } catch (err) {
            console.log(`[zoning] ${handle} webhook failed: ${err.message}`);
          }
        }, between(MIN_DELAY, MAX_DELAY));

        send(202, { handle });
      },
    },
    {
      method: 'GET',
      path: /^\/checks\/([\w-]+)$/,
      handler: ({ params, send }) => {
        if (rebuilding()) return unavailable(send);
        const check = checks.get(params[0]);
        return check ? send(200, check) : send(404, { error: 'unknown_handle' });
      },
    },
    {
      // Start an index rebuild window by hand: POST /admin/rebuild {"seconds": 120}
      method: 'POST',
      path: /^\/admin\/rebuild$/,
      handler: ({ body, send }) => {
        rebuildUntil = Date.now() + Number(body.seconds || 120) * 1000;
        send(200, { rebuildingUntil: new Date(rebuildUntil).toISOString() });
      },
    },
  ],
});
