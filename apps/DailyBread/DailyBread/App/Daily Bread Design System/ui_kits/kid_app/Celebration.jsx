// Celebration.jsx — three-tier celebration ladder (IMPLEMENTATION.md §1.3, tier 3)
// Canvas particle system: gravity + drag, varied geometry, depth via scale/blur/parallax,
// theme tint + money-gold, radial bloom, rising chime.
// Exports to window: CelebrationLayer, CELEB_PRESETS, celebSwiftConfig
// Imperative API installed on window.DBCelebrate: { perfect(), threshold(from,toEl), pop(point) }
//
// Perf notes (learned the hard way — do not undo):
//   * Canvas2D applies ctx.filter PER DRAW CALL, so blurring 1200 particles individually
//     costs ~1 fps. Each depth band renders unblurred into its own offscreen canvas, then
//     the three bands are composited with one filtered draw each: 3 blurred draws/frame.
//   * Particles come from a fixed pre-allocated pool. Allocating 1200 objects inside the
//     fire handler cost a 320ms frame on its own.

const MAX_PARTICLES = 2600;

const MIX = {
  balanced:  { coin: 3, ribbon: 3, star: 2, spark: 2 },
  coinHeavy: { coin: 6, ribbon: 2, star: 1, spark: 1 },
  ribbons:   { coin: 1, ribbon: 7, star: 1, spark: 1 },
  sparks:    { coin: 1, ribbon: 1, star: 1, spark: 7 }
};

const CELEB_PRESETS = {
  full:   { count: 1200, duration: 2500, gravity: 1150, drag: 0.55, burstVelocity: 900, spin: 7,
            emitter: 'both', mix: 'balanced', depthBlur: 3.5, bloom: 0.9, wobble: 34, chime: true },
  modest: { count: 260,  duration: 1400, gravity: 1300, drag: 0.8,  burstVelocity: 520, spin: 5,
            emitter: 'rain', mix: 'coinHeavy', depthBlur: 1.5, bloom: 0.35, wobble: 18, chime: false },
  off:    { count: 0,    duration: 0,    gravity: 0,    drag: 0,    burstVelocity: 0,   spin: 0,
            emitter: 'rain', mix: 'balanced', depthBlur: 0, bloom: 0, wobble: 0, chime: false }
};

function celebSwiftConfig(t) {
  const f = (n) => (Math.round(n * 100) / 100).toFixed(2);
  return `// Paste into DailyBreadKit — drives the tier-3 particle system.
struct CelebrationConfig {
    var particleCount: Int      = ${t.count}
    var duration: TimeInterval  = ${f(t.duration / 1000)}
    var gravity: CGFloat        = ${f(t.gravity)}     // points/s²
    var drag: CGFloat           = ${f(t.drag)}     // velocity damping per second
    var burstVelocity: CGFloat  = ${f(t.burstVelocity)}     // radial impulse from the bloom origin
    var spin: CGFloat           = ${f(t.spin)}     // radians/s, randomised ±40%
    var emitter: Emitter        = .${t.emitter}
    var geometryMix: Mix        = .${t.mix}
    var depthBlur: CGFloat      = ${f(t.depthBlur)}     // max blur on the farthest layer
    var bloomStrength: CGFloat  = ${f(t.bloom)}
    var wobble: CGFloat         = ${f(t.wobble)}     // lateral drift amplitude
    var playsChime: Bool        = ${t.chime}

    // Invariant: money-gold always participates, whatever the theme.
    static let goldDeep  = Color(hex: "#C98A1E")
    static let goldLight = Color(hex: "#E7B44A")
}
// Render depth bands into three separate layers and blur each layer once — never per particle.
// Respect Reduce Motion regardless of this config, and honour enableConfetti.`;
}

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const rand = (a, b) => a + Math.random() * (b - a);
function readVar(name, fb) {
  const v = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return v || fb;
}
function pickWeighted(mix) {
  const keys = Object.keys(mix);
  let total = 0; for (const k of keys) total += mix[k];
  let r = Math.random() * total;
  for (const k of keys) { r -= mix[k]; if (r <= 0) return k; }
  return keys[0];
}

function chime(rising) {
  try {
    const Ctx = window.AudioContext || window.webkitAudioContext; if (!Ctx) return;
    const ac = new Ctx();
    const notes = rising ? [523.25, 659.25, 783.99, 1046.5] : [783.99, 1046.5];
    notes.forEach((f, i) => {
      const o = ac.createOscillator(), g = ac.createGain();
      o.type = 'triangle'; o.frequency.value = f;
      const t0 = ac.currentTime + i * 0.085;
      g.gain.setValueAtTime(0, t0);
      g.gain.linearRampToValueAtTime(0.16, t0 + 0.012);
      g.gain.exponentialRampToValueAtTime(0.0001, t0 + 0.55);
      o.connect(g); g.connect(ac.destination); o.start(t0); o.stop(t0 + 0.6);
    });
    setTimeout(() => ac.close(), 1400);
  } catch (e) { /* audio is a bonus, never a failure */ }
}

