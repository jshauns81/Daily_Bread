const DS = window.DailyBreadDesignSystem_2a4964;
const { GlassCard, SectionHeader, Chip, Tag, StatTile, ProgressRing, Button, EmptyState,
  InlineError, Icon, ChildRow, ChildTile, WeekStrip, YearHeatmapCard, ApprovalRow, HelpRow,
  ExpandRow, ConfirmRow, PlannerGrid, PlannerChoreRow, SegmentedPicker, ChoreRow,
  AtRiskCard, ScreenTimeCard, SheetHeader, SheetCard, SheetField, SheetActionBar,
  TextField, ToggleRow, Slider, Stepper, DayPicker, ALL_DAYS } = DS;

const money = (n) => '$' + n.toFixed(2);

// ================= PARENT HOME =================
function ParentHome({ dash, onDrill, onNav }) {
  const kids = dash.children;
  const singleChild = kids.length === 1;
  const waiting = dash.approvals.length + dash.help.length;
  const doneToday = kids.reduce((s, c) => s + c.done, 0);
  const totalToday = kids.reduce((s, c) => s + c.total, 0);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <GlassCard padding="16px">
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
          <div style={{ display: 'flex', alignItems: 'baseline', gap: 8 }}>
            <span style={{ fontSize: 'var(--ds-text-title2)', fontWeight: 'var(--ds-weight-bold)' }}>
              Good evening, <span style={{ color: 'var(--ds-accent)' }}>Shaun</span>
            </span>
            <span style={{ flex: 1 }} />
            <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)', whiteSpace: 'nowrap' }}>Friday, July 24, 2026</span>
          </div>
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            <Chip tone="var(--ds-gold)" emphasized={dash.approvals.length > 0}>✦ {dash.approvals.length} awaiting approval</Chip>
            <Chip emphasized={false}>✓ {doneToday} done today</Chip>
            <Chip tone="var(--ds-help)" emphasized={dash.help.length > 0}>! {dash.help.length} needs help</Chip>
          </div>
        </div>
      </GlassCard>

      <div style={{ display: 'flex', gap: 12 }}>
        <StatTile value={waiting} label="waiting on you" color="var(--ds-accent)" />
        <StatTile value={money(dash.weekEarnings)} label="earned this week" color="var(--ds-gold)" />
        <StatTile value={doneToday + '/' + totalToday} label="chores today" />
      </div>

      <SectionHeader>Today</SectionHeader>
      {kids.length <= 2 ? (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
          {kids.map((c) => <ChildRow key={c.name} child={c} onTap={() => onDrill(c)} />)}
        </div>
      ) : (
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
          {kids.map((c) => <ChildTile key={c.name} child={c} onTap={() => onDrill(c)} />)}
        </div>
      )}

      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        <SectionHeader>Quick view — this week</SectionHeader>
        <WeekStrip days={dash.week} />
      </div>

      {/* Single-child mode: the header is singular. */}
      <SectionHeader>{singleChild ? 'Balance' : 'Balances'}</SectionHeader>
      <GlassCard>
        {kids.map((c, i) => (
          <React.Fragment key={c.name}>
            <button type="button" style={{
              display: 'flex', alignItems: 'center', gap: 8, width: '100%',
              background: 'transparent', border: 'none', cursor: 'pointer',
              fontFamily: 'inherit', textAlign: 'left', padding: '10px 0',
              fontSize: 'var(--ds-text-body)', color: 'var(--ds-label)', minHeight: 44
            }}>
              <span>{c.name}</span>
              {c.balance >= 25 ? <Tag tone="var(--ds-success)">Cash out ready</Tag> : null}
              <span style={{ flex: 1 }} />
              <span style={{ fontSize: 'var(--ds-text-subheadline)', fontWeight: 'var(--ds-weight-semibold)', color: 'var(--ds-gold)' }}>{money(c.balance)}</span>
              <Icon name="slider.horizontal.3" size={13} color="var(--ds-label-3)" />
            </button>
            {i < kids.length - 1 ? <div style={{ height: '.5px', background: 'var(--ds-hairline)' }} /> : null}
          </React.Fragment>
        ))}
      </GlassCard>

      <YearHeatmapCard title={kids[0].name + "'s year"} />
    </div>
  );
}

