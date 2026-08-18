const http = require('http');
const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');

let port = Number(process.env.PORT || 8080);
const token = crypto.randomBytes(12).toString('hex');
const page = fs.readFileSync(path.join(__dirname, 'public', 'index.html'));
const queues = new Map();

function tvAddresses() {
  return Object.values(os.networkInterfaces()).flat()
    .filter(x => x && x.family === 'IPv4' && !x.internal)
    .sort((a, b) => Number(b.address.startsWith('192.168.') || b.address.startsWith('10.')) - Number(a.address.startsWith('192.168.') || a.address.startsWith('10.')))
    .map(x => `http://${x.address}:${port}/tv`);
}

function localRequest(req) {
  return ['127.0.0.1', '::1', '::ffff:127.0.0.1'].includes(req.socket.remoteAddress);
}

function authorized(req, url) {
  return localRequest(req) || url.searchParams.get('token') === token || req.headers.cookie?.split(';').some(x => x.trim() === `remoteplay=${token}`);
}

function json(res, status, value) {
  res.writeHead(status, { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' });
  res.end(JSON.stringify(value));
}

function body(req) {
  return new Promise((resolve, reject) => {
    let data = '';
    req.on('data', chunk => {
      data += chunk;
      if (data.length > 1_000_000) req.destroy();
    });
    req.on('end', () => {
      try { resolve(JSON.parse(data || '{}')); } catch (error) { reject(error); }
    });
    req.on('error', reject);
  });
}

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, `http://${req.headers.host}`);

  if (req.method === 'GET' && url.pathname === '/tv') {
    res.writeHead(200, {
      'content-type': 'text/html; charset=utf-8',
      'cache-control': 'no-store',
      'set-cookie': `remoteplay=${token}; Path=/; HttpOnly; SameSite=Strict`
    });
    return res.end(page);
  }
  if (url.pathname === '/api/config' && authorized(req, url)) return json(res, 200, { token, tvUrls: tvAddresses() });
  if (!authorized(req, url)) return json(res, 403, { error: 'Недействительная ссылка' });

  if (req.method === 'GET' && url.pathname === '/') {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
    return res.end(page);
  }

  if (req.method === 'POST' && url.pathname === '/api/send') {
    try {
      const message = await body(req);
      if (!message.to || !message.from || !message.type) return json(res, 400, { error: 'Некорректное сообщение' });
      const queue = queues.get(message.to) || [];
      queue.push(message);
      queues.set(message.to, queue.slice(-100));
      return json(res, 200, { ok: true });
    } catch {
      return json(res, 400, { error: 'Некорректный JSON' });
    }
  }

  if (req.method === 'GET' && url.pathname === '/api/poll') {
    const id = url.searchParams.get('id');
    if (!id) return json(res, 400, { error: 'Нужен id' });
    const messages = queues.get(id) || [];
    queues.delete(id);
    return json(res, 200, messages);
  }

  json(res, 404, { error: 'Не найдено' });
});

server.on('error', error => {
  if (error.code === 'EADDRINUSE' && !process.env.PORT) {
    port += 1;
    return server.listen(port, '0.0.0.0');
  }
  console.error(`Не удалось запустить сервер: ${error.message}`);
  process.exitCode = 1;
});

server.on('listening', () => {
  const addresses = tvAddresses();
  console.log(`\nНа компьютере: http://localhost:${port}`);
  console.log('На телевизоре:');
  addresses.forEach(address => console.log(`  ${address}`));
  console.log('\nОстановить: Ctrl+C\n');
});

server.listen(port, '0.0.0.0');
