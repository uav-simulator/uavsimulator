"""Drive real KS0223 to precise target distance/angle using calibration data.

Calibration (from earlier evidence):
  - Linear: 0.73 m/s steady, ~150ms spinup (linear ramp)
  - Angular: 380 deg/s steady, ~50-100ms effective spinup (very short burst <250ms
    only reaches half avg; medium+long bursts reach steady-state quickly)

Empirical timing model (for forward and turn):
  - forward_cm  -> dur_s = (cm/100) / 0.73 + 0.03 (3% overhead for spinup)
  - turn_deg    -> use piecewise interpolation between observed datapoints

Usage: python3 precise_motion.py forward 50    # drive 50cm
       python3 precise_motion.py left 90       # rotate left 90deg
       python3 precise_motion.py right 180     # rotate right 180
       python3 precise_motion.py l-route       # demo L-shape: 30cm fwd, 90° turn, 30cm fwd
"""
import json
import sys
import time
import urllib.request

BACKEND = "http://localhost:5287"; CLIENT = "cal"; MODE = "real-robot"

# Real measured calibration
LINEAR_MPS = 0.73
LINEAR_SPINUP_S = 0.10  # estimated overhead

YAW_STEADY_DEG_S = 380.0
# Spinup deficit model: short bursts (<250ms) lose some angle
# Datapoints: 50deg@238ms (deficit=40), 180@466 (-3), 350@927 (+2)
# So ramp essentially complete by ~250ms.
YAW_SPINUP_DEFICIT_DEG = 40.0  # angle "lost" during spinup vs ideal steady

def post(p, d):
    r = urllib.request.Request(f"{BACKEND}{p}", data=json.dumps(d).encode(),
                                headers={"Content-Type":"application/json"}, method="POST")
    return json.loads(urllib.request.urlopen(r, timeout=3).read())

def cmd(c):
    return post("/api/command", {"clientId":CLIENT,"runtimeMode":MODE,"command":c})

def time_for_distance_cm(cm: float) -> float:
    """Time to drive forward N cm: distance/speed + spinup."""
    return (cm / 100.0) / LINEAR_MPS + LINEAR_SPINUP_S

def time_for_angle_deg(deg: float) -> float:
    """Time to rotate N degrees, accounting for spinup deficit."""
    if deg <= 50:
        # Empirical: 50deg in 238ms (extrapolate linearly)
        return deg * 238 / 50 / 1000.0
    # For larger turns, we want angle target, sim achieves (steady_rate * (T - spinup_loss/rate))
    # = steady * T - spinup_deficit -> T = (target + deficit) / steady
    return (deg + YAW_SPINUP_DEFICIT_DEG) / YAW_STEADY_DEG_S

def drive(direction: str, dur_s: float, label: str = ""):
    print(f"  {label or direction}: {direction} for {dur_s*1000:.0f} ms")
    t0 = time.perf_counter()
    cmd(direction)
    time.sleep(dur_s)
    cmd("DirStop")
    t1 = time.perf_counter()
    print(f"     actual cmd duration: {(t1-t0)*1000:.0f} ms")

def go_forward_cm(cm: float):
    print(f"\n=== Forward {cm} cm ===")
    drive("DirForward", time_for_distance_cm(cm), f"+{cm}cm")

def turn_deg(direction: str, deg: float):
    name = "Left" if direction == "DirLeft" else "Right"
    print(f"\n=== Turn {name} {deg}° ===")
    drive(direction, time_for_angle_deg(deg), f"{name} {deg}°")

def main():
    if len(sys.argv) < 2:
        print(__doc__); return
    cmd("DirStop"); time.sleep(0.3)

    op = sys.argv[1]
    arg = float(sys.argv[2]) if len(sys.argv) > 2 else 0.0

    if op == "forward":
        go_forward_cm(arg)
    elif op == "left":
        turn_deg("DirLeft", arg)
    elif op == "right":
        turn_deg("DirRight", arg)
    elif op == "l-route":
        print("Demo L-route: 30cm forward, 90° right turn, 30cm forward")
        go_forward_cm(30); time.sleep(2)
        turn_deg("DirRight", 90); time.sleep(2)
        go_forward_cm(30)
    else:
        print(f"Unknown op: {op}"); sys.exit(1)

if __name__ == "__main__":
    main()
