# TripTogether

A .NET MAUI iOS/Android app for local trip planning and foreground nearby sharing.

## Current implementation

- Local trips/events in SQLite, including all-day dates, time zones, notes, location, deletion history and visible edit conflicts.
- Hosting a selected trip for ten minutes with automatic Wi-Fi address detection and manual fallback.
- **Send invitation** opens the system share sheet so the user can choose Messages, email or another app. No message is sent without the user's selection in that app.
- URL-safe `triptogether://join/...` links, copy/paste fallback, Android cold/warm activation, and iOS scene cold/warm activation. Incoming links display an in-app invitation notification with review/decline actions.
- Guest confirmation followed by a host approval prompt; authenticated guests do not exchange calendar data until approved. Device names are self-reported.
- TLS with an invitation-pinned ECDSA certificate, random token and expiration. The host survives rejected or failed peers and supports sequential joins until stopped/expired.
- Host persistence before sending results, guest persistence before sending a completion receipt, and distinct host “joined and saved” status.
- Explicit stop/cancel, last-join status, disabled editing while sharing, and no cancellation when a share sheet/date modal covers the page.

Built from upstream commit `328e7ae1058c7d0d4605d9b6364af2854ba52b7f`. Existing iOS SceneDelegate, AppDelegate and scene manifest registration are preserved. See [hosting acceptance checks](docs/HOSTING_AND_INVITATIONS.md).

Core compiled and **43 checks passed**, including real loopback TLS sockets. Android device build requires a full Android SDK/JDK; the authoring environment has no Android SDK. iOS compilation and physical Android/iOS tests remain pending. Shared UI and Android/iOS adapter managed type-checks passed without warnings/errors. These are not native builds or device tests.

## Visual Studio setup

1. Install Visual Studio with .NET 10 support and the .NET MAUI/mobile development workload. Install the current compatible Android SDK/JDK through Visual Studio.
2. Open `TripTogether.sln`. Restore NuGet packages. Set `TripTogether.App` as startup project.
3. Select `net10.0-android` and an emulator or attached Android device. For real sharing, use two phones on the same reachable network.
4. For iOS, pair Visual Studio to a Mac with the Xcode version required by the installed .NET iOS workload. Configure Apple development signing/provisioning. Select an iOS simulator or physical device. Physical-device signing credentials are not included.
5. Application ID `com.aloha9880.triptogether` is provisional: verify ownership/availability before store publication.

Command-line checks (no mobile workload required for core):

```powershell
dotnet run --project tests/TripTogether.Core.Checks -c Release
dotnet run --project tests/TripTogether.Core.Checks -c Release -- --network
dotnet build src/TripTogether.App -f net10.0-android -c Debug
```

The optional network check requires a non-loopback IPv4 address and permission to open a local listening socket. iOS target builds require the Apple toolchain. Building the whole solution on Windows without a Mac may fail on iOS; build the selected Android framework explicitly.

## Nearby sharing

1. Put both phones on the same Wi-Fi or hotspot and install the updated app on both. Older version-1 invitations use a different handshake and must be regenerated.
2. Host: select/create a trip, review the detected Wi-Fi IP (choose an interface or enter it from Settings if necessary), and tap **Start hosting selected trip**.
3. Tap **Send invitation…** and choose a recipient using the system share sheet. Alternatively, copy the link and transfer it privately. Return to TripTogether immediately and keep it open.
4. Guest: tap the link. The app opens with an invitation banner; tap **Review and join** and confirm. If the messaging app does not make custom links tappable, copy the link into the invitation field and tap **Review pasted invitation**.
5. Host: review the join-request prompt and tap **Allow join**. The guest saves a local copy. Both phones show the result; the host stays available for the next join.
6. Resolve conflicts and synchronize again as needed. To invalidate the link, tap **Stop hosting / cancel join**. Hosting expires after ten minutes; starting again creates a new token, key and port.

**Notifications:** delivery notifications are provided by the chosen messaging/email app according to its settings. TripTogether shows an invitation banner after a link is opened and a host approval prompt when an authenticated guest requests entry. There is no APNs/FCM server, recipient directory, background invite listener, or delivery guarantee. Sharing-sheet dismissal is not evidence of delivery.

Both apps must be in the foreground for a reliable join. No background mode is declared. Sending through an external app may temporarily suspend the host: return before the guest joins. Messages may travel over the internet, but calendar synchronization itself uses the local network. This is not remote synchronization.

Guest-isolated Wi-Fi may block connections. Grant iOS local-network permission. A changed IP, stopped host, expired invitation or mismatched app protocol requires a new invitation. Full snapshots remain bounded to 4 MiB / 10,000 changes. The new session protocol is version 2; database snapshots remain version 1 for compatibility with existing saved trips.

## Privacy and limits

SQLite stores calendar content unencrypted in the application sandbox. Do not claim database encryption. Android backup is disabled; iOS backup policy is not customized yet. No server, analytics, external calendar provider, or cloud account is used by the current app. The random device ID is an operation author marker, not a cryptographically verified long-term identity.

The host authenticates guests with the invitation's temporary token and asks for explicit approval on every join. The guest pins the certificate hash contained in the invitation. Treat invitations as bearer secrets; send privately and do not post links publicly. Custom URL schemes are not verified HTTPS universal/app links: another installed app could register the same scheme. App and OS sharing/clipboard histories may retain the link. No invitation tokens are intentionally logged or persisted by the app.

Invitations are reusable for sequential approved joins until stopped/expired. There is no durable membership, permanent trust, per-recipient revocation or ongoing push synchronization. Host approval confirms consent, not a cryptographically verified person. Already shared data cannot be erased from another phone.

Full history/tombstones are retained indefinitely. Trip metadata is immutable after creation. No recurrence or reminders yet. The existing date/time picker and iOS scene setup are retained.

## Planned capabilities

See `docs/IMPLEMENTATION_PLAN.md` for persistent identity/membership, incremental sync, native calendar integration, ICS, QR/discovery, remote transport, and release milestones. These are not hidden success-path stubs or advertised as implemented.

## Applying this update

The patch targets upstream commit `328e7ae1058c7d0d4605d9b6364af2854ba52b7f` on `master`. From a clean up-to-date clone, create a branch and apply the delivered patch:

```powershell
git switch -c feature/hosting-invitations-joins
git apply --check ../TripTogether-hosting.patch
git apply ../TripTogether-hosting.patch
```

Alternatively, copy the source archive over the clone, review `git diff`, then build. Commit/push from your authenticated local checkout. Existing SQLite snapshot data is compatible. Keep tokens, private keys, build outputs and signing credentials out of Git.
