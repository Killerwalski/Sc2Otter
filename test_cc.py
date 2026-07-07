import sc2reader
import sys

# Get the first replay file
import glob
replays = glob.glob(r"C:\Users\pakow\Documents\StarCraft II\Accounts\**\*.SC2Replay", recursive=True)

if not replays:
    print("No replays found")
    sys.exit(1)

for path in replays[:5]:
    try:
        print(f"Parsing {path}")
        replay = sc2reader.load_replay(path, load_level=3)
        for player in replay.players:
            if not player.is_human or player.play_race != 'Terran': continue
            
            print(f"Player: {player.name} (Terran)")
            for event in replay.tracker_events:
                if getattr(event, 'unit', None) and event.unit.name == 'CommandCenter' and getattr(event, 'control_pid', None) == player.pid:
                    print(f"  [{event.second // 60:02d}:{event.second % 60:02d}] {event.name} - {event.unit.name}")
                    
    except Exception as e:
        print(f"Error parsing: {e}")
