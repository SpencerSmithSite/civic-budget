# Vendor management policy

**Owner:** the security owner · **Reviewed:** yearly

## Vendors

| Vendor | What it does | Data it holds | Assurance |
|---|---|---|---|
| Microsoft Azure *or* Amazon Web Services | Hosting: database, containers, secrets, logs | All of it, encrypted at rest | SOC 1, SOC 2 Type 2, ISO 27001, FedRAMP; reports downloaded from the provider's trust portal |
| GitHub | Source code, CI, code scanning, Dependabot | Source only; no customer data | SOC 2 Type 2 |
| The mail provider (any SMTP service the operator chooses) | Delivers notices and sign-in links | Recipients' names and addresses, message text | Its SOC 2 report, or an equivalent, before use |
| The AI model provider (Anthropic or Ollama, as the operator configures) | Answers the assistant's questions | Only for governments whose Administrator turned the assistant on: the question, the conversation so far, and the figures the assistant looked up to answer it. Never passwords, keys, or anything the user's account cannot see | Its SOC 2 Type 2 report and its terms on retention and training, reviewed before a key is issued |
| NuGet and npm packages | Libraries built into the app | None directly | The dependency checks in [vulnerability management](vulnerability-management.md) |

## Rules

- **Before a new vendor touches customer data:** read its SOC 2 report (or equivalent), note its
  subservice organizations and the controls it expects of its customers (the "complementary user
  entity controls"), and add it to the register.
- **Every year:** get each vendor's current report, check the auditor's opinion and any exceptions,
  and confirm the operator still does what the report expects of customers. For example: two-step
  sign-in on the cloud account, least-privilege roles, logging on.
- **When a vendor has an incident** that could touch customer data, treat it as an incident
  ([incident response](incident-response.md)).

## Records

- The vendor register.
- Each year's reports and review notes.
