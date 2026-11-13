// Stand-in for the Inspection Scheduler.
//
// Slot search is immediate. Booking is accepted and then confirmed a few
// seconds later. Slots are contended: other callers take them between a
// search and a booking, and the booking then gets 409 with alternatives.
const { createService, id, env } = require('./lib');

const CONTENTION_RATE = Number(env('CONTENTION_RATE', '0.35'));
const CONFIRM_MS = Number(env('CONFIRM_MS', '4000'));
const HOURS = [9, 11, 14];
const CAPACITY = { 'North Ridge': 2, Eastbank: 2, 'Old Harbour': 1, Millworks: 1 };

const taken = new Map();
const bookings = new Map();

function slotsFor(type, district) {
  const slots = [];
  const day = new Date();
  day.setUTCHours(0, 0, 0, 0);
  while (slots.length < 15) {
    day.setUTCDate(day.getUTCDate() + 1);
    if (day.getUTCDay() === 0 || day.getUTCDay() === 6) continue;
    for (const hour of HOURS) {
      const start = new Date(day);
      start.setUTCHours(hour + 5);
      const slotId = `${district.replace(/\W/g, '').toLowerCase()}-${type}-${start.toISOString().slice(0, 13)}`;
      const remaining = (CAPACITY[district] || 1) - (taken.get(slotId) || 0);
      if (remaining > 0) slots.push({ slotId, start: start.toISOString(), inspectionType: type, district, remaining });
    }
  }
  return slots;
}

function parse(slotId) {
  const match = slotId.match(/^([a-z]+)-([a-z]+)-(\d{4}-\d{2}-\d{2}T\d{2})$/);
  if (!match) return null;
  const district = Object.keys(CAPACITY).find((d) => d.replace(/\W/g, '').toLowerCase() === match[1]);
  return district ? { district, type: match[2], start: `${match[3]}:00:00.000Z` } : null;
}

createService({
  name: 'scheduling',
  port: Number(env('PORT', '7103')),
  apiKey: env('API_KEY', ''),
  routes: [
    {
      method: 'GET',
      path: /^\/slots$/,
      handler: ({ url, send }) => {
        const type = url.searchParams.get('type');
        const district = url.searchParams.get('district');
        if (!type || !district) return send(422, { error: 'missing_fields' });
        send(200, slotsFor(type, district));
      },
    },
    {
      method: 'POST',
      path: /^\/bookings$/,
      handler: ({ body, send }) => {
        const slot = body.slotId && parse(body.slotId);
        if (!slot) return send(422, { error: 'unknown_slot' });

        const used = taken.get(body.slotId) || 0;
        const full = used >= (CAPACITY[slot.district] || 1);
        if (full || Math.random() < CONTENTION_RATE) {
          // Somebody else got there first.
          taken.set(body.slotId, CAPACITY[slot.district] || 1);
          const alternatives = slotsFor(slot.type, slot.district).slice(0, 3);
          return send(409, { error: 'slot_taken', alternatives });
        }

        taken.set(body.slotId, used + 1);
        const bookingId = id('bk');
        bookings.set(bookingId, { bookingId, status: 'pending', slotId: body.slotId, start: slot.start });
        setTimeout(() => {
          bookings.set(bookingId, { ...bookings.get(bookingId), status: 'confirmed' });
        }, CONFIRM_MS);
        send(202, bookings.get(bookingId));
      },
    },
    {
      method: 'GET',
      path: /^\/bookings\/([\w-]+)$/,
      handler: ({ params, send }) => {
        const booking = bookings.get(params[0]);
        return booking ? send(200, booking) : send(404, { error: 'unknown_booking' });
      },
    },
  ],
});
