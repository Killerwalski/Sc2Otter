import os
import sc2reader

replays_dir = r"C:\Users\pakow\Documents\StarCraft II\Accounts\57638796\1-S2-1-498796\Replays\Multiplayer"
filepath = os.path.join(replays_dir, "Old Sun Temple LE (22).SC2Replay")
replay = sc2reader.load_replay(filepath, load_level=4)
player = next(p for p in replay.players if p.name == "bobby" and p.play_race == "Terran")

for event in replay.events:
    if hasattr(event, 'unit_controller') and event.unit_controller == player:
        if event.name in ["UnitInitEvent", "UnitBornEvent"]:
            if "Command" in event.unit.name or "Planetary" in event.unit.name:
                print(f"[{event.second//60}:{event.second%60:02d}] {event.name}: {event.unit.name} (Unit ID: {event.unit_id})")
                print(f"Location: {getattr(event, 'location', 'None')} / Unit location: {getattr(event.unit, 'location', 'None')}")
