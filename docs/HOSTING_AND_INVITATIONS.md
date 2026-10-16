# Hosting, invitation and join acceptance checks

## Implementation and scope

No cloud service is needed for nearby synchronization. Sending an invitation uses the OS share sheet: the user chooses the recipient/app and confirms sending there. TripTogether cannot guarantee a notification is delivered; it reports incoming invitations after app-link activation. There is no APNs/FCM integration or background wake-up promise.

## Session protocol 2

1. Host binds its selected IPv4 interface and generates an ephemeral ECDSA TLS key/certificate plus a random 256-bit token. An invitation is valid for ten minutes and names a single trip.
2. Guest validates the link/version/expiration, asks user consent, connects and verifies the SHA-256 certificate pin. It sends a bounded framed join request containing protocol version, token, trip ID, random device ID and display name.
3. Host verifies the token and input before prompting for approval. The display name is unverified. A decline sends a negative decision; no calendar data is transferred.
4. After approval, guest sends its existing trip snapshot (or an empty trip). Host validates, builds a candidate merged log, persists it, then sends the combined snapshot.
5. Guest validates/merges, persists and sends a saved receipt. Only receipt-confirmed joins are counted by the host. If the receipt is lost, either side may already have saved; retrying is idempotent.
6. Host continues waiting after success, decline, malformed request, bad token, bad pin, EOF or per-peer timeout. Only stop/expiry or a nonrecoverable storage/programming error ends hosting. One peer is processed at a time, with a 90-second per-peer timeout. A slow peer can delay others; backlog clients may time out and retry.

Wire framing remains 4-byte big-endian length + UTF-8 JSON, max 4 MiB/frame. Invitation version 2 is independent of database snapshot version 1. Null collections/records, invalid graph history and unsupported versions are rejected. Tombstones remain in complete snapshots; no compaction is introduced.

## App activation

- iOS: retain existing `MauiUISceneDelegate` and scene manifest. `SceneWillConnect` captures cold-launch URL contexts; `SceneOpenUrl` handles a connected scene; legacy `OpenUrl` is also routed. Activation is queued until the page is initialized. A custom `CFBundleURLTypes` registration claims `triptogether`.
- Android: exported single-top activity registers `triptogether://join`; both `OnCreate` and `OnNewIntent` forward links.
- Inbox: one pending link in memory, deduplicated while pending, no token logging/persistence. A newer invitation replaces the pending one. It never auto-joins; an active host must be stopped before joining. Invalid/expired links show a message instead of connecting.
- Date/share modals no longer cancel hosting. Window destruction cancels it. Background execution remains best effort, so return to the host app before accepting elsewhere.

## Automated verification

Executed .NET 10 core checks: **43 passed**, including pinned-TLS socket tests using loopback (`TRIP_TEST_IP=127.0.0.1`). The sandbox has only a loopback interface, so physical networking was not tested. Tests cover calendar convergence/tombstones, raw/link round trips, malformed and expired invites, wrong token/pin, decline, multiple joins without host restart, failed guest persistence, receipt accounting, cancellation and stopped-listener refusal.

```powershell
$env:TRIP_TEST_IP = "127.0.0.1"
dotnet run --project tests/TripTogether.Core.Checks -c Release -- --network
```

Android build attempted: blocked by missing Android SDK and incomplete JDK in the authoring environment (XA5300), before native compilation. iOS build/signing cannot run here; use your existing Mac/Xcode/iOS 27 configuration. Shared UI and both platform C# adapters were type-checked against MAUI/platform reference assemblies with zero warnings/errors. This uses temporary field declarations matching the XAML and does not execute XAML generation, native linking, signing, packaging or runtime behavior. The iOS reference pack used for that check is iOS 26; iOS 27 behavior must be verified with your installed toolchain. Structural XAML/plist parsing, event-handler matching and preservation of scene declarations checked separately. Do not interpret loopback tests as phone interoperability tests.

## Physical-device acceptance matrix (pending)

Run Android→iOS, iOS→Android and same-platform pairs on ordinary Wi-Fi and a hotspot. Record devices/OS versions and results:

- Start host with auto detection; select correct interface when VPN/cellular introduces alternatives.
- Send via Messages/email; cancel the share sheet; verify hosting continues after returning.
- Tap invitation with guest app terminated, backgrounded, and already open. Test both iOS scene callbacks and Android intents. Verify banner appears once, no automatic data exchange.
- Decline banner; decline host approval; verify no trip is imported.
- Accept on both phones; verify trip persisted after restart and host still accepts another guest.
- Resend/reopen invitation; verify no duplicate events. Make offline changes, rejoin and resolve conflicts.
- Invalid/expired link, changed host IP, missing permissions, isolated Wi-Fi, stop while waiting, stop during approval, guest disconnect, failed save and receipt interruption.
- Verify no successful-join count before guest save receipt. If transfer fails after either side saves, retry and confirm convergence.
- Test large accessibility text, VoiceOver/TalkBack announcements and iPad share-sheet presentation.

## Applying source safely

Base commit: `328e7ae1058c7d0d4605d9b6364af2854ba52b7f`. Existing `SceneDelegate.cs`, `AppDelegate.cs`, iOS target version and date-picker feature are preserved; the modal completion race is fixed with a closing guard. Use the supplied Git patch on a clean base clone, or review the updated archive diff. No external deployment, push notification credentials, message sends or GitHub changes were performed by this update.