// ================= APPROVALS =================
function ParentApprovals({ dash, onApprove, onApproveAll, onHelp }) {
  const [showAllApprovals, setShowAllApprovals] = React.useState(false);
  const [showAllHelp, setShowAllHelp] = React.useState(false);
  const [confirming, setConfirming] = React.useState(false);
  const [justApproved, setJustApproved] = React.useState(null);
  const cap = 3;
  const singleChild = dash.children.length === 1;
  const total = dash.approvals.reduce((s, a) => s + a.value, 0);

  if (dash.approvals.length === 0 && dash.help.length === 0) {
    return <EmptyState icon="checkmark.seal" title="All caught up"
      description="Nothing needs your approval right now." />;
  }

  const shownHelp = showAllHelp ? dash.help : dash.help.slice(0, cap);
  const shownApprovals = showAllApprovals ? dash.approvals : dash.approvals.slice(0, cap);

  const approve = (a) => {
    setJustApproved(a.id);
    setTimeout(() => { setJustApproved(null); onApprove(a.id); }, 900);
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      {dash.help.length > 0 ? (
        <React.Fragment>
          <SectionHeader>Help raised</SectionHeader>
          <GlassCard>
            {shownHelp.map((h, i) => (
              <React.Fragment key={h.id}>
                <HelpRow choreName={h.choreName} reason={h.reason} childName={h.childName} onOpen={() => onHelp(h)} />
                {i < shownHelp.length - 1 ? <div style={{ height: '.5px', background: 'var(--ds-hairline)' }} /> : null}
              </React.Fragment>
            ))}
            {dash.help.length > cap ? (
              <ExpandRow expanded={showAllHelp} moreCount={dash.help.length - cap}
                label="more help requests" color="var(--ds-help)"
                onToggle={() => setShowAllHelp((s) => !s)} />
            ) : null}
          </GlassCard>
        </React.Fragment>
      ) : null}

      {dash.approvals.length > 0 ? (
        <React.Fragment>
          <SectionHeader>Waiting for approval</SectionHeader>
          <GlassCard>
            {dash.approvals.length > 1 ? (
              confirming ? (
                <ConfirmRow message={'Approve ' + dash.approvals.length + ' chores?'}
                  confirmTitle={'Approve — ' + money(total)} keepTitle="Cancel" tone="var(--ds-gold)"
                  onKeep={() => setConfirming(false)}
                  onConfirm={() => { setConfirming(false); onApproveAll(); }} />
              ) : (
                <button type="button" onClick={() => setConfirming(true)} style={{
                  display: 'flex', alignItems: 'center', gap: 7, width: '100%',
                  background: 'transparent', border: 'none', cursor: 'pointer',
                  fontFamily: 'inherit', textAlign: 'left', padding: '10px 0', minHeight: 44,
                  fontSize: 'var(--ds-text-subheadline)', fontWeight: 'var(--ds-weight-bold)',
                  color: 'var(--ds-gold)'
                }}>
                  <Icon name="checkmark.seal.fill" size={15} />
                  Approve all ({dash.approvals.length}) — {money(total)}
                </button>
              )
            ) : null}
            {shownApprovals.map((a) => (
              <ApprovalRow key={a.id} choreName={a.choreName}
                childName={singleChild ? undefined : a.childName}
                value={money(a.value)} approved={justApproved === a.id}
                onApprove={() => approve(a)} />
            ))}
            {dash.approvals.length > cap ? (
              <ExpandRow expanded={showAllApprovals} moreCount={dash.approvals.length - cap}
                label="more waiting" color="var(--ds-gold)"
                onToggle={() => setShowAllApprovals((s) => !s)} />
            ) : null}
          </GlassCard>
        </React.Fragment>
      ) : null}
    </div>
  );
}

