const { GlassCard, ProgressRing, Tag, ChoreRow, Icon } = window.DailyBreadDesignSystem_2a4964;

/* Rainbow-year maths — IMPLEMENTATION.md §1.2.
   Hue comes from the REAL day-of-year, never from position within the window,
   so a 4-week widget shows a 4-week slice of the wheel and a March day is a
   March colour on every surface. */
const TODAY_DOY = 205;
const dayHue = doy => ((doy % 365) / 365) * 360;
const rnd = n => ((n * 2654435761) % 1000) / 1000;

/* ---------- Expressive config (driven by the Tweaks panel) ----------
   Three controls that change what the widget set IS, not how big its bits are:
   reading   = how much year you show vs how countable a day is
   shortfall = what a bad day looks like (the emotional message of the whole wall)
   perfect   = how hard a perfect day celebrates on the wall
*/
const WIDGET_MODES = {
  ambient: { counts: false,
    small: { weeks: 8, cell: 7.5 }, medium: { weeks: 26, cell: 9.5 },
    large: { weeks: 52, cell: 4.8, bands: 1 }, parentLarge: { weeks: 26, cell: 7.5 },
    standby: { weeks: 26, cell: 8.5 } },
  balanced: { counts: true,
    small: { weeks: 4, cell: 12 }, medium: { weeks: 12, cell: 15 },
    large: { weeks: 26, cell: 10.5, bands: 2 }, parentLarge: { weeks: 12, cell: 10 },
    standby: { weeks: 26, cell: 8.5 } },
  countable: { counts: true,
    small: { weeks: 3, cell: 13 }, medium: { weeks: 8, cell: 16 },
    large: { weeks: 13, cell: 12, bands: 2 }, parentLarge: { weeks: 8, cell: 12 },
    standby: { weeks: 17, cell: 9.5 } }
};
const CURVES = {
  forgiving: { e: 1.0, sat: 34 },   // a shortfall still looks like a colour you'd keep
  honest:    { e: 1.6, sat: 18 },   // §1.2 as specced
  stark:     { e: 2.6, sat: 8 }     // only near-perfect days carry real colour
};
const DEFAULT_WCFG = { reading: 'balanced', shortfall: 'honest', perfect: 'glow' };
const wcfg = () => Object.assign({}, DEFAULT_WCFG, window.DB_WIDGET_CFG || {});
const wmode = () => WIDGET_MODES[wcfg().reading] || WIDGET_MODES.balanced;

/** Deterministic stand-in for real data: required-ratio + optional bloom. */
function dayState(doy) {
  const v = rnd(doy + 11);
  if (doy > TODAY_DOY) return { ratio: -1, bloom: false };
  if (v < 0.13) return { ratio: 0, bloom: false };
  if (v < 0.34) return { ratio: 0.45, bloom: false };
  if (v < 0.62) return { ratio: 0.78, bloom: false };
  if (v < 0.86) return { ratio: 1, bloom: false };
  return { ratio: 1, bloom: true };
}

/** One cell. Intensity ramp is non-linear (§1.2) so a shortfall reads as one. */
function cellStyle(doy, dark, cell) {
  const cfg = wcfg();
  const cv = CURVES[cfg.shortfall] || CURVES.honest;
  const { ratio, bloom } = dayState(doy);
  const hue = dayHue(doy);
  if (ratio < 0) return { background: 'transparent', boxShadow: 'inset 0 0 0 1px rgb(128 128 128 / .16)' };
  if (ratio === 0) return { background: `hsl(${hue} ${Math.max(6, cv.sat * 0.6)}% ${dark ? 20 : 88}%)` };
  const e = Math.pow(ratio, cv.e);
  const s = { background: `hsl(${hue} ${cv.sat + e * (86 - cv.sat)}% ${dark ? 16 + e * 40 : 84 - e * 40}%)` };
  // bloom scales with the cell or it smears into its neighbours on the year wall
  if (bloom && cfg.perfect !== 'flat') {
    s.boxShadow = cfg.perfect === 'ember'
      ? `0 0 ${(cell * 1.15).toFixed(1)}px hsl(${hue} 96% 58% / .85), inset 0 0 0 1px hsl(45 100% 80% / .9)`
      : `0 0 ${Math.max(2, cell * 0.55).toFixed(1)}px hsl(${hue} 90% 55% / .7)`;
  }
  return s;
}

