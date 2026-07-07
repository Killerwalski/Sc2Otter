import os
import sc2reader
from datetime import datetime, timezone, timedelta

filepath = r"C:\Users\pakow\Documents\StarCraft II\Accounts\57638796\1-S2-1-498796\Replays\Multiplayer\Old Sun Temple LE (22).SC2Replay"
replay = sc2reader.load_replay(filepath, load_level=2)
print("sc2reader start time:", replay.start_time.isoformat())

mtime = os.path.getmtime(filepath)
dt = datetime.fromtimestamp(mtime, tz=timezone.utc)
start_dt = dt - timedelta(seconds=replay.length.seconds)
print("file mtime start time:", start_dt.isoformat())
