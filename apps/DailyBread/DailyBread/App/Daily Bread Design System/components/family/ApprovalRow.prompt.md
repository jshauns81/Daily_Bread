One-line: the parent's queue row; the Approve button is gold because approving moves money.

```jsx
<ApprovalRow choreName="Dishes" value="$2.00" onApprove={ok} />
<ApprovalRow choreName="Dishes" value="$2.00" approved />   {/* the 0.9s Blessing */}
```

Above the list sits the batch row: "Approve all (5) — $12.50", which morphs **in place**
into its own confirm ("Approve 5 chores?" + Cancel / gold Approve). Never a system dialog.