// ================= HELP RESPOND SHEET =================
function HelpRespondSheet({ request, onChoose, onClose }) {
  const choice = (title, subtitle, prominent, kind) => (
    <button key={kind} type="button" onClick={() => onChoose(kind)} style={{
      display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 2,
      width: '100%', padding: '12px 10px', border: 'none', cursor: 'pointer',
      borderRadius: 'var(--ds-radius-swatch)', fontFamily: 'inherit',
      background: prominent ? 'var(--ds-gold)' : 'color-mix(in srgb, var(--ds-label-2) 35%, transparent)',
      color: prominent ? 'rgb(0 0 0 / .8)' : 'var(--ds-label)'
    }}>
      <span style={{ fontSize: 'var(--ds-text-body)', fontWeight: 'var(--ds-weight-bold)' }}>{title}</span>
      <span style={{ fontSize: 'var(--ds-text-caption)', opacity: .8 }}>{subtitle}</span>
    </button>
  );
  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%', padding: 16, gap: 20 }}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <span style={{ width: 9, height: 9, borderRadius: '50%', background: 'var(--ds-help)' }} />
          <SectionHeader kerning="eyebrow" color="var(--ds-help)" style={{ fontWeight: 'var(--ds-weight-heavy)' }}>Help raised</SectionHeader>
        </div>
        <div style={{ display: 'flex', alignItems: 'baseline', gap: 8 }}>
          <span style={{ fontSize: 'var(--ds-text-title2)', fontWeight: 'var(--ds-weight-bold)' }}>{request.choreName}</span>
          <span style={{ flex: 1 }} />
          {request.value ? (
            <span style={{ fontSize: 'var(--ds-text-headline)', color: 'var(--ds-gold)' }}>{money(request.value)}</span>
          ) : null}
        </div>
        <span style={{ fontSize: 'var(--ds-text-subheadline)', color: 'var(--ds-label-2)' }}>
          {request.childName} · Friday, July 24, 2026 · raised 20m ago
        </span>
      </div>

      {request.reason ? (
        <GlassCard>
          <div style={{ fontSize: 'var(--ds-text-body)', fontStyle: 'italic', color: 'var(--ds-label-2)', lineHeight: 1.45 }}>
            “{request.reason}”
          </div>
        </GlassCard>
      ) : null}

      <span style={{ flex: 1 }} />

      <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        {choice('✓ Fulfill for them', 'You did it — ' + request.childName + ' receives full credit', true, 'completedByParent')}
        {choice('Grant dispensation', 'Excused for today. No penalty, no earning', false, 'excused')}
        {choice('↺ They must try again', 'Back to pending — ' + request.childName + ' does it themselves', false, 'denied')}
      </div>
    </div>
  );
}

// ================= PLANNER =================
function ParentPlanner({ chores, onToggleDay, onEdit, onAdd, singleChild }) {
  const [kind, setKind] = React.useState('Task');
  const [isGrid, setIsGrid] = React.useState(true);
  const [confirmingId, setConfirmingId] = React.useState(null);
  const visible = chores.filter((c) => c.kind === kind);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      <SegmentedPicker value={kind} onChange={setKind}
        options={[{ value: 'Task', label: 'Tasks' }, { value: 'Routine', label: 'Routines' }]} />

      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
        <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)' }}>
          {visible.length} {kind === 'Task' ? 'task' : 'routine'}{visible.length === 1 ? '' : 's'}
        </span>
        <span style={{ flex: 1 }} />
        <button type="button" onClick={() => setIsGrid((g) => !g)} aria-label={isGrid ? 'List view' : 'Grid view'}
          style={{ background: 'transparent', border: 'none', cursor: 'pointer', color: 'var(--ds-accent)', padding: 4 }}>
          <Icon name={isGrid ? 'list.bullet' : 'square.grid.2x2'} size={18} />
        </button>
        <button type="button" onClick={onAdd} aria-label="Add chore"
          style={{ background: 'transparent', border: 'none', cursor: 'pointer', color: 'var(--ds-accent)', padding: 4 }}>
          <Icon name="plus" size={18} />
        </button>
      </div>

      {visible.length === 0 ? (
        <EmptyState icon="checklist" title="No chores yet"
          description="Add the first one. Tasks earn money; Routines are just expected."
          action={<Button icon="plus" onClick={onAdd}>Add a chore</Button>} />
      ) : isGrid ? (
        <GlassCard padding="8px">
          <PlannerGrid chores={visible} showAssignee={!singleChild}
            onToggle={onToggleDay} onEdit={onEdit} />
        </GlassCard>
      ) : (
        <GlassCard>
          {visible.map((c, i) => (
            <React.Fragment key={c.id}>
              {confirmingId === c.id ? (
                <ConfirmRow message={'Delete “' + c.name + '”? This can\'t be undone.'}
                  onKeep={() => setConfirmingId(null)} onConfirm={() => setConfirmingId(null)} />
              ) : (
                <PlannerChoreRow name={c.name} icon={c.icon}
                  value={c.value} isTask={c.kind === 'Task'}
                  schedule={c.weeklyTarget ? 'up to ' + c.weeklyTarget + '× a week' : scheduleLabel(c.days)}
                  assignee={singleChild ? undefined : c.assignee}
                  importance={c.importance} active={c.active !== false}
                  onTap={() => onEdit(c)} />
              )}
              {i < visible.length - 1 ? <div style={{ height: '.5px', background: 'var(--ds-hairline)' }} /> : null}
            </React.Fragment>
          ))}
        </GlassCard>
      )}
    </div>
  );
}

