const DS = window.DailyBreadDesignSystem_2a4964;
const { GlassCard, SectionHeader, Chip, Tag, StatTile, ProgressRing, ProgressBar, Button,
  EmptyState, InlineError, Icon, ChoreRow, AtRiskCard, ScreenTimeCard, YearHeatmapCard,
  LevelBadge, XPBar, TierFor, AchievementCard, BalanceHero, GoalCard, TransactionRow,
  ThemeRow, DB_THEMES, ToggleRow, SheetHeader, SheetCard, SheetField, SheetActionBar,
  TextField } = DS;

const money = (n) => '$' + n.toFixed(2);

// ================= KID HOME =================
function KidHome({ state, onDone, onHelp, onNav }) {
  const { chores, balance, streak, earnedToday, goal, badges, name } = state;
  const total = chores.length;
  const done = chores.filter((c) => c.done).length;
  const pending = chores.filter((c) => !c.done && !c.help).length;
  const pct = total === 0 ? 0 : Math.round((done / total) * 100);
  const tier = TierFor(pct);
  const nextUp = chores.find((c) => !c.done && !c.help) || chores.find((c) => c.help);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={{ fontSize: 'var(--ds-text-title2)', fontWeight: 'var(--ds-weight-bold)' }}>
        Good evening, <span style={{ color: 'var(--ds-accent)' }}>{name}</span>
      </div>

      {/* The hero — the one card that opts out of glassCard() */}
      <div style={{
        borderRadius: 'var(--ds-radius-hero)', padding: 20,
        background: 'linear-gradient(to bottom, color-mix(in srgb, ' + tier.color + ' 20%, var(--ds-card)), color-mix(in srgb, ' + tier.color + ' 2%, var(--ds-card)))',
        boxShadow: 'var(--ds-card-shadow), inset 0 0 0 1px color-mix(in srgb, ' + tier.color + ' 28%, transparent)',
        display: 'flex', flexDirection: 'column', gap: 18
      }}>
        <LevelBadge percent={pct} style={{ alignSelf: 'center' }} />
        <XPBar done={done} total={total} />
        <div style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)', textAlign: 'center' }}>
          {total === 0 ? '😎 Rest day — no quests'
            : pending === 0 ? '🎉 Every quest done today!'
            : pending + ' quest' + (pending === 1 ? '' : 's') + ' remaining'}
        </div>
        <div style={{ height: 1, background: 'color-mix(in srgb, var(--ds-label) 8%, transparent)' }} />
        <div style={{ display: 'flex', alignItems: 'center', gap: 18, fontSize: 'var(--ds-text-subheadline)', flexWrap: 'wrap' }}>
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5 }}>
            <span style={{ fontFamily: 'var(--ds-font-emoji)' }}>💰</span>
            <b data-coin-target style={{ color: 'var(--ds-gold)' }}>{money(balance)}</b>
          </span>
          {streak > 0 ? (
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5 }}>
              <span style={{ fontFamily: 'var(--ds-font-emoji)' }}>🔥</span>
              <b style={{ color: 'var(--db-streak)', fontWeight: 'var(--ds-weight-semibold)' }}>{streak} day streak</b>
            </span>
          ) : null}
        </div>
        {earnedToday > 0 ? (
          <div style={{ display: 'flex', alignItems: 'center', gap: 4, fontSize: 'var(--ds-text-subheadline)' }}>
            <span style={{ fontFamily: 'var(--ds-font-emoji)' }}>✨</span>
            <b style={{ color: 'var(--ds-gold)', fontWeight: 'var(--ds-weight-heavy)' }}>+{money(earnedToday)}</b>
            <span style={{ color: 'var(--ds-label-2)' }}>today</span>
          </div>
        ) : null}
      </div>

      <GoalCard name={goal.name} current={money(balance)} target={money(goal.target)}
        percent={Math.min(100, Math.round((balance / goal.target) * 100))} onTap={() => onNav('earnings')} />

      <div style={{ display: 'flex', gap: 12, alignItems: 'stretch' }}>
        <GlassCard style={{ flex: 1 }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 5 }}>
              <Icon name="trophy" size={12} color="var(--ds-label-2)" />
              <SectionHeader>Latest</SectionHeader>
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <span style={{ fontSize: 20, fontFamily: 'var(--ds-font-emoji)' }}>🔥</span>
              <span style={{ fontSize: 'var(--ds-text-subheadline)', fontWeight: 'var(--ds-weight-semibold)' }}>Seven days</span>
            </div>
            <span style={{ fontSize: 'var(--ds-text-caption)', fontWeight: 'var(--ds-weight-bold)', color: 'var(--ds-gold)' }}>+50 pts</span>
          </div>
        </GlassCard>
        <GlassCard padding="var(--ds-card-padding-tight)" style={{ minWidth: 96, cursor: 'pointer' }}>
          <div onClick={() => onNav('awards')} style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 4 }}>
            <span style={{ display: 'flex', alignItems: 'baseline', gap: 2 }}>
              <span style={{ fontSize: 'var(--ds-text-title)', fontWeight: 'var(--ds-weight-heavy)', color: 'var(--ds-accent)', lineHeight: 1 }}>{badges.earned}</span>
              <span style={{ fontSize: 'var(--ds-text-subheadline)', fontWeight: 'var(--ds-weight-semibold)', color: 'var(--ds-label-2)' }}>/ {badges.total}</span>
            </span>
            <SectionHeader kerning="eyebrow" style={{ fontSize: 'var(--ds-text-caption2)' }}>Badges</SectionHeader>
          </div>
        </GlassCard>
      </div>

      {nextUp ? (
        <GlassCard>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
            <SectionHeader kerning="eyebrow" color="var(--ds-accent)">Next up</SectionHeader>
            <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
              <span style={{
                width: 'var(--ds-icon-tile-lg)', height: 'var(--ds-icon-tile-lg)', flex: '0 0 auto',
                borderRadius: 'var(--ds-radius-icon-tile)', background: 'var(--ds-fill)',
                display: 'grid', placeItems: 'center', fontSize: 22, fontFamily: 'var(--ds-font-emoji)'
              }}>{nextUp.icon}</span>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 2, minWidth: 0 }}>
                <span style={{ fontSize: 'var(--ds-text-body)', fontWeight: 'var(--ds-weight-semibold)' }}>{nextUp.name}</span>
                {nextUp.help ? (
                  <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-help)' }}>Help raised — waiting on a grown-up</span>
                ) : nextUp.weekly ? (
                  <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)' }}>{nextUp.weekly.done} of {nextUp.weekly.target} this week</span>
                ) : nextUp.value ? (
                  <span style={{ fontSize: 'var(--ds-text-caption)', fontWeight: 'var(--ds-weight-semibold)', color: 'var(--ds-gold)' }}>{money(nextUp.value)}</span>
                ) : null}
              </div>
            </div>
            {!nextUp.help ? (
              <div style={{ display: 'flex', gap: 10 }}>
                <Button variant="capsuleHelp" icon="questionmark.circle" style={{ flex: 1 }} onClick={() => onHelp(nextUp.id)}>Help</Button>
                <Button variant="capsuleAccent" icon="checkmark.circle.fill" style={{ flex: 1 }} onClick={() => onDone(nextUp.id)}>Done</Button>
              </div>
            ) : null}
          </div>
        </GlassCard>
      ) : total > 0 ? (
        <GlassCard>
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 8, padding: '8px 0' }}>
            <span style={{ fontSize: 40, fontFamily: 'var(--ds-font-emoji)' }}>🎉</span>
            <span style={{ fontSize: 'var(--ds-text-headline)', fontWeight: 'var(--ds-weight-semibold)', color: 'var(--ds-success)' }}>Every quest done today!</span>
          </div>
        </GlassCard>
      ) : null}
    </div>
  );
}

