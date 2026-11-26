/**
 * Development settings. `ng build` (production) swaps this file for
 * environment.prod.ts via "fileReplacements" in angular.json.
 * The API runs on another origin (port 5101), so the browser applies CORS:
 * Erp.Api allows http://localhost:4200 explicitly. In Phase 6 this points at the gateway.
 */
export const environment = {
  production: false,
  apiBaseUrl: 'http://localhost:5101',
};
