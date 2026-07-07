import sc2reader
import time
for level in [4, 3]:
    start = time.time()
    replay = sc2reader.load_replay("test.SC2Replay", load_level=level)
    print(f"Level {level}: {time.time() - start:.3f}s - Units: {len(getattr(replay.players[0], 'units', []))}")
