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
            
            first_prod_time = 9999
            first_exp_time = 9999
            
            mutalisk_count = 0
            factory_units = 0
            bio_units = 0
            reaper_count = 0
            bc_count = 0
            warp_prism_count = 0
            nydus_count = 0
            dark_shrine_time = 9999
            bc_first_time = 9999
            
            for event in replay.tracker_events:
                if event.name == 'UnitInitEvent' and event.control_pid == player.pid:
                    unit_name = event.unit.name
                    time_sec = event.second
                    
                    if unit_name in ['SpawningPool', 'Gateway', 'Barracks'] and time_sec < first_prod_time:
                        first_prod_time = time_sec
                        
                    if unit_name in ['Hatchery', 'Nexus', 'CommandCenter'] and time_sec < first_exp_time:
                        first_exp_time = time_sec
                    
                    # 1. Fast Pool Detection (Zerg)
                    # 14 pool is usually started around 1:00-1:05
                    # Standard 16 or 17 pool is around 1:15-1:20
                    if player.play_race == 'Zerg' and unit_name == 'SpawningPool' and time_sec < 70:
                        fast_pool_detected = True
                        player_result["tags"].append("Cheese")
                        player_result["tags"].append("Fast Pool")
                        player_result["notes"].append(f"Built Spawning Pool very early ({time_sec//60}:{time_sec%60:02d})")
                        
                    # 2. Proxy Detection (Terran / Protoss)
                    # Check distance from main base for early production structures
                    if starting_loc and unit_name in ['Barracks', 'Gateway', 'Factory', 'Starport', 'RoboticsFacility', 'Stargate']:
                        # Only care about early game proxies (first 6 minutes)
                        if time_sec < 360:
                            dist = math.hypot(event.location[0] - starting_loc[0], event.location[1] - starting_loc[1])
                            if dist > 55: # Usually Natural is ~25 away, 3rd is ~40 away. >55 is proxy territory.
                                proxy_detected = True
                                tag_name = f"Proxy {unit_name.replace('Barracks', 'Rax').replace('Gateway', 'Gate').replace('RoboticsFacility', 'Robo')}"
                                if tag_name not in player_result["tags"]:
                                    player_result["tags"].append("Cheese")
                                    player_result["tags"].append(tag_name)
                                    player_result["notes"].append(f"Proxied {unit_name} at {time_sec//60}:{time_sec%60:02d}")
                elif event.name == 'UnitBornEvent' and event.control_pid == player.pid:
                    unit_name = event.unit.name
                    time_sec = event.second
                    
                    if unit_name == 'Mutalisk':
                        mutalisk_count += 1
                    elif unit_name in ['Marine', 'Marauder']:
                        bio_units += 1
                    elif unit_name in ['Hellion', 'Hellbat', 'Cyclone', 'SiegeTank', 'Thor', 'WidowMine']:
                        factory_units += 1
                    elif unit_name == 'Reaper':
                        reaper_count += 1
                    elif unit_name == 'WarpPrism':
                        warp_prism_count += 1
                    elif unit_name == 'Battlecruiser':
                        bc_count += 1
                        if time_sec < bc_first_time:
                            bc_first_time = time_sec
                elif event.name == 'UnitInitEvent' and event.control_pid == player.pid:
                    unit_name = event.unit.name
                    time_sec = event.second
                    
                    if unit_name == 'NydusNetwork':
                        nydus_count += 1
                    elif unit_name == 'DarkShrine':
                        if time_sec < dark_shrine_time:
                            dark_shrine_time = time_sec
                                    
            # 3. Expansion First Detection
            if first_exp_time < first_prod_time and first_exp_time < 300:
                if player.play_race == 'Zerg':
                    if "Hatchery First" not in player_result["tags"]:
                        player_result["tags"].append("Hatchery First")
                        player_result["notes"].append(f"Built Hatchery before Spawning Pool ({first_exp_time//60}:{first_exp_time%60:02d})")
                elif player.play_race == 'Protoss':
                    if "Nexus First" not in player_result["tags"]:
                        player_result["tags"].append("Nexus First")
                        player_result["notes"].append(f"Built Nexus before Gateway ({first_exp_time//60}:{first_exp_time%60:02d})")
                elif player.play_race == 'Terran':
                    if "CC First" not in player_result["tags"]:
                        player_result["tags"].append("CC First")
                        player_result["notes"].append(f"Built Command Center before Barracks ({first_exp_time//60}:{first_exp_time%60:02d})")
                        
            # 4. Unit Composition / Other Tags
            if mutalisk_count > 8:
                player_result["tags"].append("Mutas")
            if reaper_count > 1:
                player_result["tags"].append("Multi reaper")
            if warp_prism_count > 0:
                player_result["tags"].append("Warp prism")
            if nydus_count > 0:
                player_result["tags"].append("Nydus Network")
            if bc_count > 1:
                player_result["tags"].append("Battlecruiser")
            if bc_first_time < 420: # 7 minutes
                player_result["tags"].append("Fast BCs")
            if dark_shrine_time < 420: # 7 minutes
                player_result["tags"].append("Fast DTs")
                
            if player.play_race == 'Terran':
                if factory_units > (bio_units + 5) and factory_units > 10:
                    player_result["tags"].append("Mech")
                elif bio_units > (factory_units * 2) and bio_units > 15:
                    player_result["tags"].append("Bio player")
                                    
            results.append(player_result)
            
        total_players = len(players)
        if total_players == 2:
            game_mode = "1v1"
        elif total_players == 4:
            game_mode = "2v2"
        elif total_players == 6:
            game_mode = "3v3"
        elif total_players == 8:
            game_mode = "4v4"
        else:
            game_mode = f"{total_players}p"
            
        print(json.dumps({
            "success": True, 
            "mapName": replay.map_name,
            "startTime": replay.start_time.isoformat(),
            "gameMode": game_mode,
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
