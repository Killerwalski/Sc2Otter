# Sc2Otter 🦦

Sc2Otter is a lightweight, cloud-synced StarCraft 2 scouting companion. 

By running the Local Client on your PC, you can automatically capture Live Game information on your opponents before the match starts, and automatically run a Python-based Replay Analyzer in the background every time a match finishes to extract builds, units made, and assign intelligent tags to your opponents.

All this data is pushed securely to your Sc2Otter Web App so you can review it from anywhere!

## Installation (Local Client)

The Local Client is a `.NET` Global Tool that runs in the background while you play.

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Python 3.9+ (with `sc2reader` installed: `pip install sc2reader`)
- StarCraft 2 installed on your PC

### Setup

1. **Clone the repository** (or download as a ZIP):
   ```bash
   git clone https://github.com/Killerwalski/Sc2Otter.git
   cd Sc2Otter/src/Sc2Otter.LocalClient
   ```

2. **Build and Pack the Tool**:
   ```bash
   dotnet pack -c Release
   ```

3. **Install as a Global Tool**:
   ```bash
   dotnet tool install -g --add-source ./nupkg Sc2Otter.LocalClient
   ```

### Connecting to Your Web App

Once installed, you can start Sc2Otter from anywhere in your terminal by running:
```bash
sc2otter --sync-key YOUR_SYNC_KEY_HERE
```
*(You can find your Sync Key on the **Settings** page of your Sc2Otter Web App).*

Once connected, Sc2Otter will run quietly in the background. It will automatically detect when a match starts and ends, seamlessly syncing data to your cloud database!

### Updating

When a new version is released, simply pull the latest code, repack it, and run the update command:
```bash
git pull
dotnet pack -c Release
dotnet tool update -g --add-source ./nupkg Sc2Otter.LocalClient
```
