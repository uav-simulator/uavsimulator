from training.traffic.auto_label import parse_state_sample, GROUND_TRUTH_LABELS


def test_parse_state_sample_extracts_traffic_light():
    state = {
        "pose": {"x": 0, "y": 0, "z": 0},
        "linearVelocity": {"x": 0, "y": 0, "z": 0},
        "extensions": {
            "nearestTrafficLight": {"hasLight": True, "state": "Red", "distanceM": 7.4}
        },
    }
    sample = parse_state_sample(state)
    assert sample is not None
    assert sample.label == GROUND_TRUTH_LABELS.index("Red")
    assert sample.distance_m == 7.4


def test_parse_state_sample_returns_none_when_no_light():
    state = {"extensions": {"nearestTrafficLight": {"hasLight": False, "state": "None"}}}
    assert parse_state_sample(state) is None
