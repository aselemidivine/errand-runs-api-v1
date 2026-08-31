# Location discovery API

All routes require `Authorization: Bearer <accessToken>`. Provider credentials remain on the server.

## Recommended mobile flow

1. Generate one URL-safe session token for the search interaction.
2. Call `GET /api/v1/location-search/autocomplete?query=Admiralty&sessionToken=<token>` as the user types (debounce requests in the app).
3. Present `primaryText`, `secondaryText`, and `fullText`; retain the opaque `placeId`.
4. Resolve the chosen result with `GET /api/v1/location-search/places/{placeId}?sessionToken=<same-token>`.
5. Let the user confirm or move the pin. If moved, call `/reverse-geocode`.
6. Save the confirmed address through `/api/v1/users/me/locations`, including `googlePlaceId` and serialized `addressComponentsJson` when available.

Example saved-location additions:

```json
{
  "address": "1 Admiralty Way, Lekki Phase 1, Lagos, Nigeria",
  "latitude": 6.4474,
  "longitude": 3.4727,
  "googlePlaceId": "opaque-google-place-id",
  "addressComponentsJson": "[{\"longText\":\"Lagos\",\"shortText\":\"LA\",\"types\":[\"administrative_area_level_1\"]}]"
}
```

The saved-location API also continues to accept its existing fields. `googlePlaceId` and `addressComponentsJson` are nullable for manually entered and legacy addresses.

Errors use RFC 7807-compatible Problem Details: validation is `400`, no reverse-geocode match is `404`, a rejected/unavailable upstream is `502`, disabled provider configuration is `503`, and throttling is `429`.
