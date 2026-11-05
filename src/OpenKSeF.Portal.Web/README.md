# OpenKSeF portal

React 19, TypeScript and Vite. The UI is in Polish. Project overview and install: [README](../../README.md).

```bash
npm ci
npm run dev     # http://localhost:5173
npm run build
npm run lint
npm test
```

Runtime settings: `VITE_API_BASE_URL`, `VITE_AUTH_AUTHORITY`, `VITE_AUTH_CLIENT_ID`. `src/vendor/ksef-pdf-generator` is a pinned third-party build: see its README.
