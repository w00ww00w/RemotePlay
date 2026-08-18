const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const root = path.join(__dirname, '..');
const entries = [
  ['RemotePlay Server.app/', null, 0o755, '5'],
  ['RemotePlay Server.app/Contents/', null, 0o755, '5'],
  ['RemotePlay Server.app/Contents/Info.plist', 'packaging/macos/Info.plist', 0o644, '0'],
  ['RemotePlay Server.app/Contents/MacOS/', null, 0o755, '5'],
  ['RemotePlay Server.app/Contents/MacOS/RemotePlayServer', 'release/server-macos-arm64/RemotePlayServer', 0o755, '0']
];

function octal(buffer, offset, length, value) {
  buffer.write(value.toString(8).padStart(length - 1, '0') + '\0', offset, length, 'ascii');
}

function header(name, size, mode, type) {
  const value = Buffer.alloc(512);
  value.write(name, 0, 100, 'utf8');
  octal(value, 100, 8, mode);
  octal(value, 108, 8, 0);
  octal(value, 116, 8, 0);
  octal(value, 124, 12, size);
  octal(value, 136, 12, Math.floor(Date.now() / 1000));
  value.fill(0x20, 148, 156);
  value.write(type, 156, 1, 'ascii');
  value.write('ustar\0', 257, 6, 'ascii');
  value.write('00', 263, 2, 'ascii');
  octal(value, 148, 8, [...value].reduce((sum, byte) => sum + byte, 0));
  return value;
}

const blocks = [];
for (const [name, source, mode, type] of entries) {
  const data = source ? fs.readFileSync(path.join(root, source)) : Buffer.alloc(0);
  blocks.push(header(name, data.length, mode, type), data);
  if (data.length % 512) blocks.push(Buffer.alloc(512 - data.length % 512));
}
blocks.push(Buffer.alloc(1024));
fs.writeFileSync(path.join(root, 'release', 'RemotePlayServer-macos-arm64.tar.gz'), zlib.gzipSync(Buffer.concat(blocks), { level: 9 }));
