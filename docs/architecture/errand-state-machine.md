# Errand state machine

`Draft → PendingEstimate → PendingPayment → PaymentConfirmed → SearchingForRunner → RunnerAssigned → RunnerAccepted → RunnerEnRoute → AtStop ↔ TaskInProgress → AwaitingConfirmation → Completed`.

Cancellation is allowed before completion, subject to future fee policy. Dispute and failure transitions are administrator/system commands. Clients invoke actions, never assign status. Stop progression is sequential and only the assigned runner can mutate execution; only the owning customer confirms completion.
