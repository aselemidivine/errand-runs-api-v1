# External service configuration

Copy `.env.example` to `.env` for Docker Compose. The `.env` file is ignored by Git. In hosted environments, inject the same settings from a managed secret store instead of deploying an `.env` file.

| Capability | Initial provider | Configuration section | Intended use |
|---|---|---|---|
| Location discovery | Google Maps Platform | `GoogleMaps` | Places Autocomplete (New), Place Details (New), reverse geocoding |
| Approximate IP location | ipapi.co | `IpGeolocation` | optional city-level onboarding hint |
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

The phone-verification slice implements Termii SMS delivery through the transactional `dnd` route. In Development with Termii disabled, the request endpoint returns `developmentCode` so the complete OTP flow can be exercised without sending SMS. Paystack runner payouts and Google-powered location discovery are also implemented. The remaining provider sections currently bind and validate configuration but do not by themselves claim a complete email, KYC, storage, or push workflow.

Set `TERMII_BASE_URL` to the regulatory base URL displayed in the Termii dashboard. Production OTP delivery also requires an approved sender ID and an activated DND/transactional route.

## Google-powered location discovery

Enable these APIs in the server's Google Cloud project and attach billing:

- Places API (New), for Autocomplete (New) and Place Details (New).
- Geocoding API, for reverse geocoding through the current v4 endpoint.

Configure a server-only key through secrets or environment variables:

```text
GoogleMaps__Enabled=true
GoogleMaps__ServerApiKey=replace-with-a-restricted-server-key
```

Restrict the key to those two APIs and to the production server identity or egress IP. Do not put it in a mobile build, response, query string, source-controlled settings file, or client-side Google SDK configuration. The backend transmits it to Google in `X-Goog-Api-Key` headers and uses field masks to limit returned and billable Place fields.

The client should create a URL-safe session token (a UUID without braces is suitable), reuse it for a sequence of autocomplete calls, then send the same token once to the selected place-details request. Start a new token after selection or abandonment.

Autocomplete is restricted to Nigeria and biased toward a 50 km circle around Lagos. A bias improves ranking but does not further restrict the already Nigerian result set. All location-search endpoints require a JWT; the group permits 60 calls per authenticated user or resolved client IP each minute.

Reverse geocoding ranks street addresses above premises, routes, and broad administrative results. The mobile app must still show the returned address to the user for confirmation before saving it.

## Approximate IP location and trusted proxies

IP lookup is disabled by default. Enable the configured provider with:

```text
IpGeolocation__Enabled=true
IpGeolocation__Provider=IpApiCo
```

The API returns only latitude, longitude, city, region, country, and `approximate: true`; it never returns the request IP and must never be used as a confirmed delivery address.

When deployed behind a reverse proxy, list only that proxy's IP addresses under `ForwardedHeaders:KnownProxies`. `X-Forwarded-For` from any other sender is ignored. Keep `ForwardLimit` equal to the number of trusted proxy hops (the default is one):

```json
{
  "ForwardedHeaders": {
    "ForwardLimit": 1,
    "KnownProxies": ["10.0.0.10"]
  }
}
```
