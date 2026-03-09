const QRCode = require('qrcode');
const fs = require('fs');

const serverUrl = process.argv[2] || 'http://localhost:8080';
const setupToken = process.argv[3] || '';
const outputPath = process.argv[4] || 'scripts/test-setup-qr.png';

const payload = JSON.stringify({
  type: 'openksef-setup',
  version: 1,
  serverUrl,
  ...(setupToken ? { setupToken } : {}),
});

console.log(`Payload length: ${payload.length}`);
console.log(`Payload preview: ${payload.substring(0, 80)}...`);

QRCode.toFile(outputPath, payload, { errorCorrectionLevel: 'M', width: 400 }, (err) => {
  if (err) {
    console.error('Error:', err);
    process.exit(1);
  }
  const stats = fs.statSync(outputPath);
  console.log(`QR code saved to: ${outputPath} (${stats.size} bytes)`);
});
