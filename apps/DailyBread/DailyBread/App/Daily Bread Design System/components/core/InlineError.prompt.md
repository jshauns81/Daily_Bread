One-line: quiet inline failure — the app has no alerts and no toasts.

```jsx
{error && <InlineError>{error}</InlineError>}
{validationError && <InlineError icon="exclamationmark.circle" style={{color:'var(--ds-help)'}}>{validationError}</InlineError>}
```
