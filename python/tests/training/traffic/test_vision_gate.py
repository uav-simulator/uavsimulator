import numpy as np
import pytest

from training.traffic.vision_gate import crop_detection, detect_traffic_light_state


def _synthetic_camera_frame(active_state: str) -> np.ndarray:
    image = np.full((240, 320, 3), (108, 132, 148), dtype=np.uint8)
    image[150:, :, :] = (60, 62, 58)  # road
    image[:150, :80, :] = (50, 120, 52)  # large green building/park distractor
    image[40:132, 148:174, :] = (36, 34, 28)  # traffic-light housing

    bulb_colors = {
        "Red": (245, 24, 20),
        "Yellow": (248, 210, 34),
        "Green": (38, 230, 64),
    }
    centers = {
        "Red": (161, 58),
        "Yellow": (161, 86),
        "Green": (161, 114),
    }
    yy, xx = np.ogrid[: image.shape[0], : image.shape[1]]
    for state, center in centers.items():
        cx, cy = center
        radius = 8
        mask = (xx - cx) ** 2 + (yy - cy) ** 2 <= radius**2
        if state == active_state:
            image[mask] = bulb_colors[state]
        else:
            image[mask] = (52, 46, 34)
    return image


@pytest.mark.parametrize("state", ["Red", "Yellow", "Green"])
def test_detect_traffic_light_state_from_synthetic_camera_frame(state: str):
    image = _synthetic_camera_frame(state)

    result = detect_traffic_light_state(image)

    assert result.state == state
    assert result.confidence >= 0.35
    assert result.bbox_xyxy is not None
    x1, y1, x2, y2 = result.bbox_xyxy
    assert x1 <= 161 <= x2
    assert y1 <= {"Red": 58, "Yellow": 86, "Green": 114}[state] <= y2


def test_detect_traffic_light_state_prefers_small_signal_over_large_green_region():
    image = _synthetic_camera_frame("Yellow")
    image[12:144, 208:310, :] = (40, 230, 60)  # too large to be a lamp

    result = detect_traffic_light_state(image)

    assert result.state == "Yellow"
    assert result.bbox_xyxy is not None
    assert result.area_px < 1000


def test_detect_traffic_light_state_rejects_wide_red_canopy_in_city_frame():
    image = np.full((720, 1280, 3), (138, 180, 220), dtype=np.uint8)
    image[220:260, 720:820, :] = (225, 70, 72)  # gas-station canopy, not a signal
    image[270:330, 560:590, :] = (58, 58, 52)  # distant traffic-light housing

    yy, xx = np.ogrid[: image.shape[0], : image.shape[1]]
    green_mask = (xx - 575) ** 2 + (yy - 276) ** 2 <= 7**2
    image[green_mask] = (35, 235, 70)

    result = detect_traffic_light_state(image)

    assert result.state == "Green"
    assert result.blob_bbox_xyxy is not None
    assert result.blob_bbox_xyxy[0] < 590


def test_detect_traffic_light_state_returns_none_without_signal():
    image = np.full((180, 260, 3), (76, 82, 86), dtype=np.uint8)
    image[100:, :, :] = (50, 52, 50)

    result = detect_traffic_light_state(image)

    assert result.state == "None"
    assert result.confidence == 0.0
    assert result.bbox_xyxy is None


def test_crop_detection_expands_around_detected_signal():
    image = _synthetic_camera_frame("Green")
    result = detect_traffic_light_state(image)

    crop = crop_detection(image, result)

    assert crop is not None
    assert crop.shape[0] > 40
    assert crop.shape[1] > 30
