import { defineConfig } from 'orval';

// The UI calls the API only through this generated client (spec: Generated API contract).
export default defineConfig({
  polvorapp: {
    input: { target: '../contracts/openapi.json' },
    output: {
      mode: 'tags-split',
      target: 'src/api/generated/endpoints.ts',
      schemas: 'src/api/generated/model',
      client: 'react-query',
      httpClient: 'fetch',
      clean: true,
      override: {
        mutator: { path: 'src/api/http.ts', name: 'apiFetch' },
      },
    },
  },
});
