const DS = window.DailyBreadDesignSystem_2a4964;
const { Icon, GlassCard, SectionHeader } = DS;

/**
 * macOS NavigationSplitView shell. Sidebar sections come from MainView.swift's
 * Section enum, so the parent and kid sidebars are the same component with a
 * different list — one design language, two audiences.
 */
function MacWindow({ sections, active, badges = {}, onSelect, title, toolbar, back, children }) {
  return (
    <div style={{
      display: 'flex', height: '100vh', overflow: 'hidden',
      background: 'var(--ds-background)', color: 'var(--ds-label)'
    }}>
      {/* ---- Sidebar ---- */}
      <aside style={{
        width: 'var(--ds-mac-sidebar)', flex: '0 0 auto',
        display: 'flex', flexDirection: 'column',
        background: 'color-mix(in srgb, var(--ds-card) 45%, transparent)',
        backdropFilter: 'blur(30px)', WebkitBackdropFilter: 'blur(30px)',
        borderRight: '.5px solid var(--ds-hairline)'
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '18px 20px 22px' }}>
          {['#FF5F57', '#FEBC2E', '#28C840'].map((c) => (
            <span key={c} style={{ width: 12, height: 12, borderRadius: '50%', background: c }} />
          ))}
        </div>
        <nav style={{ display: 'flex', flexDirection: 'column', gap: 2, padding: '0 10px' }}>
          {sections.map((s) => {
            const on = s.key === active;
            return (
              <button key={s.key} type="button" onClick={() => onSelect(s.key)} style={{
                display: 'flex', alignItems: 'center', gap: 12, width: '100%', minHeight: 44,
                padding: '10px 12px', cursor: 'pointer', fontFamily: 'inherit', textAlign: 'left',
                border: 'none', borderRadius: 'var(--ds-radius-swatch)',
                background: on ? 'var(--ds-accent)' : 'transparent',
                color: on ? 'var(--ds-on-accent)' : 'var(--ds-label)',
                fontSize: 'var(--ds-text-body)',
                fontWeight: on ? 'var(--ds-weight-semibold)' : 'var(--ds-weight-regular)',
                transition: 'background var(--ds-snappy)'
              }}>
                <Icon name={s.icon} size={18} color={on ? 'var(--ds-on-accent)' : 'var(--ds-accent)'} />
                <span style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{s.label}</span>
                {badges[s.key] ? (
                  <span style={{
                    fontSize: 'var(--ds-text-caption)', fontWeight: 'var(--ds-weight-bold)',
                    color: on ? 'var(--ds-on-accent)' : 'var(--ds-label-2)',
                    fontVariantNumeric: 'tabular-nums'
                  }}>{badges[s.key]}</span>
                ) : null}
              </button>
            );
          })}
        </nav>
      </aside>

      {/* ---- Detail ---- */}
      <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column' }}>
        <header style={{
          display: 'flex', alignItems: 'center', gap: 12, padding: '14px 24px',
          flex: '0 0 auto', borderBottom: '.5px solid var(--ds-hairline)'
        }}>
          {back ? (
            <button type="button" onClick={back} aria-label="Back" style={{
              width: 34, height: 34, flex: '0 0 auto', cursor: 'pointer', border: 'none',
              borderRadius: '50%', display: 'grid', placeItems: 'center',
              background: 'color-mix(in srgb, var(--ds-label) 8%, transparent)',
              color: 'var(--ds-label)'
            }}><Icon name="chevron.left" size={16} /></button>
          ) : null}
          <span style={{ fontSize: 'var(--ds-text-title3)', fontWeight: 'var(--ds-weight-bold)' }}>{title}</span>
          <span style={{ flex: 1 }} />
          {toolbar ? (
            <span style={{
              display: 'inline-flex', alignItems: 'center', gap: 4, padding: 4,
              borderRadius: 'var(--ds-radius-capsule)',
              background: 'color-mix(in srgb, var(--ds-label) 7%, transparent)'
            }}>{toolbar}</span>
          ) : null}
        </header>
        <main style={{ flex: 1, overflowY: 'auto', padding: '20px 24px 32px' }}>
          <div style={{ maxWidth: 'var(--ds-mac-content)', margin: '0 auto' }}>{children}</div>
        </main>
      </div>
    </div>
  );
}

/** Round toolbar button — the macOS header's only control shape. */
function ToolbarButton({ icon, label, onClick }) {
  return (
    <button type="button" onClick={onClick} aria-label={label} style={{
      width: 34, height: 34, cursor: 'pointer', border: 'none', borderRadius: '50%',
      display: 'grid', placeItems: 'center', background: 'transparent', color: 'var(--ds-accent)'
    }}><Icon name={icon} size={17} /></button>
  );
}

const PARENT_SECTIONS = [
  { key: 'home', label: 'Home', icon: 'house' },
  { key: 'planner', label: 'Planner', icon: 'checklist' },
  { key: 'approvals', label: 'Approvals', icon: 'checkmark.circle' },
  { key: 'settings', label: 'Settings', icon: 'gearshape' }
];

const KID_SECTIONS = [
  { key: 'kidHome', label: 'Home', icon: 'house' },
  { key: 'today', label: 'Today', icon: 'sun.max' },
  { key: 'earnings', label: 'Earnings', icon: 'dollarsign.circle' },
  { key: 'awards', label: 'Awards', icon: 'trophy' },
  { key: 'settings', label: 'Settings', icon: 'gearshape' }
];

Object.assign(window, { MacWindow, ToolbarButton, PARENT_SECTIONS, KID_SECTIONS });
