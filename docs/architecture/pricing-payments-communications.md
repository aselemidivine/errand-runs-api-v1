# Pricing, payments, and communications

## Current pricing model

The server calculates the service fee; clients cannot submit or overwrite it. The current MVP formula is `NGN 2,500 + NGN 750 × additional stops after the first two`. A two-stop route therefore costs NGN 2,500, and a four-stop route costs NGN 4,000. `PricingService` supports a bounded percentage discount, but errand creation currently passes zero because memberships are not implemented.

The customer's estimate is `merchandise estimate + service fee`. Merchandise is customer money reserved for goods and is not platform revenue or runner income. The supplied UI also shows distance, time, complexity, urgency, and zone components; these are planned pricing inputs and are not yet calculated by this implementation.

## Payment and settlement model

Customer payment follows `Pending → Authorized/Confirmed → Failed/Refunded`. Matching requires the errand to be `PaymentConfirmed`. The payment entity and provider contracts exist, but customer checkout initialization and collection-webhook endpoints remain a separate unfinished slice; client code must never set `PaymentConfirmed` directly.

After all stops are complete, the customer confirms delivery. That action credits one idempotent runner earning to the ledger. By default the runner receives 80% of the service fee and the platform retains 20% gross margin before payment fees, transfer charges, support, refunds, and operating costs. Both the split and the NGN 50 payout fee are configuration values.

A verified runner registers a bank account through Paystack. ErrandRuns stores the resolved account name, last four digits, and Paystack recipient token—not the raw account number. A withdrawal requires an idempotency key. Signed Paystack transfer webhooks move a payout from Submitted to Paid, Failed, or Reversed; failed/reversed transfers create a compensating ledger credit.

## Communications

Notifications, conversations, messages, read receipts, and voice-call metadata are persisted. SignalR at `/hubs/communications` delivers notification, message, read-receipt, call-lifecycle, and WebRTC signaling events in real time. Conversation access is limited to the customer and assigned runner for that errand.

Voice APIs manage ringing, answering, declining, and ending. SignalR relays WebRTC offer, answer, and ICE-candidate payloads. Audio does not pass through this API. Production clients still need WebRTC media capture plus configured STUN/TURN infrastructure (or a telecom/voice provider); the API deliberately does not expose customer or runner phone numbers.
