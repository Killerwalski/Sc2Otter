import sc2reader

rep = sc2reader.load_replay(r"C:\Users\pakow\Documents\StarCraft II\Accounts\57638796\1-S2-1-498796\Replays\Multiplayer\10000 Feet LE (10).SC2Replay", load_level=3)

for p in rep.players:
    stats = [e for e in rep.tracker_events if e.name == 'PlayerStatsEvent' and e.pid == p.pid]
    if stats:
        last = stats[-1]
        print(f"{p.name} - MinLost: {getattr(last, 'minerals_lost', 0)}, GasLost: {getattr(last, 'vespene_lost', 0)}")
