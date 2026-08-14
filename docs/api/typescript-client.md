# TypeScript client

Run the API in Development and fetch `/openapi/v1.json`, then generate a client with a pinned tool such as:

```bash
npx @openapitools/openapi-generator-cli generate -i http://localhost:8080/openapi/v1.json -g typescript-fetch -o src/generated/errandruns
```

Do not hand-edit generated files. Wrap the generated client with an authentication adapter that attaches the access token, performs one serialized refresh on 401, and never persists tokens in application logs. Treat money as the API's decimal JSON representation; avoid JavaScript floating-point arithmetic for totals. Send scheduled timestamps with an explicit offset (for example `2026-08-13T14:00:00+01:00`). Regenerate and contract-test the client in CI whenever OpenAPI changes.
