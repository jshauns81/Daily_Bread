Supervised-driving log — a totals card plus one row per drive, for teens working toward a permit.

\`\`\`jsx
<DrivingTotals totalHours={0} nightHours={0} />
<GlassCard>
  <DrivingLogRow date="Jun 25" duration="36m" from="22:03" to="22:39"
    supervisor="Dad" isNight status="Pending" note="Highway practice" />
</GlassCard>
\`\`\`

Only **approved** drives count toward the totals, so a fresh entry shows \`status="Pending"\`
and leaves the header at its old number. This is the clearest instance of the app's
teen-tier surface: same cards and rows as the kid screens, but the content assumes a
15-to-17-year-old. Empty state: "No drives logged / Tap + to log a supervised drive.
It counts toward your hours once a parent approves."
