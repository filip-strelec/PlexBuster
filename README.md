# PlexBuster VR

A 90s video rental store in VR, stocked from your own Plex library. Walk the aisles, pick up VHS cases with
real posters on the front and the synopsis on the back, then watch a title in the store's TV room or cinema,
or put it on the checkout counter to play it on your real TV.

Built for PC VR (Valve Index through SteamVR / OpenXR) with Unity **6000.2.9f1**, URP and the XR Interaction Toolkit.

- [Quick start](#quick-start)
- [Configuration (.env)](#configuration-env)
- [Playing on your TV](#playing-on-your-tv)
- [Jukebox music](#jukebox-music)
- [What's in the store](#whats-in-the-store)
- [How it works](#how-it-works)
- [Tuning](#tuning)
- [Troubleshooting](#troubleshooting)

## Quick start

1. **Install:**
   - Unity 6000.2.9f1
   - Git LFS (models and textures are stored in LFS)
   - SteamVR, set as the OpenXR runtime (SteamVR → Settings → OpenXR → *Set SteamVR as OpenXR Runtime*)
2. **Clone:** `git lfs install`, then clone the repo.
3. **Configure:** copy `.env.example` to `.env` in the project root and fill it in (see [Configuration](#configuration-env)).
   Without it the store still runs, stocked with a made-up library.
4. **Run:** open the project, open `Assets/Scenes/Store.unity` and press Play with the headset connected.

To build, use *File → Build Profiles → Windows*. `Store.unity` is the only scene in the build.
Put the `.env` file next to the built `.exe`.

## Configuration (.env)

The server address and token are never stored in code, scenes or git. They come from environment variables:

| Variable | Required | What it is |
|---|---|---|
| `PLEX_URL` | yes | Your Plex server's address. It **must be `https://`** (the project doesn't allow plain HTTP). |
| `PLEX_TOKEN` | yes | Your Plex account token (`X-Plex-Token`). |
| `PLEX_PLAYER` | no | Part of the name, model, id or address of the TV to offer first at the checkout, e.g. `Philips`. |
| `JUKEBOX_DIR` | no | A different folder for the jukebox's songs. This one is read from real environment variables only, not from `.env`. |

`.env` is a plain `KEY=VALUE` file. `#` starts a comment, and quotes around values are optional:

```
PLEX_URL=https://plex.example.com
PLEX_TOKEN=xxxxxxxxxxxxxxxxxxxx
PLEX_PLAYER=Philips
```

**Where it's read from** ([PlexConfig.cs](Assets/Scripts/PlexBuster/Data/PlexConfig.cs)), in this order:

1. `.env` in the project root in the editor, or next to the `.exe` in a build.
2. `.env` in Unity's persistent data folder (`%USERPROFILE%\AppData\LocalLow\<company>\PlexBuster VR`).
3. Real environment variables, which override anything read from the file.

`.env` and `.env.*` are git-ignored. Only `.env.example` is committed. Never commit a token.
If one leaks, sign out of all devices in your Plex account settings to invalidate it.

**Getting the values:**

- **Token:** sign in to Plex Web, open any movie, choose *⋯ → Get Info → View XML*. The address of the page that
  opens ends in `X-Plex-Token=…`, and that value is your token.
- **URL:** any HTTPS address that reaches your server, for example a domain in front of Plex, or the server's own
  `https://…plex.direct:32400` address.

**Mock mode.** The `StoreServices` object in `Store.unity` has a *Source Mode* setting:

- **Auto** (default): uses Plex when `PLEX_URL` and `PLEX_TOKEN` are set, and falls back to the fake library if they aren't or Plex can't be reached.
- **Plex**: always uses Plex.
- **Mock**: always uses the fake library.

The Console says which one it picked (`[Store] Library source: …`). When it falls back, it also lists the paths it
checked for `.env`.

## Playing on your TV

Put a tape on the lobby's checkout counter and the till screen above it offers to play that title on a TV in your
home. A show carries on from the next episode, and a movie resumes where you left off.
This uses the Plex app on the TV as a remote-controlled player. It was tested with Plex for Android TV on a
Philips Android TV.

**One-time TV setup:**

1. **Install Plex and sign in.** Install the Plex app on the TV and sign in to **the same Plex account** as the token.
2. **Turn on "Advertise as player".** Open the Plex app's **Settings** and switch on **Advertise as player** (on
   Plex for Android TV it's in the *Advanced* section; the exact place varies between app versions). This lets the
   TV announce itself as a player that other apps can control. While it's off, the TV turns down every play command.
3. **Keep Plex open.** Leave the Plex app running on the TV. It only answers while it's open.

**At the checkout:**

- The till lists every TV it finds and pre-selects the one matching `PLEX_PLAYER`. Press *PLAY*.
- The TV takes about 5–10 seconds to start.
- If the list is empty, check the three setup steps above and press *SEARCH AGAIN*.

**How the TV is found** ([PlexRemotePlayback.cs](Assets/Scripts/PlexBuster/Data/PlexRemotePlayback.cs)):

- **Discovery.** Plex's usual LAN discovery (GDM) doesn't work with this TV: it doesn't answer, not even to the Plex
  server. So the store reads your account's device list from plex.tv and asks each device's address, directly on
  the LAN (port 32500), whether it's a player.
- **Stale "Remote" label.** plex.tv can keep listing a TV as only a "remote" for a while after you turn on
  *Advertise as player*. That doesn't matter, because every device is asked directly.
- **Play command.** The store creates a play queue on the server and tells the TV to play it. The TV streams
  straight from the server, using its HTTPS LAN address.
- **Firewall.** Only outgoing connections are used, so nothing needs opening in Windows Firewall, even on a
  network set to *Public*.
- **Same network.** The PC and the TV must be on the same network.
- **TV power.** Turning the TV on is not part of the app; use its remote, or a smart-home automation.

## Jukebox music

The lobby jukebox plays a shuffled pass through every `.mp3`, `.ogg`, `.wav` or `.aif(f)` in the `Music/` folder:

- **Where:** `Music/` in the project root, or next to the `.exe` in a build. Subfolders count.
- **Other folders:** set `JUKEBOX_DIR` to use a different folder.
- **Not in git:** the folder is git-ignored, so songs stay on your machine.
- **Labels:** the cabinet reads artist and title from file names like `Artist - Title.mp3`, so name files that way.
- **Adding songs:** new files are picked up at the start of the next pass through the playlist.

## What's in the store

**Lobby**
- New Releases shelves.
- The jukebox.
- An info desk: stand a tape in its slot and a hologram shows its details, including the trailer.
- A shopping basket on a stand.
- The checkout counter.
- Through the back, a corridor of department doors.

**Departments**
- **TV Room**: a living room with a big CRT television. Put a tape in the VCR to watch it.
- **Cinema**: stadium seating and a 12 m screen. Put a tape in the console by your seat. The house lights go off when the film starts.
- **My Watchlist**: a room with the titles on your Plex watchlist that are in your library.
- **One door per Plex library section** (Movies, TV Shows and the rest). Each leads to a hall with:
  - a door per genre;
  - an *All <section>* door;
  - a *Browse by…* board for decades, top actors and directors, and collections.

**Rooms** are generated for whatever you picked, and sized to fit it:
- Posters of the top-rated titles hang on the walls.
- A search terminal by the door filters the shelves as you type, and its top row of keys re-sorts them
  (A–Z, newest, oldest, top rated, recently added).
- A big red **RE-SHELVE ALL** button on the terminal's desk puts back every tape that was dropped or lost. The TV
  room and the cinema have the same button on the wall by their shelf.

**Tapes**
- Pick one up to read the back cover.
- Let go of a tape near its slot and it goes back on the shelf.
- Let go of a tape inside a shelf, wall or floor and it also goes straight back to its slot, so it can't get stuck.
- Drop tapes into the basket to carry them between rooms.

**Controls** (the XR Interaction Toolkit's standard rig):

| Control | Action |
|---|---|
| Left stick | Walk |
| Right stick, forward | Teleport |
| Right stick, left/right | Snap turn |
| Right stick, back | Turn around |
| Grip | Grab a tape or the basket |
| Trigger, while pointing | Press buttons and keys |
| Controller tip | Poke buttons and keys directly |

Walking out through a room's EXIT puts you back at the door you came in by.

## How it works

The code lives in `Assets/Scripts/PlexBuster/`:

- **`Data/`** talks to Plex. It has no scene objects.
  - `PlexClient`: HTTP requests. The token goes in a header, never in a URL or a log line.
  - `PlexLibrarySource`: sections, genres and other filters, the watchlist, and search queries.
  - `MockLibrarySource`: the fake library used in mock mode.
  - `PosterCache`: loads posters and keeps count of which tapes still use each one.
  - `PlexRemotePlayback`: plays titles on the TV.
  - `LocalStreamProxy`: relays video into the headset (see below).
- **`Store/`** builds the world at runtime.
  - `StoreServices`: picks Plex or the mock library at startup.
  - `StoreNavigator`: moving between places (fade out, build, teleport, fade in).
  - `RoomLayout` and `GeneratedRoom`: room size and shelf placement for a result, then stocking the shelves.
  - `ShelfUnit`, `VhsTape`, `TapeGrabInteractable`: the shelves and the tapes on them.
  - `CinemaRoom`, `TvRoom`, `ScreenPlayer`: the screening rooms and their video.
  - Plus the kiosks, terminals, basket, jukebox and checkout.
- **`Editor/`** holds one-off scene setup commands under the **PlexBuster** menu: Add Checkout To Counter, Add Info
  Station and Basket, Add Lobby Jukebox, Set Up TV Room and Cinema, Generate Store Textures. They're already
  applied to `Store.unity`. Run one again only to rebuild that part.

**Places are built on the fly.**
- The lobby is the only hand-made part of the scene.
- Halls and rooms are built when you walk through a door. They're placed far from each other and destroyed when you leave.
- Places stack up as lobby → hall → room, and walking out of one returns you to the door you came through.
- Everything they're made of comes from one asset, `Assets/Settings/StoreTheme.asset`: materials, the tape prefab, light levels and more.

**Big rooms load in pieces.**
- *All Movies* is about 2,800 tapes.
- Shelves nearest the entrance are stocked first, so what you see fills in before the far corners.
- Stocking shelves and preparing posters share a small per-frame time budget, so the frame rate stays steady while a room fills.
- Library listings are parsed off the main thread.
- Poster downloads, poster files from the disk cache and API calls queue separately, so a room full of posters never delays pressing Play.

**Caches.** Everything is cached on disk under Unity's temporary cache folder
(`%LOCALAPPDATA%\Temp\<company>\PlexBuster VR\plex\<server>\`):
- **`responses/`**: library listings, reused for 12 hours. They're also used when the server can't be reached.
- **`posters/`**: each poster as a JPEG, plus a `.tex` file holding the finished GPU texture. The `.tex` loads
  much faster than decoding and compressing the JPEG again. A full library takes a few hundred MB.

Delete the folder to start fresh.

**Watching in VR** (TV room, cinema):
- **Relay.** Unity's VideoPlayer can't send the token in a header, and it logs the URLs it plays. So it plays from a
  small relay on `127.0.0.1`, which adds the token to the real request.
- **Direct play vs conversion.** MP4 files with 8-bit H.264 video and AAC audio play directly. Everything else is
  converted by the Plex server into MKV while you watch.
- **Subtitles.** External text subtitle files are drawn by the store. Subtitles embedded in the video file, or
  picture-based ones, are burned into the picture by Plex.
- **Progress.** Your position is reported to Plex, so a title resumes anywhere: in the store, on the TV or in any other Plex app.

## Tuning

These are all on `Assets/Settings/StoreTheme.asset`:

| Setting | Default | What it changes |
|---|---|---|
| *Cinema House Lights* | 1 | How bright the cinema is before the film. Takes effect the next time you enter the cinema. |
| *Load Budget Ms* | 2 | Main-thread milliseconds per frame for stocking shelves and preparing posters. Higher fills big rooms faster; lower is smoother. |
| *Light Intensity / Range / Spacing* | | Ceiling lights in generated rooms. |

## Troubleshooting

| Symptom | Check |
|---|---|
| The store has made-up titles | `.env` wasn't found or is incomplete. The Console lists the paths it checked. |
| `HTTP 401` in the Console | The token is wrong or has expired. Get a new one. |
| `Insecure connection not allowed` in the Console | `PLEX_URL` starts with `http://`. Use an `https://` address. |
| New additions don't show up | Listings are cached for 12 hours. Delete the `responses` cache folder. |
| The checkout says "No TVs found" | Plex is open on the TV, *Advertise as player* is on, it's signed in to the same account, and it's on the same network. |
| A converted film stops with "Not enough CPU for conversion" | Other streams are still converting on the server (check *Now Playing* in Plex). Wait a minute, or stop the streams you started. |
| Converted video plays at half frame rate | The relay should fix Plex's MKV marking progressive video as interlaced (`MatroskaHeaderFix`). If it happens, look there first. |
| A tape fell out of reach | Press the red RE-SHELVE button in that room. |
