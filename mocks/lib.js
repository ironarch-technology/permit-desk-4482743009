// Shared plumbing for the stand-in city services. No dependencies.
const http = require('http');
const crypto = require('crypto');

function createService({ name, port, apiKey, routes, publicPaths = [] }) {
  const server = http.createServer(async (req, res) => {
    const url = new URL(req.url, `http://${req.headers.host}`);
    const send = (status, body, headers = {}) => {
      const isText = typeof body === 'string';
      res.writeHead(status, { 'Content-Type': isText ? 'text/html; charset=utf-8' : 'application/json', ...headers });
      res.end(isText ? body : JSON.stringify(body ?? {}));
    };

    try {
      const isPublic = publicPaths.some((prefix) => url.pathname.startsWith(prefix));
      if (!isPublic && url.pathname !== '/health' && req.headers['x-api-key'] !== apiKey) {
        return send(401, { error: 'invalid_api_key' });
      }
      if (url.pathname === '/health') return send(200, { status: 'ok', service: name });

      let body = {};
      if (req.method === 'POST') {
        const raw = await readBody(req);
        if (raw) {
          try { body = JSON.parse(raw); } catch { return send(400, { error: 'invalid_json' }); }
        }
      }

      for (const route of routes) {
        if (route.method !== req.method) continue;
        const match = url.pathname.match(route.path);
        if (!match) continue;
        return await route.handler({ req, url, body, params: match.slice(1), send });
      }
      send(404, { error: 'not_found' });
    } catch (err) {
      console.error(`[${name}]`, err);
      send(500, { error: 'internal_error' });
    }
  });

  server.listen(port, () => console.log(`[${name}] listening on ${port}`));
  process.on('SIGTERM', () => server.close(() => process.exit(0)));
  return server;
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    let data = '';
    req.on('data', (chunk) => { data += chunk; });
    req.on('end', () => resolve(data));
    req.on('error', reject);
  });
}

// Signed callback to Permit Desk: X-Signature: sha256=<hex hmac of the body>.
async function postSigned(url, secret, payload) {
  const body = JSON.stringify(payload);
  const signature = crypto.createHmac('sha256', secret).update(body).digest('hex');
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Signature': `sha256=${signature}` },
    body,
  });
  return response.status;
}

const id = (prefix) => `${prefix}-${crypto.randomBytes(5).toString('hex')}`;
const between = (min, max) => min + Math.floor(Math.random() * (max - min + 1));
const env = (name, fallback) => (process.env[name] === undefined ? fallback : process.env[name]);

module.exports = { createService, postSigned, id, between, env };
