import { createServer } from 'node:http';
import { request } from 'node:https';

const origin = process.env.PULSE_ORIGIN ? new URL(process.env.PULSE_ORIGIN) : null;
if (origin && (origin.protocol !== 'https:' || origin.href !== origin.origin + '/')) {
  throw new Error('PULSE_ORIGIN must be an HTTPS origin without a path');
}

const hopHeaders = [
  'connection', 'keep-alive', 'proxy-authenticate', 'proxy-authorization',
  'te', 'trailer', 'transfer-encoding', 'upgrade',
];

function endToEndHeaders(headers) {
  const result = { ...headers };
  const connectionHeaders = String(headers.connection ?? '').toLowerCase().split(',');
  for (const name of [...hopHeaders, ...connectionHeaders]) {
    delete result[name.trim()];
  }
  return result;
}

function requestOrigin(incoming, upgrade = false) {
  const headers = endToEndHeaders(incoming.headers);
  delete headers.forwarded;
  for (const name of Object.keys(headers)) {
    if (name.startsWith('x-forwarded-')) delete headers[name];
  }
  headers.host = origin.host;
  headers['x-pulse-edge-key'] = process.env.PULSE_EDGE_KEY;
  if (upgrade) {
    headers.connection = 'Upgrade';
    headers.upgrade = 'websocket';
  }
  return request(origin, {
    method: incoming.method,
    path: incoming.url,
    headers,
    ca: process.env.PULSE_ORIGIN_CA || undefined,
  });
}

const configured = Boolean(origin && process.env.PULSE_EDGE_KEY);
const server = createServer((incoming, outgoing) => {
  if (incoming.url === '/healthz') {
    outgoing.writeHead(200, { 'cache-control': 'no-store' });
    outgoing.end('OK');
    return;
  }
  if (!configured) {
    outgoing.writeHead(503, {
      'content-type': 'text/plain; charset=utf-8',
      'cache-control': 'no-store',
    });
    outgoing.end('Пульс. Подключаем сервер клуба.');
    return;
  }

  const upstream = requestOrigin(incoming);
  upstream.on('response', (response) => {
    const headers = endToEndHeaders(response.headers);
    // Keep user sessions and form tokens out of the hosting cache.
    headers['cache-control'] = 'private, no-store';
    if (headers.location?.startsWith(origin.origin + '/')) {
      headers.location = headers.location.slice(origin.origin.length);
    }
    outgoing.writeHead(response.statusCode, headers);
    response.on('error', () => outgoing.destroy());
    response.pipe(outgoing);
  });
  upstream.on('error', () => {
    if (outgoing.headersSent) outgoing.destroy();
    else {
      outgoing.writeHead(502, { 'cache-control': 'no-store' });
      outgoing.end('Pulse server unavailable');
    }
  });
  upstream.setTimeout(45_000, () => upstream.destroy());
  outgoing.on('close', () => upstream.destroy());
  incoming.pipe(upstream);
});

server.on('upgrade', (incoming, socket, head) => {
  if (!configured || incoming.headers.upgrade?.toLowerCase() !== 'websocket') {
    socket.end('HTTP/1.1 503 Service Unavailable\r\nConnection: close\r\n\r\n');
    return;
  }
  const upstream = requestOrigin(incoming, true);
  socket.on('error', () => upstream.destroy());
  socket.on('close', () => upstream.destroy());
  upstream.on('error', () => socket.destroy());
  upstream.on('response', (response) => {
    response.resume();
    socket.end('HTTP/1.1 502 Bad Gateway\r\nConnection: close\r\n\r\n');
  });
  upstream.on('upgrade', (response, originSocket, originHead) => {
    originSocket.setTimeout(0);
    let handshake = 'HTTP/1.1 101 Switching Protocols\r\n';
    for (let index = 0; index < response.rawHeaders.length; index += 2) {
      handshake += response.rawHeaders[index] + ': ' + response.rawHeaders[index + 1] + '\r\n';
    }
    socket.write(handshake + '\r\n');
    if (originHead.length) socket.write(originHead);
    if (head.length) originSocket.write(head);
    originSocket.on('error', () => socket.destroy());
    originSocket.on('close', () => socket.destroy());
    socket.on('close', () => originSocket.destroy());
    socket.pipe(originSocket).pipe(socket);
  });
  upstream.setTimeout(15_000, () => upstream.destroy());
  upstream.end();
});

server.listen(Number(process.env.PORT || 3000), '0.0.0.0');
