One household member row — avatar, name, role, and the parent's manage menu (reset password / lock account).

\`\`\`jsx
<GlassCard>
  <FamilyMemberRow name="parent_test" role="Parent" isYou onResetPassword={fn} onToggleLock={fn} />
  <FamilyMemberRow name="emma_test" role="Child" onResetPassword={fn} onToggleLock={fn} />
</GlassCard>
\`\`\`

The avatar hue is the role: **accent for parents, gold for children**. This is the one
sanctioned non-monetary use of gold in the system — a child is the earning party.
"Lock account" is always Help red; it pauses an account rather than deleting it, and the
app never offers member deletion. Menu opens in place — no system dialog.