/** weeks of real days ending today. Columns are weeks, rows are weekdays. */
// A day cell MUST be square — the whole read (embers, a streak as a burning line)
// depends on it. So the caller passes an explicit cell edge and the grid sizes to
// content; never `flex: 1`, which stretches the two axes independently and turns
// the calendar into bars or stripes.
function RainbowGrid({ weeks, dark, cell = 9, gap = 2, radius, endDoy = TODAY_DOY, style }) {
  const start = endDoy - weeks * 7 + 1;
  const r0 = radius != null ? radius : Math.max(1, +(cell * 0.28).toFixed(1));
  const cells = [];
  for (let c = 0; c < weeks; c++) {
    for (let r = 0; r < 7; r++) {
      const doy = start + c * 7 + r;
      cells.push(<div key={`${c}-${r}`} style={{
        width: cell, height: cell, borderRadius: r0, ...cellStyle(doy, dark, cell)
      }} />);
    }
  }
  return (
    <div style={{
      display: 'grid', gridTemplateColumns: `repeat(${weeks}, ${cell}px)`,
      gridTemplateRows: `repeat(7, ${cell}px)`, gridAutoFlow: 'column', gap, width: 'max-content', ...style
    }}>{cells}</div>
  );
}

const Label = ({ children, dark }) => (
  <div style={{
    font: '700 9.5px var(--ds-font)', letterSpacing: '.9px', textTransform: 'uppercase',
    color: dark ? 'rgb(255 255 255 / .55)' : 'var(--ds-label-3)'
  }}>{children}</div>
);

const Money = ({ children, size = 13 }) => (
  <span style={{ font: `800 ${size}px var(--ds-font)`, color: 'var(--ds-gold)' }}>{children}</span>
);

/** Widget-scale chore row (~27px). The app's ChoreRow is 46px and cannot fit three
    rows plus a header and footer in a 158px medium tile — don't reuse it here. */
const WidgetChore = ({ icon, name, done }) => (
  <div style={{ display: 'flex', alignItems: 'center', gap: 8, height: 27, minWidth: 0 }}>
    <span style={{ fontFamily: 'var(--ds-font-emoji)', fontSize: 14, flex: '0 0 auto' }}>{icon}</span>
    <span style={{
      font: '600 12px var(--ds-font)', flex: 1, minWidth: 0, whiteSpace: 'nowrap',
      overflow: 'hidden', textOverflow: 'ellipsis',
      color: done ? 'var(--ds-label-3)' : 'var(--ds-label)',
      textDecoration: done ? 'line-through' : 'none'
    }}>{name}</span>
    <span style={{
      width: 15, height: 15, borderRadius: '50%', flex: '0 0 auto',
      display: 'grid', placeItems: 'center',
      background: done ? 'var(--ds-accent)' : 'transparent',
      boxShadow: done ? 'none' : 'inset 0 0 0 1.5px var(--ds-label-3)'
    }}>{done ? <span style={{ font: '800 9px var(--ds-font)', color: 'var(--ds-on-accent)' }}>✓</span> : null}</span>
  </div>
);

/** The widget tile itself — GlassCard gives us the real card surface + stroke. */
const Tile = ({ w, h, children, pad = 15 }) => (
  <GlassCard padding={pad} style={{
    width: w, height: h, borderRadius: 22, display: 'flex', flexDirection: 'column',
    overflow: 'hidden', boxShadow: '0 6px 20px rgb(0 0 0 / .22)'
  }}>{children}</GlassCard>
);