function scheduleLabel(days) {
  if (!days || days.length === 0) return 'No days';
  if (days.length === 7) return 'Every day';
  const short = { Sunday: 'Sun', Monday: 'Mon', Tuesday: 'Tue', Wednesday: 'Wed', Thursday: 'Thu', Friday: 'Fri', Saturday: 'Sat' };
  if (days.length === 5 && !days.includes('Sunday') && !days.includes('Saturday')) return 'Weekdays';
  return ALL_DAYS.filter((d) => days.includes(d)).map((d) => short[d]).join(', ');
}

// ================= CHORE EDITOR SHEET =================
function ChoreEditorSheet({ chore, singleChild, onSave, onCancel }) {
  const isEditing = !!chore;
  const [name, setName] = React.useState(chore ? chore.name : '');
  const [icon, setIcon] = React.useState(chore ? chore.icon : '');
  const [isTask, setIsTask] = React.useState(chore ? chore.kind === 'Task' : true);
  const [earn, setEarn] = React.useState(chore && chore.value ? chore.value.replace('$', '') : '1.00');
  const [isWeekly, setIsWeekly] = React.useState(chore ? !!chore.weeklyTarget : false);
  const [days, setDays] = React.useState(chore && chore.days ? chore.days : ALL_DAYS);
  const [weeklyTarget, setWeeklyTarget] = React.useState(chore && chore.weeklyTarget ? chore.weeklyTarget : 3);
  const [repeatable, setRepeatable] = React.useState(false);
  const [importance, setImportance] = React.useState(chore ? (chore.importance || 0) : 0);
  const [autoApprove, setAutoApprove] = React.useState(true);
  const [notes, setNotes] = React.useState('');
  const canSave = name.trim().length > 0 && (isWeekly || days.length > 0);
  const noun = isTask ? 'Task' : 'Routine';

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      <SheetHeader title={(isEditing ? 'Edit ' : 'New ') + noun} />
      <div style={{ flex: 1, overflowY: 'auto', padding: '4px 16px 12px', display: 'flex', flexDirection: 'column', gap: 14 }}>
        <SheetCard>
          <div style={{ display: 'flex', gap: 10 }}>
            <TextField value={icon} onChange={(v) => setIcon(v.slice(0, 2))} placeholder="🧺" width={46} align="center" />
            <TextField value={name} onChange={setName} placeholder="What's the chore?" />
          </div>
        </SheetCard>

        <SheetCard>
          <SegmentedPicker value={isTask ? 'task' : 'routine'} onChange={(v) => setIsTask(v === 'task')}
            options={[{ value: 'task', label: 'Earns' }, { value: 'routine', label: 'Expected' }]} />
          {isTask ? (
            <SheetField label="Amount"><TextField prefix="$" value={earn} onChange={setEarn} placeholder="1.00" /></SheetField>
          ) : null}
          <div style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)' }}>
            {isTask ? "Earns money when it's done and approved." : 'Just expected — no money attached.'}
          </div>
        </SheetCard>

        <SheetCard title="Schedule rule">
          <SegmentedPicker value={isWeekly ? 'weekly' : 'days'} onChange={(v) => setIsWeekly(v === 'weekly')}
            options={[{ value: 'days', label: 'Fixed days' }, { value: 'weekly', label: 'Weekly goal' }]} />
          {isWeekly ? (
            <React.Fragment>
              <Stepper label={'up to ' + weeklyTarget + '× a week'} value={weeklyTarget} min={1} max={7} onChange={setWeeklyTarget} />
              <ToggleRow label="Bonus reps allowed" checked={repeatable} onChange={setRepeatable} />
            </React.Fragment>
          ) : (
            <React.Fragment>
              <DayPicker selected={days} onChange={setDays} />
              {days.length === 0 ? (
                <InlineError icon="exclamationmark.circle" style={{ color: 'var(--ds-help)' }}>Pick at least one day.</InlineError>
              ) : null}
            </React.Fragment>
          )}
        </SheetCard>

        {/* Single-child mode: no "Who's it for" card at all. */}

        <SheetCard title="Screen time">
          <SheetField label={importance === 0 ? 'No screen-time impact' : 'Importance'}
            value={importance === 0 ? null : importance + ' of 10'} valueColor="var(--ds-accent)">
            <Slider value={importance} max={10} onChange={setImportance} />
          </SheetField>
          {importance > 0 ? (
            <div style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)' }}>
              Missing it costs a share of the weekly screen-time budget.
            </div>
          ) : null}
        </SheetCard>

        <SheetCard title="Options">
          <ToggleRow label="Auto-approve completions" description="No parent check needed"
            checked={autoApprove} onChange={setAutoApprove} />
          <SheetField label="Notes (optional)">
            <TextField multiline rows={2} value={notes} onChange={setNotes} placeholder="e.g. don't forget under the bed" />
          </SheetField>
        </SheetCard>
      </div>
      <div style={{ padding: 16 }}>
        <SheetActionBar saveTitle={isEditing ? 'Save' : 'Create'} canSave={canSave}
          onCancel={onCancel}
          onSave={() => onSave({
            id: chore ? chore.id : Date.now(), name: name.trim(), icon: icon || undefined,
            kind: isTask ? 'Task' : 'Routine', value: isTask ? '$' + Number(earn || 0).toFixed(2) : undefined,
            days: isWeekly ? undefined : days, weeklyTarget: isWeekly ? weeklyTarget : undefined,
            importance, active: true
          })} />
      </div>
    </div>
  );
}

