"""Refined turn timing: piecewise linear interpolation between observed datapoints."""
import json
import sys
import time
import urllib.request

BACKEND="http://localhost:5287"; CLIENT="cal"; MODE="real-robot"
def post(p,d):
    r=urllib.request.Request(f"{BACKEND}{p}",data=json.dumps(d).encode(),
      headers={"Content-Type":"application/json"},method="POST")
    return json.loads(urllib.request.urlopen(r,timeout=3).read())
def cmd(c): return post("/api/command",{"clientId":CLIENT,"runtimeMode":MODE,"command":c})

def time_for_deg(deg):
    """Piecewise linear interpolation between observed (deg, ms) points:
       (0, 0), (50, 238), (180, 466), (350, 927)."""
    if deg <= 0: return 0.0
    pts = [(0, 0), (50, 238), (180, 466), (350, 927)]
    for i in range(len(pts)-1):
        d0, t0 = pts[i]; d1, t1 = pts[i+1]
        if deg <= d1:
            return (t0 + (deg - d0) * (t1 - t0) / (d1 - d0)) / 1000.0
    # extrapolate beyond 350: use last segment slope
    d0, t0 = pts[-2]; d1, t1 = pts[-1]
    return (t1 + (deg - d1) * (t1 - t0) / (d1 - d0)) / 1000.0

direction = sys.argv[1]
deg = float(sys.argv[2])
dur = time_for_deg(deg)

cmd("DirStop"); time.sleep(0.3)
print(f"Mark heading. Will turn {direction.replace('Dir','')} {deg}° in {dur*1000:.0f} ms:")
for i in (5,4,3,2,1):
    print(f"  {i}..."); time.sleep(1)
print("GO!")
t0=time.perf_counter()
cmd(direction)
time.sleep(dur)
cmd("DirStop")
print(f"Done. Cmd duration {1000*(time.perf_counter()-t0):.0f} ms (target {dur*1000:.0f})")
print(f"→ Measure actual rotation. Expected: {deg}°.")
