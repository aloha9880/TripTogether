# Implementation plan and decisions

## Architecture

`TripTogether.App`: MAUI UI, SQLite snapshot store, device ID storage, Android/iOS entry points.
`TripTogether.Core`: validated domain records, immutable change graph, versioned snapshots, ephemeral TLS nearby sessions.
`TripTogether.Core.Checks`: executable behavioral checks, independent of MAUI.

Start with a functional two-phone foreground session before adding persistent identity and remote infrastructure. .NET MAUI 10 is the stable target. Android API 26 and iOS 17 are product minimums, not minimum MAUI capabilities. Current UI uses XAML with code-behind orchestration; the next UI milestone extracts MVVM view models and dedicated editor pages.

## Milestones / acceptance gates

1. **Initial source complete; validation pending:** solution, local CRUD, SQLite persistence, manual invitation, pinned TLS transport, conflict handling and tombstones. Gate: core checks pass and iOS/Android compile; two real phones exchange create/edit/delete data.
2. **Usable local trip calendar:** MVVM, date/time pickers, trip date range/description, agenda and month view, event sorting, DST-aware editor, accessibility QA, database migrations and at-rest protection decision. Gate: device restart preserves data and time-zone/day-boundary cases pass.
3. **Trusted ongoing sharing:** long-term secure device identities, fingerprint confirmation, owner-managed membership, signed operations, expiring one-time QR invites, stored peer status, discovery fallback, acknowledgments, incremental transactional operations, retries and membership epoch rules. Gate: unauthorized/removed peers cannot submit new operations; interrupted/reordered sync converges. Do not confuse session token access with persistent membership.
4. **Phone calendars and ICS:** EventKit / CalendarContract adapters, contextual permission requests, selected import and export, native ID mappings, ICS UID/time zone/all-day round trips, safe recurrence handling. App remains authoritative; imported originals untouched. Gate: denied permissions work; repeated exports do not duplicate events.
5. **Remote sharing:** first verify a maintained native iOS/Android WebRTC data-channel binding with a real MAUI prototype. Evaluate package maintenance, licensing, SDK compatibility and AOT/linking. No library has been selected or verified yet. Implement signaling without calendar storage and STUN/TURN configuration only after feasibility gate. Direct-only option and direct/relayed status; end-to-end payload encryption with verified identity if relay is used. Gate: two networks, restrictive NAT, relay fallback, rejection in direct-only mode. If no viable binding exists, ask the owner before changing architecture.
6. **Release:** notifications/reminders, support/privacy policies, backup/export/delete, store signing, physical device matrix, threat-model review, dependency licenses and CI iOS runner/Xcode matching. Background delivery remains platform-constrained. No paid infrastructure or publication is authorized by this plan.

## Protocol 1

Invitation: Base64 JSON with host IPv4, port, certificate SHA-256, random token, expiration and trip metadata. It is not a deep link. Transfer privately; certificate trust depends on that channel.

Frames: four-byte big-endian payload size followed by UTF-8 JSON; maximum 4 MiB. Client sends token then snapshot. Host validates and merges its captured local state plus incoming state, sends combined snapshot, and closes. Guest validates again before saving. Version = 1. Session has no durable acknowledgments, offline relay or background wakeups.

Change: UUID operation ID, trip ID, event ID, author device ID, parents and complete event data or null tombstone. Heads are operations not referenced by another operation for that event. Concurrent heads remain conflicts; choosing one creates a new operation with all current heads as parents. Duplicate operation IDs with different contents are rejected. Parent existence, event consistency, graph acyclicity and payload bounds are checked before state mutation. Current snapshots must include all ancestors. Data is retained; histories are never compacted.

Security limits: invited peers are trusted editors for that session and can invent author markers or modify histories with new IDs. Persistent cryptographic authorship/membership must precede deployment beyond trusted participants. Invitation DoS and network privacy beyond transport encryption are not solved. No continuous pairing implied.

## Validation record

Authoring environment: .NET SDK, C# compiler, Android SDK and Xcode unavailable. XML/plist parsing, solution path validation and repository whitespace checks can run; these do not prove C# compilation or mobile functionality. Automated C# checks were written but not executed. No real device connection tested. CI results must be inspected after the first push before describing this as runnable/verified.

Manual matrix: Android↔iOS, Android↔Android, iOS↔iOS; Wi-Fi without internet, hotspot, client-isolated network; denied local-network access; app suspension; invalid/expired invitation; wrong token/pin; interrupted response; offline edits; duplicate retry; edit/edit and edit/delete conflict; stale device after deletion; persisted restart. Record OS versions, devices and results per case.
