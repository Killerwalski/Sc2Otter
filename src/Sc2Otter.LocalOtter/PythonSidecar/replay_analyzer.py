import sys
import json
import math
import os
from datetime import datetime, timezone, timedelta
import sc2reader

def analyze_replay(replay_path, my_name=None):
    try:
        # Load replay with tracker events (level 3) - skips game events (level 4) for massive speedup
        replay = sc2reader.load_replay(replay_path, load_level=3)
        
        if replay.length.seconds < 180:
            return {"success": True, "skipped": True, "message": "Game too short"}
            
        results = []
        
        # Determine players (filter out computers if needed, but keeping simple for now)
        players = [p for p in replay.players if p.is_human]
            
        for player in players:
            player_result = {
                "name": player.name,
                "toonHandle": getattr(player, 'toon_handle', None),
                "race": player.play_race,
                "result": player.result,
                "teamId": getattr(player, 'team_id', 0),
                "tags": [],
                "notes": [],
                "unitsMade": {},
                "stats": {
                    "workersCreated": 0,
                    "supplyBlockTime": 0,
                    "avgUnspentMinerals": 0,
                    "avgMineralIncome": 0,
                },
                "telemetry": {
                    "economy": [],
                    "armyLost": []
                }
            }
            
            # Check if they picked random
            if hasattr(player, 'pick_race') and player.pick_race == 'Random':
                player_result["tags"].append("Random")
                
            # Count units
            excludes = {
                'Overlord', 'Overseer', 'OverlordTransport', 
                'Larva', 'Egg', 'Broodling', 'BanelingCocoon', 'RavagerCocoon', 'OverlordCocoon', 'LurkerMPEgg',
                'LocustMP', 'LocustMPFlying', 'Interceptor', 'AutoTurret',
                'MULE', 'PointDefenseDrone', 'Changeling', 'ChangelingMarine', 'ChangelingMarineShield', 
                'ChangelingZealot', 'ChangelingZergling', 'ChangelingZerglingWings',
                'BeaconArmy', 'BeaconDefend', 'BeaconAttack', 'BeaconHarass', 'BeaconIdle', 'BeaconAuto', 
                'BeaconDetect', 'BeaconScout', 'BeaconClaim', 'BeaconExpand', 'BeaconRally', 
                'BeaconCustom1', 'BeaconCustom2', 'BeaconCustom3', 'BeaconCustom4',
                'CreepTumor', 'CreepTumorBurrowed', 'CreepTumorQueen', 'OracleStasisTrap',
                'KD8Charge', 'ParasiticBombDummy', 'AdeptPhaseShift', 'DisruptorPhased',
                'InvisibleTargetDummy'
            }
            for unit in getattr(player, 'units', []):
                if getattr(unit, 'hallucinated', False) or getattr(unit, 'is_building', False) or not unit.name:
                    continue
                
                name = unit.name
                if name.endswith('Burrowed'): name = name[:-8]
                elif name == 'SiegeTankSieged': name = 'SiegeTank'
                elif name == 'LiberatorAG': name = 'Liberator'
                elif name == 'ThorAP': name = 'Thor'
                elif name == 'VikingAssault': name = 'VikingFighter'
                elif name == 'BattleHellion': name = 'Hellbat'
                
                if name in excludes:
                    continue
                player_result["unitsMade"][name] = player_result["unitsMade"].get(name, 0) + 1
            
            # Find starting location
            starting_loc = None
            for event in replay.tracker_events:
                if event.name == 'UnitBornEvent' and event.control_pid == player.pid and event.second == 0:
                    if event.unit.name in ['CommandCenter', 'Nexus', 'Hatchery', 'OrbitalCommand', 'PlanetaryFortress', 'Lair', 'Hive']:
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
            
            cc_count = 1
            refinery_count = 0
            
            hatchery_count = 1
            
            current_food = 0
            
            for event in replay.tracker_events:
                if getattr(event, 'name', '') == 'PlayerStatsEvent' and getattr(event, 'pid', None) == player.pid:
                    current_food = event.food_used
                    
                elif event.name == 'UnitInitEvent' and event.control_pid == player.pid:
                    unit_name = getattr(event, 'unit_type_name', event.unit.name)
                    time_sec = event.second
                    
                    if unit_name in ['SpawningPool', 'Gateway', 'Barracks', 'Forge', 'EngineeringBay'] and time_sec < first_prod_time:
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
                    if starting_loc and unit_name in ['Barracks', 'Gateway', 'Factory', 'Starport', 'RoboticsFacility', 'Stargate', 'Forge', 'PhotonCannon']:
                        # Only care about early game proxies (first 6 minutes)
                        if time_sec < 360:
                            dist = math.hypot(event.location[0] - starting_loc[0], event.location[1] - starting_loc[1])
                            if dist > 55: # Usually Natural is ~25 away, 3rd is ~40 away. >55 is proxy territory.
                                proxy_detected = True
                                tag_name = f"Proxy {unit_name.replace('Barracks', 'Rax').replace('Gateway', 'Gate').replace('RoboticsFacility', 'Robo').replace('PhotonCannon', 'Cannon')}"
                                if unit_name == 'PhotonCannon' and "Cannon Rusher" not in player_result["tags"]:
                                    player_result["tags"].append("Cheese")
                                    player_result["tags"].append("Cannon Rusher")
                                    player_result["notes"].append(f"Cannon rushed at {time_sec//60}:{time_sec%60:02d}")
                                elif unit_name != 'PhotonCannon' and tag_name not in player_result["tags"]:
                                    player_result["tags"].append("Cheese")
                                    player_result["tags"].append(tag_name)
                                    player_result["notes"].append(f"Proxied {unit_name} at {time_sec//60}:{time_sec%60:02d}")
                                    
                    if player.play_race == 'Terran':
                        if unit_name == 'CommandCenter':
                            cc_count += 1
                            if cc_count == 3 and refinery_count < 2 and time_sec < 420:
                                if "Fast 3 CC" not in player_result["tags"]:
                                    player_result["tags"].append("Fast 3 CC")
                                    player_result["notes"].append(f"Built 3rd CC before 2nd Refinery ({time_sec//60}:{time_sec%60:02d})")
                        elif unit_name == 'Refinery':
                            refinery_count += 1
                    elif player.play_race == 'Zerg':
                        if unit_name == 'Hatchery':
                            hatchery_count += 1
                            if hatchery_count == 3 and current_food < 36 and time_sec < 420:
                                if "Fast 3 Hatchery" not in player_result["tags"]:
                                    player_result["tags"].append("Fast 3 Hatchery")
                                    player_result["notes"].append(f"Built 3rd Hatchery very fast at {current_food} supply ({time_sec//60}:{time_sec%60:02d})")
                                    
                    if unit_name == 'NydusNetwork':
                        nydus_count += 1
                    elif unit_name == 'DarkShrine':
                        if time_sec < dark_shrine_time:
                            dark_shrine_time = time_sec
                            
                elif event.name == 'UnitBornEvent' and event.control_pid == player.pid:
                    unit_name = getattr(event, 'unit_type_name', event.unit.name)
                    time_sec = event.second
                    
                    if unit_name == 'Mutalisk':
                        mutalisk_count += 1
                    elif unit_name in ['Marine', 'Marauder']:
                        bio_units += 1
                    elif unit_name in ['Hellion', 'Hellbat', 'Cyclone', 'SiegeTank', 'Thor', 'WidowMine']:
                        factory_units += 1
                    elif unit_name == 'Reaper':
                        if time_sec < 300: # Only count reapers in the first 5 minutes for the tag
                            reaper_count += 1
                    elif unit_name == 'WarpPrism':
                        warp_prism_count += 1
                    elif unit_name == 'Battlecruiser':
                        bc_count += 1
                        if time_sec < bc_first_time:
                            bc_first_time = time_sec
                    elif unit_name in ['SCV', 'Probe', 'Drone']:
                        player_result["stats"]["workersCreated"] += 1
                        
                elif event.name == 'UnitDiedEvent' and event.unit.owner and event.unit.owner.pid == player.pid:
                    if event.unit.name not in ['SCV', 'Probe', 'Drone', 'Overlord', 'OverlordTransport', 'MULE', 'Broodling', 'Larva', 'Egg', 'CreepTumor', 'AutoTurret']:
                        if not getattr(event.unit, 'is_building', False):
                            minute = event.second // 60
                            # Find or create minute bucket
                            bucket = next((b for b in player_result["telemetry"]["armyLost"] if b["m"] == minute), None)
                            if bucket:
                                bucket["c"] += 1
                            else:
                                player_result["telemetry"]["armyLost"].append({"m": minute, "c": 1})
                        
            # Analyze player stats for supply block and unspent minerals
            stats_events = [e for e in replay.tracker_events if e.name == 'PlayerStatsEvent' and getattr(e, 'pid', None) == player.pid]
            if stats_events:
                blocked_events = [e for e in stats_events if e.food_used >= e.food_made and e.food_made > 0]
                player_result["stats"]["supplyBlockTime"] = len(blocked_events) * 10
                player_result["stats"]["avgUnspentMinerals"] = int(sum(e.minerals_current for e in stats_events) / len(stats_events))
                player_result["stats"]["avgMineralIncome"] = int(sum(e.minerals_collection_rate for e in stats_events) / len(stats_events))
                
                # Telemetry extraction
                for e in stats_events:
                    if e.second % 60 < 15: # Take roughly 1 sample per minute (stats events occur every 10s)
                        minute = e.second // 60
                        if not any(t['m'] == minute for t in player_result["telemetry"]["economy"]):
                            player_result["telemetry"]["economy"].append({
                                "m": minute,
                                "w": getattr(e, 'workers_active_count', 0),
                                "s": getattr(e, 'food_used', 0),
                                "min": getattr(e, 'minerals_collection_rate', 0),
                                "gas": getattr(e, 'vespene_collection_rate', 0)
                            })
                                    
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
                        
            # 3b. One Baser Detection
            if first_exp_time > 240:
                if "One baser" not in player_result["tags"]:
                    player_result["tags"].append("One baser")
                    if first_exp_time < 9999:
                        unit_str = "Hatchery" if player.play_race == 'Zerg' else ("Nexus" if player.play_race == 'Protoss' else "Command Center")
                        player_result["notes"].append(f"Built first {unit_str} at {first_exp_time//60}:{first_exp_time%60:02d}")
                    else:
                        player_result["notes"].append("Did not build an expansion")
                        
            # 3c. Forge Expand
            if player.play_race == 'Protoss' and 'Forge' in player_result["unitsMade"]:
                forge_time = next((e.second for e in replay.tracker_events if e.name == 'UnitInitEvent' and e.control_pid == player.pid and getattr(e, 'unit_type_name', e.unit.name) == 'Forge'), 9999)
                if forge_time < 150: # Forge before 2:30
                    if "Forge Expand" not in player_result["tags"]:
                        player_result["tags"].append("Forge Expand")
                        player_result["notes"].append(f"Built early Forge ({forge_time//60}:{forge_time%60:02d})")
                        
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
                    
            # 5. Mass Unit Tags
            units = player_result["unitsMade"]
            # Zerg
            if units.get('Queen', 0) >= 8: player_result["tags"].append("Mass Queen")
            if units.get('Ravager', 0) >= 12: player_result["tags"].append("Mass Ravager")
            if units.get('LurkerMP', 0) + units.get('Lurker', 0) >= 6: player_result["tags"].append("Mass Lurker")
            if units.get('SwarmHostMP', 0) + units.get('SwarmHost', 0) >= 8: player_result["tags"].append("Mass Swarm Host")
            if units.get('Infestor', 0) >= 6: player_result["tags"].append("Mass Infestor")
            if units.get('Ultralisk', 0) >= 5: player_result["tags"].append("Mass Ultralisk")
            
            # Protoss
            if units.get('Adept', 0) >= 8: player_result["tags"].append("Mass Adept")
            if units.get('VoidRay', 0) >= 6: player_result["tags"].append("Mass Void Ray")
            if units.get('Oracle', 0) >= 5: player_result["tags"].append("Mass Oracle")
            if units.get('Carrier', 0) >= 5: player_result["tags"].append("Mass Carrier")
            if units.get('Tempest', 0) >= 6: player_result["tags"].append("Mass Tempest")
            if units.get('Archon', 0) >= 10: player_result["tags"].append("Mass Archon")
            
            # Terran
            if units.get('Hellion', 0) + units.get('Hellbat', 0) >= 15: player_result["tags"].append("Mass Hellion")
            if units.get('WidowMine', 0) >= 10: player_result["tags"].append("Mass Widow Mine")
            if units.get('Cyclone', 0) >= 8: player_result["tags"].append("Mass Cyclone")
            if units.get('Liberator', 0) >= 7: player_result["tags"].append("Mass Liberator")
            if units.get('Banshee', 0) >= 5: player_result["tags"].append("Mass Banshee")
            if units.get('Ghost', 0) >= 8: player_result["tags"].append("Mass Ghost")
                                    
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
            game_mode = None
            
        # sc2reader start_time has a bug with timezone offsets on some platforms.
        # replay.date gives us the UTC end time of the match
        if hasattr(replay, 'date') and replay.date:
            dt = replay.date.replace(tzinfo=timezone.utc)
            start_dt = dt - timedelta(seconds=replay.length.seconds)
        else:
            # Fallback to file modification time
            mtime = os.path.getmtime(replay_path)
            dt = datetime.fromtimestamp(mtime, tz=timezone.utc)
            start_dt = dt - timedelta(seconds=replay.length.seconds)
            
        result_json = {
            "success": True, 
            "mapName": replay.map_name,
            "startTime": start_dt.isoformat(),
            "gameMode": game_mode,
            "data": results
        }
        return result_json
        
    except Exception as e:
        return {"success": False, "error": str(e)}

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(json.dumps({"success": False, "error": "Usage: python replay_analyzer.py <replay_path> [--daemon]"}))
        sys.exit(1)
        
    if "--daemon" in sys.argv:
        # Run in daemon mode, reading paths from stdin
        for line in sys.stdin:
            path = line.strip()
            if not path:
                continue
            if path.lower() == "exit":
                break
                
            res = analyze_replay(path)
            print(json.dumps(res), flush=True)
    else:
        # Single file mode
        replay_path = sys.argv[1]
        res = analyze_replay(replay_path)
        print(json.dumps(res))
