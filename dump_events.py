import sys
import sc2reader

def dump(path):
    print("Loading", path)
    replay = sc2reader.load_replay(path, load_level=3)
    for p in replay.players:
        if p.name == "Toneman" or p.name == "Cream" or p.name == "Weekend":
            print(f"Terran player: {p.name}")
            for e in replay.tracker_events:
                if e.name == 'UnitInitEvent' and getattr(e, 'control_pid', None) == p.pid and e.second < 200:
                    print(f"[{p.name}] [{e.second // 60}:{e.second % 60:02d}] UnitInitEvent: {e.unit.name}")

if __name__ == "__main__":
    import glob
    replays = glob.glob(r"C:\Users\pakow\Documents\StarCraft II\Accounts\57638796\1-S2-1-498796\Replays\Multiplayer\10000*.SC2Replay", recursive=True)
    for r in replays[:3]:
        dump(r)
