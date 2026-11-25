# Mobile checkout and account security API

## Paystack hosted checkout

Configure the public callback and the fixed mobile deep link:

```env
PAYSTACK_CALLBACK_URL=https://api.example.com/api/v1/payments/paystack/callback
MOBILE_APP_PAYMENT_REDIRECT_URL=errandruns://payments/result
```

1. The app initializes payment with `POST /api/v1/errands/{errandId}/payments` and opens `checkoutUrl`.
2. Paystack returns to `GET /api/v1/payments/paystack/callback?reference=...`.
3. The API looks up the server-created payment, calls Paystack Verify from the server, and checks reference, amount, currency and status.
4. The API redirects to the configured deep link with `status`, `reference`, `paymentId`, and `errandId`.
5. The app treats the deep link as navigation only and reloads `GET /api/v1/errands/{errandId}/payments/{paymentId}` for authoritative state.

The signed `charge.success` webhook remains the primary settlement path. A callback visit is never proof of payment.

## Token and device session flow

Send these headers on registration, login, Google login, and refresh:

```http
X-Device-Name: iPhone 13 - Abuja
X-Device-Platform: ios
```

Successful authentication returns a 15-minute access token, a single-use refresh token, and `sessionId`. Store the refresh token in iOS Keychain or Android Keystore, never AsyncStorage.

```http
POST /api/v1/auth/refresh
{ "refreshToken": "..." }
GET /api/v1/auth/sessions
DELETE /api/v1/auth/sessions/{sessionId}
POST /api/v1/auth/sessions/revoke-others
```

Refresh tokens are stored as SHA-256 hashes and rotated. Every access token contains a session ID; revoked sessions fail authentication immediately.

## Two-factor authentication

```http
GET /api/v1/auth/security
POST /api/v1/auth/security/authenticator/setup
POST /api/v1/auth/security/authenticator/enable
DELETE /api/v1/auth/security/authenticator
PUT /api/v1/auth/security/sms
```

Authenticator setup returns a shared key and `otpauth://` URI. Display it as a QR code locally, verify the first six-digit code with the enable endpoint, and show recovery codes once.

SMS 2FA requires a verified phone and the current password. Authenticator apps are preferred because SMS is vulnerable to SIM-swap attacks.

When 2FA is enabled, `POST /api/v1/auth/login` returns HTTP 202 with `challengeId`, `method`, and `expiresAt`. Complete it with:

```http
POST /api/v1/auth/login/2fa
{ "challengeId": "...", "code": "123456" }
```

Challenges expire after five minutes and lock after five failed attempts.

## Biometric login

Biometrics are device capabilities, not a backend authentication factor. The mobile app should use Face ID/Touch ID or Android BiometricPrompt to unlock the refresh token stored in Keychain/Keystore, then call `/auth/refresh`. Never upload biometric data or templates to this API.
