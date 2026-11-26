## What this PR adds
<!-- One or two sentences: which phase / feature, and why. -->

## Interview topics covered
<!-- List the guide terms this PR implements, e.g. "CTE, window functions, covering index". -->

## How to try it
<!-- Commands or steps a reviewer can run. -->

## Review checklist
- [ ] Builds locally (`dotnet build`, `ng build` where relevant)
- [ ] Tests pass (`dotnet test`, `ng test` where relevant)
- [ ] Logic is correct and readable; names are clear
- [ ] No hard-coded secrets (use `.env` / user-secrets)
- [ ] Errors handled; no swallowed exceptions
- [ ] Performance: no N+1 queries, needed indexes exist, reads use `AsNoTracking`
- [ ] Sonar / analyzer warnings fixed