// ================= KID'S DAY (parent drill-in) =================
function ParentChildDay({ child }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <GlassCard>
        <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
          <ProgressRing progress={child.total ? child.done / child.total : 0} label={child.done + '/' + child.total} />
          <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            <span style={{ fontSize: 'var(--ds-text-subheadline)', color: 'var(--ds-label-2)' }}>Friday, July 24, 2026</span>
            <span style={{ display: 'flex', alignItems: 'baseline', gap: 6, fontSize: 'var(--ds-text-subheadline)' }}>
              <b style={{ color: 'var(--ds-gold)' }}>{money(3.5)}</b>
              <span style={{ color: 'var(--ds-label-2)' }}>today</span>
              <span style={{ color: 'var(--ds-label-3)' }}>·</span>
              <b style={{ color: 'var(--ds-gold)', fontWeight: 'var(--ds-weight-semibold)' }}>{money(child.balance)}</b>
              <span style={{ color: 'var(--ds-label-2)' }}>saved</span>
            </span>
          </div>
        </div>
      </GlassCard>
      <AtRiskCard items={[{ name: 'Dishes', detail: 'Due tonight', urgency: 'DueTonight', money: '$2.00', minutes: 20 }]} />
      {/* allowHelp is false: raising Help is the kid's own act. */}
      <GlassCard>
        <ChoreRow icon="🛏" name="Make your bed" done allowHelp={false} />
        <ChoreRow icon="🍽" name="Dishes" value="$2.00" allowHelp={false} onToggle={() => {}} />
        <ChoreRow icon="🗑" name="Take out the trash" value="$1.50" allowHelp={false} onToggle={() => {}} />
        <ChoreRow icon="📚" name="Read 20 minutes" value="$1.00" weekly={{ done: 4, target: 7 }} allowHelp={false} onToggle={() => {}} />
      </GlassCard>
      <ScreenTimeCard
        weekday={{ minutes: '2h 30m', floor: '1h 30m', atRisk: 60 }}
        weekend={{ minutes: '4h', floor: '3h', atRisk: 60 }}
        prices={[{ name: 'Take out the trash', minutes: 15 }, { name: 'Dishes', minutes: 20 }]}
        onHistory={() => {}} onSettings={() => {}} />
      <YearHeatmapCard title="The year" />
    </div>
  );
}

Object.assign(window, { ParentHome, ParentApprovals, HelpRespondSheet, ParentPlanner, ChoreEditorSheet, ParentChildDay, scheduleLabel, money });
