One-line: each kid's day in one row; use ChildTile once there are three or more.

```jsx
{kids.length <= 2
  ? kids.map(c => <ChildRow key={c.name} child={c} onTap={() => drill(c)} />)
  : <div style={{display:'grid',gridTemplateColumns:'1fr 1fr',gap:10}}>
      {kids.map(c => <ChildTile key={c.name} child={c} onTap={() => drill(c)} />)}
    </div>}
```
