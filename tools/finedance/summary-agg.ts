import fs from 'node:fs';
const L = fs.readFileSync(process.argv[2], 'utf8').split(String.fromCharCode(10)).map(s => s.split(String.fromCharCode(13)).join('')).filter(s => s.length); const h = L[0].split(',');
const R = L.slice(1).map(l => { const c = l.split(','); const o: any = {}; h.forEach((k, i) => o[k] = isNaN(+c[i]) ? c[i] : +c[i]); return o; });
const q = (a: number[], p: number) => { const s = [...a].sort((x, y) => x - y); return s[Math.min(s.length - 1, Math.floor(p * s.length))]; };
const f = (x: number, d = 2) => x.toFixed(d);
console.log('style1 x style2 (файлов, мин, ср. с, ср. скорость конечностей м/с, p95 скорости, доля низких кадров)');
const g: Record<string, any[]> = {};
R.forEach(r => (g[`${r.style1}/${r.style2}`] ??= []).push(r));
for (const [k, v] of Object.entries(g).sort((a, b) => b[1].length - a[1].length)) {
  const sec = v.map(r => r.sec), sm = v.map(r => r.speed_mean);
  console.log(k.padEnd(22), String(v.length).padStart(3), f(sec.reduce((a, b) => a + b, 0) / 60, 0).padStart(4), f(sec.reduce((a, b) => a + b, 0) / v.length, 0).padStart(4), f(sm.reduce((a, b) => a + b, 0) / v.length).padStart(5), f(v.reduce((a, r) => a + r.speed_p95, 0) / v.length).padStart(5), f(v.reduce((a, r) => a + r.low_frac, 0) / v.length, 3));
}
const sm = R.map(r => r.speed_mean);
console.log('\nскорость конечностей, средняя по файлу: p10/25/50/75/90', [.1,.25,.5,.75,.9].map(p => f(q(sm, p))).join(' '));
const tert = [q(sm, 1/3), q(sm, 2/3)];
console.log('терцили', tert.map(x => f(x)));
const lvl = (r: any) => r.speed_mean < tert[0] ? 'низкая' : r.speed_mean < tert[1] ? 'средняя' : 'высокая';
console.log('\nстиль1 по терцилям интенсивности (низкая/средняя/высокая):');
for (const s of [...new Set(R.map(r => r.style1))]) { const v = R.filter(r => r.style1 === s); console.log(s.padEnd(8), ['низкая','средняя','высокая'].map(t => v.filter(r => lvl(r) === t).length).join('/')); }
console.log('\nдлительность, с: p10/50/90', [.1,.5,.9].map(p => q(R.map(r => r.sec), p)).join('/'), ' коротких <60 с:', R.filter(r => r.sec < 60).length, ' 60-120:', R.filter(r => r.sec >= 60 && r.sec < 120).length, ' 120-240:', R.filter(r => r.sec >= 120 && r.sec < 240).length, ' >=240:', R.filter(r => r.sec >= 240).length);
console.log('работа на полу (low_frac>0.1):', R.filter(r => r.low_frac > 0.1).map(r => `${r.id}:${r.style1}`).join(' '));
console.log('перемещение, м/мин: p10/50/90', [.1,.5,.9].map(p => f(q(R.map(r => r.travel_per_min), p), 1)).join('/'));
console.log('топ-5 интенсивных:', [...R].sort((a, b) => b.speed_mean - a.speed_mean).slice(0, 5).map(r => `${r.id} ${r.song} ${r.style1}/${r.style2} ${f(r.speed_mean)}`).join('; '));
console.log('топ-5 спокойных:', [...R].sort((a, b) => a.speed_mean - b.speed_mean).slice(0, 5).map(r => `${r.id} ${r.song} ${r.style1}/${r.style2} ${f(r.speed_mean)}`).join('; '));
const corr = (a: number[], b: number[]) => { const ma = a.reduce((x,y)=>x+y)/a.length, mb = b.reduce((x,y)=>x+y)/b.length; let n=0,da=0,db=0; a.forEach((x,i)=>{n+=(x-ma)*(b[i]-mb);da+=(x-ma)**2;db+=(b[i]-mb)**2}); return n/Math.sqrt(da*db); };
console.log('корреляция скорость/длительность', f(corr(sm, R.map(r => r.sec))));
