import { z } from 'zod';

// Zod 4 probes `new Function` to compile schemas; the CSP forbids eval (`script-src 'self'`), so
// validate without compilation. Imported first by the entry point, before any schema is used.
z.config({ jitless: true });