// ================= TODAY =================
function KidToday({ state, onDone, onHelp }) {
  const { chores, balance, streak, earnedToday, name } = state;
  const total = chores.length;
  const done = chores.filter((c) => c.done).length;
  const atRisk = chores.filter((c) => !c.done && !c.help).slice(0, 3).map((c, i) => ({
    name: c.name,
    detail: i === 0 ? 'Due tonight' : c.weekly ? c.weekly.done + ' of ' + c.weekly.target + ' — getting tight' : 'Must happen every day',
    urgency: i === 0 ? 'DueTonight' : c.weekly ? 'GettingTight' : 'MustDoDaily',
    money: c.value ? money(c.value) : undefined,
    minutes: c.minutes
  }));
  const totalMin = atRisk.reduce((s, i) => s + (i.minutes || 0), 0);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <GlassCard>
        <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
          <ProgressRing progress={total ? done / total : 0} label={done + '/' + total} />
          <div style={{ display: 'flex', flexDirection: 'column', gap: 2, minWidth: 0 }}>
            <span style={{ fontSize: 'var(--ds-text-headline)' }}>
              Good evening, <span style={{ color: 'var(--ds-accent)' }}>{name}</span>.
            </span>
            <span style={{ fontSize: 'var(--ds-text-subheadline)', color: 'var(--ds-label-2)' }}>Friday, July 24, 2026</span>
            <span style={{ display: 'flex', alignItems: 'baseline', gap: 6, fontSize: 'var(--ds-text-subheadline)', flexWrap: 'wrap' }}>
              <b style={{ color: 'var(--ds-gold)' }}>{money(earnedToday)}</b>
              <span style={{ color: 'var(--ds-label-2)' }}>today</span>
              <span style={{ color: 'var(--ds-label-3)' }}>·</span>
              <b data-coin-target style={{ color: 'var(--ds-gold)', fontWeight: 'var(--ds-weight-semibold)' }}>{money(balance)}</b>
              <span style={{ color: 'var(--ds-label-2)' }}>saved</span>
            </span>
            {streak > 1 ? (
              <span style={{ fontSize: 'var(--ds-text-caption)', fontWeight: 'var(--ds-weight-bold)', color: 'var(--ds-gold)', paddingTop: 1 }}>
                🔥 {streak}-day streak
              </span>
            ) : null}
          </div>
        </div>
      </GlassCard>

      <AtRiskCard items={atRisk} totalMinutes={totalMin}
        totalMoney={atRisk.some((i) => i.money) ? money(atRisk.reduce((s, i) => s + (i.money ? parseFloat(i.money.slice(1)) : 0), 0)) : undefined}
        previewLine="Trash goes out tomorrow." />

      <GlassCard>
        {chores.map((c) => (
          <ChoreRow key={c.id} icon={c.icon} name={c.name}
            value={c.value ? money(c.value) : undefined} done={c.done} help={c.help}
            approvedBy={c.approvedBy} weekly={c.weekly}
            onToggle={() => onDone(c.id)} onHelp={() => onHelp(c.id)} />
        ))}
      </GlassCard>

      {total > 0 && done === total ? (
        <div style={{
          display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 7,
          padding: '13px 16px', borderRadius: 'var(--ds-radius-card)',
          background: 'color-mix(in srgb, var(--ds-gold) 10%, transparent)',
          color: 'var(--ds-gold)', fontSize: 'var(--ds-text-subheadline)',
          fontWeight: 'var(--ds-weight-semibold)'
        }}>
          <Icon name="party.popper" size={15} />Day complete — every chore done ✨
        </div>
      ) : null}

      <ScreenTimeCard
        weekday={{ minutes: '2h 30m', floor: '1h 30m', atRisk: 60 }}
        weekend={{ minutes: '4h', floor: '3h', atRisk: 60 }}
        prices={[{ name: 'Take out the trash', minutes: 15 }, { name: 'Dishes', minutes: 20 }, { name: 'Mow the lawn', minutes: 30 }]}
        onHistory={() => {}} />

      <YearHeatmapCard title="Your year" />
    </div>
  );
}

