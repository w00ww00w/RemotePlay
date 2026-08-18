const http = require('http');
const https = require('https');

const targetText = process.argv[2];
if (!targetText) {
  console.error('Использование: npm run sender -- http://192.168.1.10:8080/tv');
  process.exit(1);
}

let target;
try {
  target = new URL(targetText);
} catch {
  console.error('Некорректный адрес основного сервера. Скопируйте ссылку /tv из его терминала.');
  process.exit(1);
}

if (!['http:', 'https:'].includes(target.protocol)) {
  console.error('Адрес должен начинаться с http:// или https://');
  process.exit(1);
}

let port = Number(process.env.SENDER_PORT || 8090);
const transport = target.protocol === 'https:' ? https : http;

const server = http.createServer((req, res) => {
  const path = req.url === '/' ? '/tv' : req.url;
  const upstream = transport.request(new URL(path, target.origin), {
    method: req.method,
    headers: { ...req.headers, host: target.host }
  }, response => {
    res.writeHead(response.statusCode, response.headers);
    response.pipe(res);
  });

  upstream.on('error', error => {
    res.writeHead(502, { 'content-type': 'text/plain; charset=utf-8' });
    res.end(`Основной сервер недоступен: ${error.message}`);
  });
  req.pipe(upstream);
});

server.on('error', error => {
  if (error.code === 'EADDRINUSE' && !process.env.SENDER_PORT) {
    port += 1;
    return server.listen(port, '127.0.0.1');
  }
  console.error(`Не удалось запустить источник: ${error.message}`);
  process.exitCode = 1;
});

server.on('listening', () => {
  console.log(`\nОткройте на этом компьютере: http://localhost:${port}`);
  console.log('Основной сервер:', target.origin);
  console.log('\nОстановить: Ctrl+C\n');
});

server.listen(port, '127.0.0.1');