/* ---------- Home screen, kid ---------- */

/** §1.2a Small — last 4 weeks + today's ring. */
function SmallKid({ done, total }) {
  const m = wmode();
  return (
    <Tile w={158} h={158}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
        <Label>Today</Label>
        {m.counts ? <Money size={12}>£4.50</Money> : null}
      </div>
      <div style={{ flex: 1, display: 'flex', alignItems: 'center', gap: 10 }}>
        <ProgressRing progress={total ? done / total : 0} size={48} stroke={6}
          label={m.counts ? `${done}/${total}` : undefined} />
        <RainbowGrid weeks={m.small.weeks} cell={m.small.cell} gap={2} />
      </div>
    </Tile>
  );
}

/** §1.2a Medium (Year) — last 12 weeks, matching the Today card exactly. */
function MediumYear({ done, total }) {
  const m = wmode();
  // Ambient drops the numbers entirely and gives the whole tile to the wall — a stats
  // column cannot survive 26 columns of grid beside it, and shouldn't try to.
  if (!m.counts) {
    return (
      <Tile w={338} h={158}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
          <Label>Last {m.medium.weeks} weeks</Label>
          <span style={{ font: '650 11px var(--ds-font)', color: 'var(--ds-label-2)' }}>9 day streak</span>
        </div>
        <div style={{ flex: 1, display: 'grid', placeItems: 'center' }}>
          <RainbowGrid weeks={m.medium.weeks} cell={m.medium.cell} gap={2} />
        </div>
      </Tile>
    );
  }
  return (
    <Tile w={338} h={158}>
      <div style={{ flex: 1, display: 'flex', gap: 14, alignItems: 'stretch' }}>
        <RainbowGrid weeks={m.medium.weeks} cell={m.medium.cell} gap={2} style={{ alignSelf: 'center' }} />
        <div style={{ flex: '1 0 92px', minWidth: 92, display: 'flex', flexDirection: 'column', justifyContent: 'space-between' }}>
          <div>
            <Label>Last {m.medium.weeks} weeks</Label>
            <div style={{ font: '650 11px var(--ds-font)', color: 'var(--ds-label-2)', marginTop: 3 }}>9 day streak</div>
          </div>
          <Money size={19}>£62.40</Money>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <ProgressRing progress={total ? done / total : 0} size={26} stroke={4} />
            <span style={{ font: '650 11.5px var(--ds-font)', color: 'var(--ds-label-2)' }}>{done} of {total}</span>
          </div>
        </div>
      </div>
    </Tile>
  );
}

/** Second medium kind — interactive, App Intents. Not in place of the year; alongside it. */
function MediumToday({ done, total, chores }) {
  return (
    <Tile w={338} h={158} pad={13}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
        <Label>Today</Label>
        <Money size={12}>£4.50</Money>
      </div>
      <div style={{ flex: 1, minHeight: 0, overflow: 'hidden', display: 'flex', flexDirection: 'column',
        justifyContent: 'center', gap: 1, minWidth: 0 }}>
        {chores.slice(0, 3).map((c, i) => (
          <WidgetChore key={c.name} name={c.name} icon={c.icon} done={i < done} />
        ))}
      </div>
      {total > 3 ? (
        <div style={{ font: '600 10.5px var(--ds-font)', color: 'var(--ds-label-3)' }}>
          +{total - 3} more · {done} of {total} done
        </div>
      ) : null}
    </Tile>
  );
}

