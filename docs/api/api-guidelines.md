# API guidelines

Routes are versioned under `/api/v1` and represent commands (`/cancel`, `/accept`, stop `/start`) rather than writable status fields. JSON uses camel case, GUID identifiers, ISO-8601 offsets and decimal money `{ amount, currency }`. Errors use RFC 7807 with `traceId`; validation adds an `errors` map. Growing collections return `{ items, page, pageSize, totalCount, totalPages }`, with `pageSize <= 100`. Critical POSTs require `Idempotency-Key` and return the original outcome on safe replay.

OpenAPI is served only in development by default. Breaking contract changes require a new API version. File transfer uses multipart upload endpoints and short-lived download URLs rather than embedding bytes.
