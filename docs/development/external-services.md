# External service configuration

Copy `.env.example` to `.env` for Docker Compose. The `.env` file is ignored by Git. In hosted environments, inject the same settings from a managed secret store instead of deploying an `.env` file.

| Capability | Initial provider | Configuration section | Intended use |
|---|---|---|---|
| Maps and routes | Google Maps Platform | `ExternalServices:GoogleMaps` | geocoding, route distance, ETA inputs |
| Payments and payouts | Paystack | `ExternalServices:Paystack` | checkout, server verification, bank list/transfers, signed webhooks |
| Email | SendGrid | `ExternalServices:SendGrid` | verification, receipts, security and status notices |
| SMS/OTP | Termii | `ExternalServices:Termii` | Nigerian SMS OTP and time-sensitive notifications |
| KYC | Smile Identity | `ExternalServices:SmileIdentity` | runner identity checks and signed callbacks |
| Private files | Azure Blob Storage | `ExternalServices:AzureBlobStorage` | KYC, prescriptions, receipts and delivery proof |
| Push | Firebase Cloud Messaging | `ExternalServices:Firebase` | runner/customer mobile notifications |
| Telemetry | OTLP-compatible collector | `ExternalServices:OpenTelemetry` | traces and metrics export |

All integrations are disabled by default. Set the provider's `Enabled` value to `true` only after supplying its required credentials. Startup validation rejects enabled but incomplete configurations.

ASP.NET Core maps double underscores in environment variable names to nested configuration. For example:

```text
ExternalServices__Paystack__SecretKey
```

maps to:

```text
ExternalServices:Paystack:SecretKey
```

The shorter names in `.env.example` are translated to these ASP.NET Core names by `docker-compose.yml`.

## Secret-handling rules

- Paystack secret keys, webhook secrets, SendGrid/Termii/Google/KYC keys, storage connection strings, Firebase service accounts, OTLP authorization headers and JWT signing keys are server-only.
- A Paystack public key may be shared with an approved client, but it must still be environment-specific.
- Restrict Google credentials to the required APIs, server identity/IP and separate development/staging/production projects.
- Never log configuration objects, provider request authorization headers, OTPs, webhook signatures or service-account JSON.
- Rotate a secret immediately if it is committed, pasted into logs or shared outside the authorized team.
- Production should use Azure Key Vault, AWS Secrets Manager, Google Secret Manager or the deployment platform's equivalent.

The code currently binds, validates and supplies configured HTTP clients for these providers. Actual provider adapters and business endpoints should be added with their corresponding vertical slices; configuration alone does not claim payment, messaging, KYC, storage or push workflows are implemented.
