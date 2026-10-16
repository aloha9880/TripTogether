# Implementation plan and decisions

## Architecture

- `TripTogether.App`: MAUI XAML/code-behind, SQLite storage, platform activation, invitation banner and sharing UI. Existing iOS scene delegates and date picker retained.
- `TripTogether.Core`: trip/event validation, immutable revision graph, full snapshots, URL-safe invitations, local address selection and TLS host/join protocol.
- `TripTogether.Core.Checks`: independent executable checks, including real loopback TLS sessions.

.NET MAUI 10 remains the target; Android API 26 and iOS 17 are product minimums. Protocol 2 exchanges database snapshots version 1, so existing saved data remains compatible. See [HOSTING_AND_INVITATIONS.md](HOSTING_AND_INVITATIONS.md) for the implemented protocol and acceptance matrix.

## Implemented

Local trip/event editing, SQLite persistence, manual date/time pickers, conflict resolution and tombstones; ten-minute hosting, sequential joins, expiring TLS-pinned invitations, system share-sheet delivery, incoming link banners, Android cold/warm and iOS scene cold/warm URL handling; guest consent and host approval; save-before-success semantics with receipt accounting; cancellation and failure recovery.

Core compiled and 43 checks passed. Shared UI and native adapter managed type-checks passed. Full native mobile builds and physical-device tests remain pending. The Android build attempt stops at missing SDK/JDK prerequisites (XA5300); iOS requires the user's Mac/Xcode setup.

## Next gates

1. Run the physical-device acceptance matrix for the implemented hosting/invitation flow. Verify iOS 27 cold/warm scene activation, local-network permission, iPad share-sheet presentation and Android intents.
2. Extract dedicated MVVM editor/sharing view models, add trip date ranges/description, month calendar and agenda sorting. Harden backup policy, database migrations and at-rest protection based on deployment requirements.
3. Add durable cryptographic device identities and owner-managed membership before permanent trust, per-peer revocation and automatic incremental synchronization. Current session token access is not durable membership. Add operation acknowledgment/retry queues and safe history compaction only with explicit protocol design.
4. Implement optional EventKit/CalendarContract adapters, contextual permissions, selected imports/exports, stable native ID mappings and ICS round trips. Never overwrite unrelated native originals. Handle recurrence explicitly.
5. Validate maintained native iOS/Android WebRTC data channels before remote implementation. Then add signaling/STUN/TURN, direct-only settings and truthful direct/relayed connection state. No binding has been selected or verified; ask before substituting another architecture if feasibility fails.
6. Choose whether APNs/FCM notifications are required. Current invitation delivery uses the recipient's messaging/email app and an in-app banner after link activation. Real push requires credentials, recipient registration and a service; it cannot provide arbitrary continuous background P2P sockets.
7. Complete release QA, notifications/reminders, privacy policies, export/delete, licensing, signing and store prerequisites. No paid provisioning or public deployment performed.

## Persistence and security

Full-history snapshots remain bounded to 4 MiB/frame and 10,000 changes. Operation parents identify causality; concurrent heads remain conflicts and deletion tombstones never compact automatically. No changes depend on device clock ordering.

SQLite contents are not encrypted by this implementation. Device ID is a random author marker in secure storage, not a verified identity. Invitations are bearer secrets, reusable only while the selected host session is active and subject to host approval. Custom URL schemes are not verified HTTPS links. Foreground operation is required for reliable joins; serverless remote/background delivery is not promised.
