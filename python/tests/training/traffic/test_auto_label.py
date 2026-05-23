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


def test_parse_state_sample_handles_flat_configkv_telemetry():
    """Real wire format: state.telemetry is ConfigKeyValue[] (list of {key, value} strings)."""
    state = {
        "telemetry": [
            {"key": "drive.left_pwm_norm", "value": "0.3"},
            {"key": "nearestTrafficLight.hasLight", "value": "true"},
            {"key": "nearestTrafficLight.state", "value": "Yellow"},
            {"key": "nearestTrafficLight.distanceM", "value": "5.25"},
        ]
    }
    sample = parse_state_sample(state)
    assert sample is not None
    assert sample.label == GROUND_TRUTH_LABELS.index("Yellow")
    assert abs(sample.distance_m - 5.25) < 0.01


def test_parse_state_sample_flat_list_no_light():
    state = {
        "telemetry": [
            {"key": "nearestTrafficLight.hasLight", "value": "false"},
        ]
    }
    assert parse_state_sample(state) is None


def test_parse_state_sample_flat_configkv_under_extensions_key():
    """Legacy compat: same flat-list format under the older `extensions` key."""
    state = {
        "extensions": [
            {"key": "nearestTrafficLight.hasLight", "value": "true"},
            {"key": "nearestTrafficLight.state", "value": "Green"},
            {"key": "nearestTrafficLight.distanceM", "value": "12.0"},
        ]
    }
    sample = parse_state_sample(state)
    assert sample is not None
    assert sample.label == GROUND_TRUTH_LABELS.index("Green")
