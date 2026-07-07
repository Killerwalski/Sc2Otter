import sc2reader
import sys
import glob

replays = glob.glob(r"C:\Users\pakow\Documents\StarCraft II\Accounts\**\10000 Feet LE (100).SC2Replay", recursive=True)
if not replays: sys.exit(1)

path = replays[0]
replay = sc2reader.load_replay(path, load_level=3)
for player in replay.players:
    if not player.is_human or player.play_race != 'Terran': continue
    print(f"Player: {player.name} (Terran)")
    for event in replay.tracker_events:
        if getattr(event, 'unit', None) and event.unit.name in ['CommandCenter', 'OrbitalCommand', 'PlanetaryFortress'] and getattr(event, 'control_pid', None) == player.pid:
            print(f"  [{event.second // 60:02d}:{event.second % 60:02d}] {event.name} - {event.unit.name}")