// ================= EARNINGS =================
function KidEarnings({ state, onNav }) {
  const { balance, goal } = state;
  const bars = [3, 0, 6, 2, 8, 4, 5, 0, 7, 3, 9, 2, 6, 4];
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <BalanceHero balance={money(balance)} />

      <GlassCard>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <SectionHeader kerning="eyebrow">Last 14 days</SectionHeader>
          <div style={{ display: 'flex', alignItems: 'flex-end', gap: 6, height: 120 }}>
            {bars.map((v, i) => (
              <div key={i} style={{ flex: 1, display: 'flex', flexDirection: 'column', justifyContent: 'flex-end', alignItems: 'center', gap: 5 }}>
                <div style={{ width: '100%', height: Math.max(3, v * 12), background: 'var(--ds-gold)', borderRadius: 3 }} />
                {i % 3 === 0 ? <span style={{ fontSize: 'var(--ds-text-caption2)', color: 'var(--ds-label-3)' }}>{'SMTWTFS'[i % 7]}</span> : <span style={{ height: 12 }} />}
              </div>
            ))}
          </div>
        </div>
      </GlassCard>

      <SectionHeader>Goals</SectionHeader>
      <GlassCard>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
          <div style={{ display: 'flex', alignItems: 'baseline', gap: 8 }}>
            <span style={{ fontSize: 'var(--ds-text-body)', fontWeight: 'var(--ds-weight-semibold)' }}>{goal.name}</span>
            <span style={{ flex: 1 }} />
            <span style={{ fontSize: 'var(--ds-text-subheadline)', color: 'var(--ds-label-2)' }}>{money(balance)} of {money(goal.target)}</span>
          </div>
          <ProgressBar value={Math.min(100, (balance / goal.target) * 100)} total={100} />
          <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)' }}>
            {Math.round((balance / goal.target) * 100)}% there
          </span>
          <div style={{ height: '.5px', background: 'var(--ds-hairline)' }} />
          <button type="button" onClick={() => {}} style={{
            display: 'flex', alignItems: 'center', gap: 8, background: 'transparent',
            border: 'none', cursor: 'pointer', fontFamily: 'inherit', padding: '6px 0',
            fontSize: 'var(--ds-text-body)', color: 'var(--ds-label)', minHeight: 44
          }}>
            <Icon name="target" size={16} color="var(--ds-accent)" />Manage goals
            <span style={{ flex: 1 }} />
            <Icon name="chevron.right" size={12} color="var(--ds-label-3)" />
          </button>
        </div>
      </GlassCard>

      <SectionHeader>Recent</SectionHeader>
      <GlassCard>
        <TransactionRow description="Dishes" date="Jul 24" amount="+$2.00" />
        <TransactionRow description="Take out the trash" date="Jul 24" amount="+$1.50" />
        <TransactionRow description="Read 20 minutes" date="Jul 23" amount="+$1.00" />
        <TransactionRow description="Missed: Brush teeth" date="Jul 22" amount="−$0.50" negative />
        <TransactionRow description="Cash out" date="Jul 20" amount="−$25.00" negative />
      </GlassCard>
    </div>
  );
}

