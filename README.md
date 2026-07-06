# Sc2Otter 🦦

Sc2Otter is a lightweight, cloud-synced StarCraft 2 scouting companion. 

By running the Local Client on your PC, you can automatically capture Live Game information on your opponents before the match starts, and automatically run a Python-based Replay Analyzer in the background every time a match finishes to extract builds, units made, and assign intelligent tags to your opponents.

All this data is pushed securely to your Sc2Otter Web App so you can review it from anywhere!

## Installation (Local Client)

The Local Client is a lightweight standalone application that runs silently in the background while you play.

### Setup

1. **Download the latest release**: Go to the [Releases](https://github.com/Killerwalski/Sc2Otter/releases) page and download `sc2otter.exe`.
2. Place the `.exe` file anywhere on your computer (e.g., inside a new `Sc2Otter` folder).
3. Open a terminal (PowerShell or Command Prompt) in that folder.

### Connecting to Your Web App

Start Sc2Otter and link it to your Web App by running:
```bash
.\sc2otter.exe --sync-key YOUR_SYNC_KEY_HERE
```
*(You can find your Sync Key on the **Configuration** page of your Sc2Otter Web App).*

Once connected, Sc2Otter will run quietly in the background. It will automatically detect when a match starts and ends, seamlessly syncing data to your cloud database!

### Updating

When a new version is released, simply replace your old `sc2otter.exe` file with the newly downloaded one.
