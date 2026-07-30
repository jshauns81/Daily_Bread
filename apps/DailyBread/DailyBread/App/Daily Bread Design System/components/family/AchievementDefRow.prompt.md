Parent-side achievement definition — the same badge a kid sees on AchievementCard, but as an editable rule.

\`\`\`jsx
<GlassCard>
  <AchievementDefRow icon="🔥" name="Getting Going" rarity="common" points={25}
    condition="Day streak" enabled onToggle={fn} />
</GlassCard>
\`\`\`

Rarity colour and points gold come from the same \`RARITY\` map as \`AchievementCard\`, so a
badge reads identically on both sides of the app — that is the point. Turning one **off**
dims it rather than removing it: a badge a child already earned must never vanish from
their Awards wall.
