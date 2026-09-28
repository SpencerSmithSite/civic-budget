# Change management policy

**Owner:** the security owner · **Reviewed:** yearly

## Rules

1. **Every change is a pull request** to `main`, which is protected: no direct pushes, no force
   pushes, and the required checks must pass before merge.
2. **The checks** (`.github/workflows/ci.yml` and `security.yml`):
   - the build, with every compiler and analyzer warning an error;
   - formatting;
   - every test, including integration tests against a real SQL Server;
   - CodeQL code scanning;
   - the known-vulnerability check on every package;
   - the infrastructure templates (Bicep build and lint, CDK synth).
3. **Review.** A second person approves each pull request once the operator has more than one
   engineer. Until then the author reviews their own diff the next day, and the pull request
   description says what changed, why, and how it was tested.
4. **Database changes** go only through EF Core migrations in the same pull request as the code.
   A test fails if the model and the migrations disagree.
5. **Infrastructure changes** go only through the templates in `infra/`, reviewed like code. The
   cloud portal is for reading, and for emergencies that are written up afterwards.
6. **Deploys** happen only from `main`, after CI passes, through GitHub Actions. An emergency fix
   follows the same path; only its review can happen after the fact, within one business day.
7. **Decisions.** A non-obvious choice gets an ADR in `docs/DECISIONS.md`, and every release gets a
   `CHANGELOG.md` entry.

## Records

- Pull request history: the checks, the approvals, and the linked issues.
- The deploy workflow's runs.
- `CHANGELOG.md` and the ADRs.
