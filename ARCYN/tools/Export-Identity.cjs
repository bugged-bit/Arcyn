// Run with Node.js and sharp available on NODE_PATH; see CONTRIBUTING.md.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const sharp = require('sharp');
const root = path.resolve(__dirname, '../..');
const images = path.join(root, 'docs/images');
const assets = path.join(root, 'ARCYN/ARCYN.UI/Assets');
const sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
const icon = fs.readFileSync(path.join(assets, 'ARCYN.svg'), 'utf8');
const drawing = size => icon.replace('href="#icon-64"', 'href="#icon-' + (size <= 20 ? 16 : size <= 24 ? 24 : size <= 32 ? 32 : 64) + '"');
const svgData = data => `data:image/svg+xml;base64,${Buffer.from(data).toString('base64')}`;
async function exportIdentity() {
  for (const size of [16, 24, 32, 64]) assert.ok(icon.includes(`id="icon-${size}"`), `Missing ${size}px vector drawing`);
  const frames = await Promise.all(sizes.map(size => sharp(Buffer.from(drawing(size))).resize(size, size).png().toBuffer()));
  for (const size of [16, 24, 32]) assert.notDeepEqual(frames[sizes.indexOf(size)],
    await sharp(Buffer.from(icon)).resize(size, size).png().toBuffer(), `${size}px must use its optical drawing`);
  const directory = Buffer.alloc(6 + 16 * sizes.length);
  directory.writeUInt16LE(1, 2); directory.writeUInt16LE(sizes.length, 4);
  let offset = directory.length;
  frames.forEach((frame, i) => {
    const entry = 6 + i * 16;
    directory[entry] = directory[entry + 1] = sizes[i] === 256 ? 0 : sizes[i];
    directory.writeUInt16LE(1, entry + 4); directory.writeUInt16LE(32, entry + 6);
    directory.writeUInt32LE(frame.length, entry + 8); directory.writeUInt32LE(offset, entry + 12);
    offset += frame.length;
  });
  fs.writeFileSync(path.join(assets, 'ARCYN.ico'), Buffer.concat([directory, ...frames]));
  fs.writeFileSync(path.join(images, 'arcyn.svg'), icon);
  fs.writeFileSync(path.join(images, 'arcyn-icon.png'), frames.at(-1));
  const rows = ['#F7F7F5', '#15171C'].map((background, row) => {
    const y = row * 330, ink = row ? '#E9E9E6' : '#202229';
    return `<rect y="${y}" width="860" height="330" fill="${background}"/><g fill="${ink}" font-family="Segoe UI, sans-serif" font-size="14">${[16,24,32,48,256].map((size,i)=>`<text x="${[52,142,240,350,650][i]}" y="${y+40}" text-anchor="middle">${size} px</text><image x="${[52,142,240,350,650][i]-size/2}" y="${y+60+(256-size)/2}" width="${size}" height="${size}" href="data:image/png;base64,${frames[sizes.indexOf(size)].toString('base64')}"/>`).join('')}</g>`;
  }).join('');
  await sharp(Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="860" height="660">${rows}</svg>`)).png().toFile(path.join(images, 'icon-sizes.png'));
  const source = fs.readFileSync(path.join(images, 'social-preview.svg'), 'utf8');
  const embedded = source.replace(/xlink:href="(arcyn\.svg|social-workspaces\.png)"/g, (_, name) => {
    const bytes = fs.readFileSync(path.join(images, name));
    return `xlink:href="${name.endsWith('.svg') ? svgData(bytes) : 'data:image/png;base64,' + bytes.toString('base64')}"`;
  });
  assert.ok(!/xlink:href="(?!data:)/.test(embedded), 'Social preview contains an unresolved image');
  const social = await sharp(Buffer.from(embedded)).png().toBuffer();
  const metadata = await sharp(social).metadata();
  assert.equal(metadata.width, 1280); assert.equal(metadata.height, 640);
  assert.ok(social.length < 1000000, 'Social preview must remain below 1 MB');
  fs.writeFileSync(path.join(images, 'social-preview.png'), social);
  console.log(`Exported ${sizes.join(', ')} px ICO, repository icon, size proof, and 1280 × 640 social preview (${social.length} bytes).`);
}
exportIdentity().catch(error => { console.error(error); process.exitCode = 1; });
