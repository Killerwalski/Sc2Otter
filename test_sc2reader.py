import time
import sc2reader

def test_parse(path, level):
    start = time.time()
    try:
        replay = sc2reader.load_replay(path, load_level=level)
        print(f"Level {level}: {time.time() - start:.3f}s")
    except Exception as e:
        print(f"Level {level} error: {e}")

if __name__ == "__main__":
    # find a replay
    import glob
    replays = glob.glob(r"C:\Code\Antigravity\Sc2Otter\**\*.SC2Replay", recursive=True)
    if replays:
        path = replays[0]
        print("Testing with:", path)
        test_parse(path, 4)
        test_parse(path, 3)
        test_parse(path, 2)