/** §1.2a Large — the full year. */
function LargeKid({ done, total }) {
  return (
    <Tile w={338} h={338}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: 12 }}>
        <div>
          <Label>This year</Label>
          <div style={{ font: '800 24px var(--ds-font)', color: 'var(--ds-label)', letterSpacing: '-.02em' }}>128 days</div>
        </div>
        <ProgressRing progress={total ? done / total : 0} size={54} stroke={5} label={`${done}/${total}`} />
      </div>
      {/* The year, wrapped into half-year bands — nearly double the cell size of a
          single 52-week strip, which is what makes a streak legible. */}
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', justifyContent: 'center', gap: 9 }}>
        {wmode().large.bands === 1 ? (
          <RainbowGrid weeks={wmode().large.weeks} cell={wmode().large.cell} gap={1} radius={1} />
        ) : (
          <>
            <RainbowGrid weeks={wmode().large.weeks} cell={wmode().large.cell} gap={1.5}
              endDoy={TODAY_DOY - wmode().large.weeks * 7} />
            <RainbowGrid weeks={wmode().large.weeks} cell={wmode().large.cell} gap={1.5} />
          </>
        )}
      </div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', marginTop: 12 }}>
        <div>
          <Label>Balance</Label>
          <div style={{ font: '800 20px var(--ds-font)', color: 'var(--ds-gold)', lineHeight: 1.1 }}>£62.40</div>
        </div>
        <div style={{ textAlign: 'right' }}>
          <Label>Streak</Label>
          <div style={{ font: '800 20px var(--ds-font)', color: 'var(--ds-label)', lineHeight: 1.1 }}>9 days</div>
        </div>
      </div>
    </Tile>
  );
}

/* ---------- Home screen, parent ---------- */

function SmallParent() {
  return (
    <Tile w={158} h={158}>
      <Label>Approvals</Label>
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', justifyContent: 'center' }}>
        <div style={{ font: '800 46px var(--ds-font)', color: 'var(--ds-gold)', lineHeight: 1, letterSpacing: '-.03em' }}>2</div>
        <div style={{ font: '650 11.5px var(--ds-font)', color: 'var(--ds-label-2)', marginTop: 2 }}>waiting on you</div>
      </div>
      <Tag tone="var(--ds-help)" solid>1 HELP</Tag>
    </Tile>
  );
}

function MediumParent({ done, total }) {
  const rows = [
    ['Take the bins out', '£1.20 · due 6pm', 'var(--ds-gold)'],
    ['Practice piano', '20 min screen time', 'var(--ds-label-3)']
  ];
  return (
    <Tile w={338} h={158}>
      <Label>At risk today</Label>
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 9, justifyContent: 'center' }}>
        {rows.map(([n, s, c]) => (
          <div key={n} style={{ display: 'flex', alignItems: 'center', gap: 9 }}>
            <span style={{ width: 8, height: 8, borderRadius: '50%', background: c, flex: '0 0 auto' }} />
            <div style={{ minWidth: 0 }}>
              <div style={{ font: '650 12.5px var(--ds-font)', color: 'var(--ds-label)' }}>{n}</div>
              <div style={{ font: '600 10.5px var(--ds-font)', color: 'var(--ds-label-3)' }}>{s}</div>
            </div>
          </div>
        ))}
      </div>
      <div style={{ font: '650 11.5px var(--ds-font)', color: 'var(--ds-label-2)' }}>{done} of {total} done today</div>
    </Tile>
  );
}

