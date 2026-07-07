import os
import sc2reader

def find_bobby_replay():
    replays_dir = r"C:\Users\pakow\Documents\StarCraft II\Accounts\57638796\1-S2-1-498796\Replays\Multiplayer"
    for filename in os.listdir(replays_dir):
        if not filename.endswith(".SC2Replay"):
            continue
            
        filepath = os.path.join(replays_dir, filename)
        try:
            replay = sc2reader.load_replay(filepath, load_level=2)
            for p in replay.players:
                if p.name == "bobby" and p.play_race == "Terran":
                    print(f"Found bobby (Terran) in {filepath}")
                    replay = sc2reader.load_replay(filepath, load_level=4)
                    player = next(p for p in replay.players if p.name == "bobby" and p.play_race == "Terran")
                    
                    print(f"Player: {player.name} ({player.play_race})")
                    for event in replay.events:
                        if hasattr(event, 'unit_controller') and event.unit_controller == player:
                            if event.name == "UnitBornEvent" and event.unit.name in ["SCV", "MULE"]:
                                continue
                            if event.name in ["UnitInitEvent", "UnitBornEvent"]:
                                time_sec = event.second
                                print(f"[{time_sec//60}:{time_sec%60:02d}] {event.name}: {event.unit.name} (type: {event.unit._type_class.name})")
                    return
        except Exception as e:
            pass

find_bobby_replay()
