import sys
import json
import math
import sc2reader

def analyze_replay(replay_path, my_name=None):
    try:
        # Load replay with tracker events
        replay = sc2reader.load_replay(replay_path, load_level=4)
        
        if replay.length.seconds < 240:
            print(json.dumps({"success": True, "skipped": True, "message": "Game too short"}))
            return
            
        results = []
        
        # Determine players (filter out computers if needed, but keeping simple for now)
        players = [p for p in replay.players if p.is_human]
            
        for player in players:
            player_result = {
                "name": player.name,
                "race": player.play_race,
                "result": player.result,
                "tags": [],
                "notes": []
            }
            
            # Find starting location
            starting_loc = None
            for event in replay.tracker_events:
                if event.name == 'UnitBornEvent' and event.control_pid == player.pid and event.second == 0:
                    if event.unit.name in ['CommandCenter', 'Nexus', 'Hatchery']:
                        starting_loc = event.location
                        break
                        
            # Analyze early build orders
            fast_pool_detected = False
            proxy_detected = False
            
            for event in replay.tracker_events:
                if event.name == 'UnitInitEvent' and event.control_pid == player.pid:
                    unit_name = event.unit.name
                    time_sec = event.second
                    
                    # 1. Fast Pool Detection (Zerg)
                    # 14 pool is usually started around 1:00-1:05
                    # Standard 16 or 17 pool is around 1:15-1:20
                    if unit_name == 'SpawningPool' and time_sec < 70:
                        fast_pool_detected = True
                        player_result["tags"].append("Cheese")
                        player_result["tags"].append("Fast Pool")
                        player_result["notes"].append(f"Built Spawning Pool very early ({time_sec//60}:{time_sec%60:02d})")
                        
                    # 2. Proxy Detection (Terran / Protoss)
                    # Check distance from main base for early production structures
                    if starting_loc and unit_name in ['Barracks', 'Gateway', 'Factory', 'Starport', 'RoboticsFacility', 'Stargate', 'Hatchery']:
                        # Don't consider hatcheries proxy unless they are VERY far, but standard is Proxy Rax/Gate
                        if unit_name == 'Hatchery': continue
                            
                        # Only care about early game proxies (first 4 minutes)
                        if time_sec < 240:
                            dist = math.hypot(event.location[0] - starting_loc[0], event.location[1] - starting_loc[1])
                            if dist > 55: # Usually Natural is ~25 away, 3rd is ~40 away. >55 is proxy territory.
                                proxy_detected = True
                                tag_name = f"Proxy {unit_name.replace('Barracks', 'Rax').replace('Gateway', 'Gate').replace('RoboticsFacility', 'Robo')}"
                                if tag_name not in player_result["tags"]:
                                    player_result["tags"].append("Cheese")
                                    player_result["tags"].append(tag_name)
                                    player_result["notes"].append(f"Proxied {unit_name} at {time_sec//60}:{time_sec%60:02d}")
                                    
            results.append(player_result)
            
        print(json.dumps({
            "success": True, 
            "map_name": replay.map_name,
            "start_time": replay.start_time.isoformat(),
            "data": results
        }))
        
    except Exception as e:
        print(json.dumps({"success": False, "error": str(e)}))
        sys.exit(1)

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(json.dumps({"success": False, "error": "Usage: python replay_analyzer.py <replay_path>"}))
        sys.exit(1)
        
    replay_path = sys.argv[1]
    analyze_replay(replay_path)