function LargeParent({ done, total }) {
  const m = wmode();
  const week = [1, 0.83, 1, total ? done / total : 0, -1, -1, -1];
  return (
    <Tile w={338} h={338}>
      {/* The hero the small widget already had — a parent opens this to answer
          "is anything waiting on me", so that answer gets the top third. */}
      <Label>Waiting on you</Label>
      <div style={{ display: 'flex', alignItems: 'center', gap: 13, margin: '6px 0 14px' }}>
        <span style={{ font: '800 52px var(--ds-font)', color: 'var(--ds-gold)', lineHeight: .9, letterSpacing: '-.04em' }}>2</span>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6, alignItems: 'flex-start' }}>
          <span style={{ font: '650 13px var(--ds-font)', color: 'var(--ds-label-2)' }}>approvals to review</span>
          <Tag tone="var(--ds-help)" solid>1 HELP</Tag>
        </div>
      </div>
      <div style={{ height: '.5px', background: 'var(--ds-hairline)', marginBottom: 13 }} />
      <Label>This week</Label>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(7, 1fr)', gap: 6, margin: '9px 0 14px' }}>
        {['M', 'T', 'W', 'T', 'F', 'S', 'S'].map((d, i) => (
          <div key={i} style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 5 }}>
            <span style={{ font: '700 10px var(--ds-font)', color: 'var(--ds-label-3)' }}>{d}</span>
            <ProgressRing progress={Math.max(week[i], 0)} size={30} stroke={4}
              color={i === 3 ? 'var(--ds-accent)' : 'var(--ds-secondary)'} />
          </div>
        ))}
      </div>
      <Label>Last {m.parentLarge.weeks} weeks</Label>
      <RainbowGrid weeks={m.parentLarge.weeks} cell={m.parentLarge.cell} gap={2} style={{ margin: '9px 0 0' }} />
      <div style={{ flex: 1 }} />
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 7 }}>
        <Money size={20}>£18.60</Money>
        <span style={{ font: '650 12px var(--ds-font)', color: 'var(--ds-label-2)' }}>earned this week</span>
      </div>
    </Tile>
  );
}

/* ---------- Lock Screen (monochrome — iOS tints these) ---------- */

function AccCircular({ done, total }) {
  const p = total ? done / total : 0;
  return (
    <div style={{ position: 'relative', width: 52, height: 52 }}>
      <div style={{
        position: 'absolute', inset: 0, borderRadius: '50%',
        background: `conic-gradient(#fff ${p}turn, rgb(255 255 255 / .28) ${p}turn)`,
        WebkitMask: 'radial-gradient(circle, transparent 19px, #000 19.5px)',
        mask: 'radial-gradient(circle, transparent 19px, #000 19.5px)'
      }} />
      <div style={{ position: 'absolute', inset: 0, display: 'grid', placeItems: 'center', font: '800 15px var(--ds-font)', color: '#fff' }}>{done}</div>
    </div>
  );
}

function AccRect({ done, total, who }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 2, color: '#fff', minWidth: 150 }}>
      <div style={{ font: '700 10px var(--ds-font)', letterSpacing: '.8px', opacity: .7 }}>DAILY BREAD</div>
      <div style={{ font: '800 16px var(--ds-font)' }}>
        {who === 'kid' ? `${done} of ${total} done` : '2 waiting on you'}
      </div>
      <div style={{ display: 'flex', gap: 3, marginTop: 3 }}>
        {Array.from({ length: total }, (_, i) => (
          <span key={i} style={{ flex: 1, height: 4, borderRadius: 2, background: i < done ? '#fff' : 'rgb(255 255 255 / .3)' }} />
        ))}
      </div>
    </div>
  );
}

function AccInline({ done, total }) {
  const p = total ? done / total : 0;
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 6, color: '#fff', font: '600 13px var(--ds-font)' }}>
      <span style={{
        width: 11, height: 11, borderRadius: '50%', border: '2px solid #fff',
        background: `conic-gradient(#fff ${p}turn, transparent ${p}turn)`
      }} />
      <span>{done} of {total} chores</span>
    </div>
  );
}

function StandBy() {
  return (
    <div style={{
      width: 290, height: 130, borderRadius: 18, background: '#0B0F13', padding: 14,
      display: 'flex', flexDirection: 'column', gap: 9, border: '1px solid rgb(255 255 255 / .08)'
    }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
        <Label dark>Last 26 weeks</Label>
        <Money size={13}>9 day streak</Money>
      </div>
      <RainbowGrid weeks={wmode().standby.weeks} dark cell={wmode().standby.cell} gap={1.5} radius={1.5} style={{ alignSelf: 'center' }} />
    </div>
  );
}

Object.assign(window, {
  RainbowGrid, SmallKid, MediumYear, MediumToday, LargeKid,
  SmallParent, MediumParent, LargeParent,
  AccCircular, AccRect, AccInline, StandBy,
  wmode, WIDGET_MODES
});