// ================= AWARDS =================
const BADGES = [
  { icon: '🌱', name: 'First chore', description: 'Do your very first one', points: 10, rarity: 'common', earned: true },
  { icon: '👣', name: 'Three in a row', description: 'Three days, all done', points: 20, rarity: 'common', earned: true },
  { icon: '⚡', name: 'Perfect day', description: 'Every chore, one day', points: 30, rarity: 'uncommon', earned: true },
  { icon: '🔥', name: 'Seven days', description: 'A full week, every chore', points: 50, rarity: 'rare', earned: true },
  { icon: '💰', name: 'First dollar', description: 'Earn your first dollar', points: 15, rarity: 'common', earned: true },
  { icon: '🥇', name: 'Twenty-five', description: 'Earn $25 all together', points: 60, rarity: 'rare', earned: true },
  { icon: '💎', name: 'Fifty dollars', description: 'Earn $50 all together', points: 80, rarity: 'epic', progress: 48, target: 50 },
  { icon: '👑', name: 'Centurion', description: 'Earn $100 all together', points: 150, rarity: 'legendary', progress: 48, target: 100 },
  { icon: '🌙', name: 'Thirty days', description: 'A month without missing', points: 200, rarity: 'legendary', progress: 7, target: 30 }
];

function KidAwards() {
  const earned = BADGES.filter((b) => b.earned);
  const locked = BADGES.filter((b) => !b.earned);
  const points = earned.reduce((s, b) => s + b.points, 0);
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div style={{ display: 'flex', gap: 12 }}>
        <StatTile value={points} label="points" color="var(--ds-gold)" />
        <StatTile value={earned.length + '/' + BADGES.length} label="earned" color="var(--ds-accent)" />
      </div>
      <SectionHeader>Earned</SectionHeader>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
        {earned.map((b) => <AchievementCard key={b.name} {...b} />)}
      </div>
      <SectionHeader>Still out there</SectionHeader>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
        {locked.map((b) => <AchievementCard key={b.name} {...b} />)}
      </div>
    </div>
  );
}

