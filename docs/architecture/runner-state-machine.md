# Runner state machine

`Applicant → PendingVerification → Verified → Available ↔ Unavailable`; assignment changes `Available → Busy`. Administrative outcomes include `Rejected`, `Suspended`, and `Deactivated`. Account lifecycle is intentionally separate from per-job assignment lifecycle.
