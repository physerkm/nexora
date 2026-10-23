# Contributing to Nexora

Thanks for your interest! Bug reports, questions and pull requests are all welcome.
Issues may be written in English or Turkish.

## Getting started

1. Fork the repository and create a branch from `main` (`fix/…`, `feat/…`, `docs/…`).
2. Run the app locally — see [Quick start](README.md#quick-start).
3. Make your change with tests, then run the checks below.
4. Open a pull request and describe what changed and why.

## Checks

```bash
dotnet build Nexora.slnx -c Release          # warnings are treated seriously; keep it at 0
dotnet test                                  # unit + integration (needs Docker, or set NEXORA_TEST_POSTGRES)
cd frontend && npm test && npm run build     # component tests + production build
cd frontend && npm run test:e2e              # browser tests (needs a nexora_e2e database, see README)
```

If you change the EF Core model, add a migration and make sure CI's check passes:

```bash
dotnet ef migrations add <Name> --project src/Nexora.Infrastructure --startup-project src/Nexora.Api
dotnet ef migrations has-pending-model-changes --project src/Nexora.Infrastructure --startup-project src/Nexora.Api
```

Index changes on `transactions` should be built `CONCURRENTLY` — see [docs/deployment.md](docs/deployment.md).

## Guidelines

- **Tenant isolation first.** Every new query on tenant-owned data must go through the EF Core query filters or
  apply the tenant explicitly (raw SQL). Add an isolation test for new endpoints.
- **Errors** are `ProblemDetails` with a stable `code`; never leak internal details.
- **Measure performance claims** — the TabFormer import (`tools/tabformer`) gives you 24M rows to test against.
- Keep `Program.cs` composition-only; endpoints live in `src/Nexora.Api/Endpoints`.
- Code, comments and API are in English; the UI text is Turkish.

## Reporting security issues

Please do not open a public issue for vulnerabilities. Use
[GitHub private vulnerability reporting](https://github.com/physerkm/nexora/security/advisories/new) instead.