// ================= SETTINGS =================
function AppSettings({ theme, onTheme, isParent, features, onFeature, childName, onManage }) {
  const [open, setOpen] = React.useState(false);
  const current = DB_THEMES.find((t) => t.key === theme) || DB_THEMES[0];
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <GlassCard>
        <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
          <span style={{
            width: 'var(--ds-avatar)', height: 'var(--ds-avatar)', flex: '0 0 auto',
            borderRadius: '50%', display: 'grid', placeItems: 'center',
            background: 'linear-gradient(135deg, var(--ds-accent), color-mix(in srgb, var(--ds-accent) 60%, transparent))',
            color: '#fff', fontSize: 'var(--ds-text-headline)', fontWeight: 'var(--ds-weight-semibold)'
          }}>{(isParent ? 'Shaun' : childName).slice(0, 1).toUpperCase()}</span>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            <span style={{ fontSize: 'var(--ds-text-body)', fontWeight: 'var(--ds-weight-semibold)' }}>{isParent ? 'shaun' : childName.toLowerCase()}</span>
            <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)' }}>{isParent ? 'Parent' : 'Child'}</span>
          </div>
        </div>
      </GlassCard>

      <GlassCard>
        <button type="button" onClick={() => setOpen((o) => !o)} style={{
          display: 'flex', alignItems: 'center', gap: 14, width: '100%',
          background: 'transparent', border: 'none', cursor: 'pointer',
          fontFamily: 'inherit', textAlign: 'left', padding: 0, minHeight: 44
        }}>
          <DS.ThemeSwatch theme={current} width={64} height={44} />
          <span style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 2 }}>
            <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)' }}>Theme</span>
            <span style={{ fontSize: 'var(--ds-text-body)', fontWeight: 'var(--ds-weight-semibold)' }}>{current.name}</span>
          </span>
          <Icon name={open ? 'chevron.up' : 'chevron.down'} size={13} color="var(--ds-label-2)" />
        </button>
        {open ? (
          <div style={{ marginTop: 10, borderTop: '.5px solid var(--ds-hairline)', paddingTop: 4 }}>
            {DB_THEMES.map((t) => (
              <ThemeRow key={t.key} theme={t} selected={t.key === theme}
                onSelect={(k) => { onTheme(k); setOpen(false); }} />
            ))}
          </div>
        ) : null}
      </GlassCard>
      <div style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)', padding: '0 4px', lineHeight: 1.5 }}>
        Pick the look you like. It changes everywhere, on every device — switch whenever you feel like a change.
      </div>

      {isParent ? (
        <React.Fragment>
          <SectionHeader>Family features</SectionHeader>
          <GlassCard>
            <ToggleRow label="Savings goals" description={'Show goals to ' + childName}
              checked={features.goals} onChange={(v) => onFeature('goals', v)} />
            <ToggleRow label="Confetti" description="Celebrate completed days"
              checked={features.confetti} onChange={(v) => onFeature('confetti', v)} />
            <ToggleRow label="Streaks" description="Show streak counters"
              checked={features.streaks} onChange={(v) => onFeature('streaks', v)} />
          </GlassCard>
          <div style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)', padding: '0 4px' }}>These apply on every device.</div>

          <SectionHeader>Manage</SectionHeader>
          <GlassCard>
            {[
              ['achievements', 'trophy', 'Achievements'],
              ['drivingApprovals', 'car', 'Driving approvals'],
              ['family', 'person.2', 'Family']
            ].map(([key, ic, l]) => (
              <button key={key} type="button" onClick={() => onManage && onManage(key)} style={{
                display: 'flex', alignItems: 'center', gap: 10, width: '100%',
                background: 'transparent', border: 'none', cursor: 'pointer',
                fontFamily: 'inherit', textAlign: 'left', padding: '9px 0',
                fontSize: 'var(--ds-text-body)', color: 'var(--ds-label)', minHeight: 44
              }}>
                <Icon name={ic} size={17} color="var(--ds-accent)" />{l}
                <span style={{ flex: 1 }} />
                <Icon name="chevron.right" size={12} color="var(--ds-label-3)" />
              </button>
            ))}
          </GlassCard>
        </React.Fragment>
      ) : (
        <React.Fragment>
          <SectionHeader>Mine</SectionHeader>
          <GlassCard>
            <button type="button" onClick={() => onManage && onManage('driving')} style={{
              display: 'flex', alignItems: 'center', gap: 10, width: '100%',
              background: 'transparent', border: 'none', cursor: 'pointer',
              fontFamily: 'inherit', textAlign: 'left', padding: '9px 0',
              fontSize: 'var(--ds-text-body)', color: 'var(--ds-label)', minHeight: 44
            }}>
              <Icon name="car" size={17} color="var(--ds-accent)" />Driving log
              <span style={{ flex: 1 }} />
              <Icon name="chevron.right" size={12} color="var(--ds-label-3)" />
            </button>
          </GlassCard>
        </React.Fragment>
      )}

      <SectionHeader>Server</SectionHeader>
      <GlassCard>
        <div style={{ display: 'flex', alignItems: 'center', padding: '6px 0', fontSize: 'var(--ds-text-body)' }}>
          <span>Connected to</span><span style={{ flex: 1 }} />
          <span style={{ color: 'var(--ds-label-2)' }}>bread.home.arpa</span>
        </div>
        <div style={{ height: '.5px', background: 'var(--ds-hairline)', margin: '4px 0' }} />
        <button type="button" style={{
          background: 'transparent', border: 'none', cursor: 'pointer', fontFamily: 'inherit',
          padding: '9px 0', fontSize: 'var(--ds-text-body)', color: 'var(--ds-help)',
          textAlign: 'left', width: '100%', minHeight: 44
        }}>Change server</button>
      </GlassCard>
      <GlassCard>
        <button type="button" style={{
          background: 'transparent', border: 'none', cursor: 'pointer', fontFamily: 'inherit',
          padding: '9px 0', fontSize: 'var(--ds-text-body)', color: 'var(--ds-help)',
          textAlign: 'left', width: '100%', minHeight: 44
        }}>Sign Out</button>
      </GlassCard>
    </div>
  );
}

// ================= HELP SHEET =================
function HelpSheet({ choreName, voiceParent, onSubmit, onCancel }) {
  const [reason, setReason] = React.useState('');
  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      <SheetHeader title="Raise Help" />
      <div style={{ flex: 1, overflowY: 'auto', padding: '4px 16px 12px', display: 'flex', flexDirection: 'column', gap: 14 }}>
        <SheetCard>
          <div style={{ fontSize: 'var(--ds-text-subheadline)', lineHeight: 1.5 }}>
            Raising Help on <b>{choreName}</b> protects it from tonight's penalty until {voiceParent} responds.
          </div>
        </SheetCard>
        <SheetCard title="What's going on?">
          <TextField multiline rows={3} value={reason} onChange={setReason} placeholder="I need a hand because…" />
        </SheetCard>
      </div>
      <div style={{ padding: 16 }}>
        <SheetActionBar saveTitle="Raise Help" canSave={reason.trim().length > 0}
          onCancel={onCancel} onSave={() => onSubmit(reason)} />
      </div>
    </div>
  );
}

Object.assign(window, { KidHome, KidToday, KidEarnings, KidAwards, AppSettings, HelpSheet, money, BADGES });
