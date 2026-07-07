import urllib.request
import time
import sys
import json

url = "https://sc2otter-production.up.railway.app/api/debug-db"

for _ in range(30):
    try:
        req = urllib.request.Request(url)
        with urllib.request.urlopen(req) as response:
            if response.status == 200:
                print(response.read().decode('utf-8'))
                sys.exit(0)
    except urllib.error.HTTPError as e:
        print(f"Status: {e.code}")
    except Exception as e:
        pass
    time.sleep(5)
