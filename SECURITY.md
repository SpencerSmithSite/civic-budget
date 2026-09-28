# Reporting a security vulnerability

If you find a security problem in CivicBudget, please report it privately rather than in a public
issue: email **CivicBudget@spencersmith.site** with "Security" in the subject.

Please include what you found, how to reproduce it, and what an attacker could do with it. I will
acknowledge the report within 3 business days and tell you what happens next. Fix times follow the
severity (critical within 7 days); see
[vulnerability management](docs/security/policies/vulnerability-management.md).

The live demo holds only fictional data, and it is reset every night. Testing against it is
welcome, within these limits:

- no denial of service;
- no automated scanning that would keep the free database awake;
- no attempts to reach other Azure customers.

For how CivicBudget is secured, see [docs/security](docs/security/README.md).