function pulse(el) {
  el.animate([{ transform: 'scale(1)' }, { transform: 'scale(1.28)' }, { transform: 'scale(1)' }],
    { duration: 460, easing: 'cubic-bezier(.2,.9,.2,1)' });
}

const BANDS = [[0, 0.34], [0.34, 0.68], [0.68, 1.01]];

function CelebrationLayer({ cfg }) {
  const canvasRef = React.useRef(null);
  const cfgRef = React.useRef(cfg);
  cfgRef.current = cfg;

  React.useEffect(() => {
    const canvas = canvasRef.current, ctx = canvas.getContext('2d');
    let w = 0, h = 0;
    const dpr = Math.min(window.devicePixelRatio || 1, 2);

    // fixed particle pool — never grows, never allocates during a burst
    const pool = new Array(MAX_PARTICLES);
    for (let i = 0; i < MAX_PARTICLES; i++) {
      pool[i] = { alive: false, x: 0, y: 0, vx: 0, vy: 0, depth: 0, shape: 'coin', size: 6,
        r: 0, vr: 0, col: '#E7B44A', wob: 0, wobf: 1, life: 1000, age: 0 };
    }
    let cursor = 0, alive = 0;
    const blooms = [], rings = [], coins = [];
    let raf = 0, last = 0;

    // one offscreen canvas per depth band
    const bandCanvas = BANDS.map(() => document.createElement('canvas'));
    const bandCtx = bandCanvas.map((c) => c.getContext('2d'));

    const resize = () => {
      const r = canvas.parentElement.getBoundingClientRect();
      w = Math.max(1, r.width); h = Math.max(1, r.height);
      canvas.width = Math.round(w * dpr); canvas.height = Math.round(h * dpr);
      canvas.style.width = w + 'px'; canvas.style.height = h + 'px';
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      bandCanvas.forEach((c, i) => {
        c.width = canvas.width; c.height = canvas.height;
        bandCtx[i].setTransform(dpr, 0, 0, dpr, 0, 0);
      });
    };
    resize();
    const ro = new ResizeObserver(resize); ro.observe(canvas.parentElement);

    const palette = () => {
      const t = cfgRef.current;
      const gold = [readVar('--ds-gold', '#E7B44A'), '#C98A1E', '#F0C868'];
      if (!t.themeTint) return gold;
      return gold.concat([readVar('--ds-accent', '#3B82D6'), readVar('--ds-success', '#2E9E63'),
        readVar('--db-level-4', '#7A5AF8')]);
    };

    // claim a slot from the pool; oldest slot wins if the pool is saturated
    const claim = () => {
      const p = pool[cursor];
      cursor = (cursor + 1) % MAX_PARTICLES;
      if (!p.alive) alive++;
      p.alive = true;
      return p;
    };

    const emitSpark = (x, y, col, vx, vy, size, life) => {
      const p = claim();
      p.x = x; p.y = y; p.vx = vx; p.vy = vy; p.depth = 0.88; p.shape = 'spark';
      p.size = size; p.r = 0; p.vr = 0; p.col = col; p.wob = 0; p.wobf = 1;
      p.life = life; p.age = 0;
    };

    const spawn = (n, origin) => {
      const t = cfgRef.current, mix = MIX[t.mix] || MIX.balanced, cols = palette();
      const capped = Math.min(n, MAX_PARTICLES);
      for (let i = 0; i < capped; i++) {
        const p = claim();
        p.shape = pickWeighted(mix);
        const fromBurst = origin && (t.emitter === 'burst' || (t.emitter === 'both' && Math.random() < 0.55));
        if (fromBurst) {
          const a = rand(0, Math.PI * 2), sp = t.burstVelocity * rand(0.35, 1);
          p.x = origin.x + rand(-8, 8); p.y = origin.y + rand(-8, 8);
          p.vx = Math.cos(a) * sp; p.vy = Math.sin(a) * sp - t.burstVelocity * 0.35;
        } else {
          p.x = rand(-0.05, 1.05) * w; p.y = rand(-0.35, -0.02) * h;
          p.vx = rand(-70, 70); p.vy = rand(40, 220);
        }
        p.depth = Math.random();
        p.size = (p.shape === 'ribbon' ? rand(5, 9) : rand(4.5, 9)) * (0.55 + p.depth * 0.95);
        p.r = rand(0, Math.PI * 2); p.vr = rand(-1, 1) * t.spin * rand(0.6, 1.4);
        p.col = cols[(Math.random() * cols.length) | 0];
        p.wob = rand(0, Math.PI * 2); p.wobf = rand(0.8, 2.4);
        p.life = t.duration * rand(0.7, 1.15); p.age = 0;
      }
    };

    const drawShape = (c, p) => {
      const s = p.size;
      c.fillStyle = p.col;
      if (p.shape === 'coin') {
        const sx = Math.max(0.12, Math.abs(Math.cos(p.r)));
        c.save(); c.scale(sx, 1);
        c.beginPath(); c.arc(0, 0, s, 0, Math.PI * 2); c.fill();
        c.globalAlpha *= 0.45; c.fillStyle = '#FFF6DC';
        c.beginPath(); c.arc(-s * 0.22, -s * 0.24, s * 0.42, 0, Math.PI * 2); c.fill();
        c.restore();
      } else if (p.shape === 'ribbon') {
        const sx = Math.max(0.1, Math.abs(Math.cos(p.r * 0.7)));
        c.save(); c.scale(sx, 1);
        const rw = s * 0.62, rh = s * 2.6;
        c.beginPath(); c.roundRect(-rw / 2, -rh / 2, rw, rh, rw * 0.5); c.fill();
        c.restore();
      } else if (p.shape === 'star') {
        c.beginPath();
        for (let i = 0; i < 10; i++) {
          const rr = i % 2 ? s * 0.46 : s * 1.05, a = (i / 10) * Math.PI * 2 - Math.PI / 2;
          const px = Math.cos(a) * rr, py = Math.sin(a) * rr;
          i ? c.lineTo(px, py) : c.moveTo(px, py);
        }
        c.closePath(); c.fill();
      } else {
        c.beginPath(); c.arc(0, 0, s * 1.1, 0, Math.PI * 2); c.fill();
      }
    };

    const tick = (now) => {
      const t = cfgRef.current;
      const dt = Math.min(0.05, (now - (last || now)) / 1000); last = now;
      ctx.clearRect(0, 0, w, h);

      // radial bloom behind everything
      for (let i = blooms.length - 1; i >= 0; i--) {
        const b = blooms[i]; b.age += dt;
        const k = b.age / b.life;
        if (k >= 1) { blooms.splice(i, 1); continue; }
        const rr = (0.15 + k * 0.85) * Math.max(w, h) * 0.85;
        const g = ctx.createRadialGradient(b.x, b.y, 0, b.x, b.y, rr);
        const a = (1 - k) * 0.5 * t.bloom;
        g.addColorStop(0, `rgba(255,232,168,${a})`);
        g.addColorStop(0.55, `rgba(255,206,102,${a * 0.35})`);
        g.addColorStop(1, 'rgba(255,206,102,0)');
        ctx.fillStyle = g; ctx.fillRect(0, 0, w, h);
      }

      if (alive) {
        // 1. draw each band unblurred into its own layer
        for (let bi = 0; bi < BANDS.length; bi++) bandCtx[bi].clearRect(0, 0, w, h);
        for (let i = 0; i < MAX_PARTICLES; i++) {
          const p = pool[i]; if (!p.alive) continue;
          const bi = p.depth < 0.34 ? 0 : p.depth < 0.68 ? 1 : 2;
          const c = bandCtx[bi];
          c.save();
          c.globalAlpha = clamp(0.45 + p.depth * 0.55, 0, 1) * clamp(1 - p.age / p.life, 0, 1) ** 0.6;
          c.translate(p.x, p.y); c.rotate(p.r);
          drawShape(c, p);
          c.restore();
        }
        // 2. composite the three layers — one filtered draw each, far to near
        for (let bi = 0; bi < BANDS.length; bi++) {
          const blur = t.depthBlur * (1 - bi / 2);
          ctx.filter = blur > 0.05 ? `blur(${blur.toFixed(2)}px)` : 'none';
          ctx.drawImage(bandCanvas[bi], 0, 0, w, h);
        }
        ctx.filter = 'none';
      }

      // integrate
      for (let i = 0; i < MAX_PARTICLES; i++) {
        const p = pool[i]; if (!p.alive) continue;
        p.age += dt * 1000;
        const par = 0.55 + p.depth * 0.75;                 // parallax by depth
        p.wob += dt * p.wobf;
        p.vy += t.gravity * par * dt;
        p.vx += Math.sin(p.wob) * t.wobble * dt;
        const d = Math.exp(-t.drag * dt);
        p.vx *= d; p.vy *= d;
        p.x += p.vx * dt; p.y += p.vy * dt; p.r += p.vr * dt;
        if (p.age > p.life || p.y > h + 80) { p.alive = false; alive--; }
      }

      // tier-1 rings
      for (let i = rings.length - 1; i >= 0; i--) {
        const g2 = rings[i]; g2.age += dt;
        const k = g2.age / g2.life;
        if (k >= 1) { rings.splice(i, 1); continue; }
        ctx.save();
        ctx.globalAlpha = (1 - k) * 0.9;
        ctx.strokeStyle = g2.col; ctx.lineWidth = 2.5 * (1 - k * 0.6);
        ctx.beginPath(); ctx.arc(g2.x, g2.y, 14 + k * 22, 0, Math.PI * 2); ctx.stroke();
        ctx.restore();
      }

      // tier-2 coin arcs
      for (let i = coins.length - 1; i >= 0; i--) {
        const c = coins[i]; c.age += dt;
        const k = clamp(c.age / c.life, 0, 1);
        const e = 1 - Math.pow(1 - k, 2.2);
        const mx = (c.x0 + c.x1) / 2, my = Math.min(c.y0, c.y1) - 130;
        const x = (1 - e) ** 2 * c.x0 + 2 * (1 - e) * e * mx + e * e * c.x1;
        const y = (1 - e) ** 2 * c.y0 + 2 * (1 - e) * e * my + e * e * c.y1;
        const gold = readVar('--ds-gold', '#E7B44A');
        ctx.save();
        ctx.globalAlpha = k > 0.88 ? (1 - k) / 0.12 : 1;
        ctx.translate(x, y); ctx.rotate(c.age * 14);
        drawShape(ctx, { shape: 'coin', size: 11, r: c.age * 14, col: gold });
        ctx.restore();
        if (k >= 1) {
          coins.splice(i, 1);
          if (c.onArrive) c.onArrive();
          for (let j = 0; j < 10; j++) {
            emitSpark(c.x1, c.y1, gold, rand(-160, 160), rand(-200, 40), rand(2.5, 4.5), 520);
          }
        }
      }

      const busy = alive || blooms.length || rings.length || coins.length;
      raf = busy ? requestAnimationFrame(tick) : 0;
    };
    const start = () => { if (!raf) { last = 0; raf = requestAnimationFrame(tick); } };

    const reduced = () => cfgRef.current.reduceMotion ||
      window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    window.DBCelebrate = {
      perfect() {
        const t = cfgRef.current; if (t.tier === 'off') return;
        if (reduced()) { blooms.push({ x: w / 2, y: h * 0.42, age: 0, life: 0.7 }); start(); return; }
        blooms.push({ x: w / 2, y: h * 0.42, age: 0, life: t.duration / 1000 * 0.45 });
        spawn(t.count, { x: w / 2, y: h * 0.42 });
        if (t.chime) chime(true);
        start();
      },
      threshold(from, toEl) {
        const t = cfgRef.current; if (t.tier === 'off') return;
        const box = canvas.parentElement.getBoundingClientRect();
        const target = toEl && toEl.getBoundingClientRect();
        const x1 = target ? target.left + target.width / 2 - box.left : w - 40;
        const y1 = target ? target.top + target.height / 2 - box.top : 40;
        if (reduced()) { if (toEl) pulse(toEl); return; }
        coins.push({ x0: from.x, y0: from.y, x1, y1, age: 0, life: 0.62,
          onArrive: () => { if (toEl) pulse(toEl); } });
        if (t.chime) chime(false);
        start();
      },
      pop(pt, gold) {
        const t = cfgRef.current; if (t.tier === 'off' || reduced()) return;
        const col = gold ? readVar('--ds-gold', '#E7B44A') : readVar('--ds-accent', '#3B82D6');
        rings.push({ x: pt.x, y: pt.y, age: 0, life: 0.34, col });
        if (t.tier === 'full') {
          for (let i = 0; i < 8; i++) {
            const a = rand(0, Math.PI * 2);
            emitSpark(pt.x, pt.y, col, Math.cos(a) * rand(60, 190),
              Math.sin(a) * rand(60, 190) - 60, rand(2, 3.6), 460);
          }
        }
        start();
      },
      // exposed for perf measurement
      _stats() { return { alive, pool: MAX_PARTICLES }; }
    };

    return () => {
      ro.disconnect(); cancelAnimationFrame(raf); raf = 0; delete window.DBCelebrate;
    };
  }, []);

  return <canvas ref={canvasRef} style={{ position: 'absolute', inset: 0, pointerEvents: 'none', zIndex: 400 }} />;
}

Object.assign(window, { CelebrationLayer, CELEB_PRESETS, celebSwiftConfig, DB_CELEB_MIX: MIX });
