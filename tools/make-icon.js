// 打包应用图标 src/app.ico
// 各尺寸 PNG 由无头 Chrome 从 Mask group.svg 栅格化（透明背景），本脚本只负责 ICO 容器。
// ICO 条目直接内嵌 PNG（Windows Vista+ 均支持，任务栏/资源管理器渲染正常）。
const fs = require('fs');
const path = require('path');

const DIR = path.join(__dirname, '..', 'build', 'icon');
const OUT = path.join(__dirname, '..', 'src', 'app.ico');
const SIZES = [16, 24, 32, 48, 64, 128, 256];

const pngs = SIZES.map(n => {
  const p = path.join(DIR, `icon-${n}.png`);
  const buf = fs.readFileSync(p);
  if (buf.readUInt32BE(0) !== 0x89504E47) throw new Error(`${p} 不是 PNG`);
  return buf;
});

const header = Buffer.alloc(6);
header.writeUInt16LE(0, 0);
header.writeUInt16LE(1, 2);          // ICO
header.writeUInt16LE(pngs.length, 4);

let offset = 6 + 16 * pngs.length;
const entries = pngs.map((img, i) => {
  const size = SIZES[i];
  const e = Buffer.alloc(16);
  e[0] = size >= 256 ? 0 : size;     // 256 写 0
  e[1] = size >= 256 ? 0 : size;
  e[2] = 0; e[3] = 0;                // 无调色板
  e.writeUInt16LE(1, 4);             // 图标
  e.writeUInt16LE(32, 6);            // 32bpp
  e.writeUInt32LE(img.length, 8);
  e.writeUInt32LE(offset, 12);
  offset += img.length;
  return e;
});

fs.writeFileSync(OUT, Buffer.concat([header, ...entries, ...pngs]));
fs.copyFileSync(path.join(DIR, 'icon-256.png'), path.join(__dirname, '..', 'build', 'icon-preview.png'));
console.log('icon written:', OUT, fs.statSync(OUT).size, 'bytes,', SIZES.length, 'sizes');
