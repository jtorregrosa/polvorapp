import { setupServer } from 'msw/node';

/** Network mock for component tests; each test registers the handlers it needs with `server.use`. */
export const server = setupServer();
