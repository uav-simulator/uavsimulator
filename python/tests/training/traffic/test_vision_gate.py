import numpy as np
import pytest

from training.traffic.vision_gate import crop_detection, detect_traffic_light_state


def _synthetic_camera_frame(active_state: str) -> np.ndarray:
    image = np.full((240, 320, 3), (108, 132, 148), dtype=np.uint8)
    image[150:, :, :] = (60, 62, 58)  # road
    image[:150, :80, :] = (50, 120, 52)  # large green building/park distractor
    image[40:132, 148:174, :] = (142, 122, 34)  # POLYGON-style yellow traffic-light housing

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
    image[270:330, 560:590, :] = (142, 122, 34)  # distant traffic-light housing

    yy, xx = np.ogrid[: image.shape[0], : image.shape[1]]
    green_mask = (xx - 575) ** 2 + (yy - 276) ** 2 <= 7**2
    image[green_mask] = (35, 235, 70)

    result = detect_traffic_light_state(image)

    assert result.state == "Green"
    assert result.blob_bbox_xyxy is not None
    assert result.blob_bbox_xyxy[0] < 590


def test_detect_traffic_light_state_prefers_low_contrast_green_bulb_over_yellow_housing():
    image = np.full((720, 1280, 3), (142, 190, 230), dtype=np.uint8)
    image[420:, :, :] = (55, 58, 58)
    image[250:322, 560:592, :] = (142, 122, 34)
    yy, xx = np.ogrid[: image.shape[0], : image.shape[1]]
    for cy in (268, 290):
        off_mask = (xx - 576) ** 2 + (yy - cy) ** 2 <= 5**2
        image[off_mask] = (44, 40, 32)
    green_mask = (xx - 576) ** 2 + (yy - 310) ** 2 <= 5**2
    image[green_mask] = (84, 140, 57)

    result = detect_traffic_light_state(image)

    assert result.state == "Green"
    assert result.blob_bbox_xyxy is not None
    assert result.blob_bbox_xyxy[1] >= 300


def test_detect_traffic_light_state_rejects_construction_barricades():
    image = np.full((360, 640, 3), (138, 180, 220), dtype=np.uint8)
    image[150:, :, :] = (55, 58, 58)
    # Orange/white road barricades in the distance: saturated, small, and
    # visually close to traffic-light colors. The red patches intentionally
    # mimic the real false-positive crops from old chase screenshots.
    for offset in (0, 46, 92):
        x1 = 250 + offset
        image[160:205, x1 : x1 + 36, :] = (225, 84, 38)
        image[166:174, x1 : x1 + 36, :] = (238, 238, 230)
        image[190:198, x1 : x1 + 36, :] = (238, 238, 230)
        image[164:171, x1 + 10 : x1 + 24, :] = (220, 35, 28)
    image[155:215, 242:250, :] = (150, 120, 30)

    result = detect_traffic_light_state(image)

    assert result.state == "None"
    assert result.bbox_xyxy is None


def test_detect_traffic_light_state_rejects_warm_roadwork_cones_without_signal():
    image = np.full((360, 640, 3), (142, 190, 230), dtype=np.uint8)
    image[190:, :, :] = (82, 84, 78)
    # POLYGON construction cones/barricades use warm yellow-orange plus white
    # stripes. In real city closeups these can look like a valid signal housing
    # unless explicitly rejected.
    for x in (365, 410, 455, 500, 545):
        image[135:225, x : x + 38, :] = (220, 150, 40)
        image[145:158, x : x + 38, :] = (245, 245, 235)
        image[184:198, x : x + 38, :] = (245, 245, 235)
        image[132:142, x + 9 : x + 29, :] = (220, 35, 28)

    result = detect_traffic_light_state(image)

    assert result.state == "None"
    assert result.bbox_xyxy is None


def test_detect_traffic_light_state_rejects_green_street_bin_after_intersection():
    image = np.full((720, 1280, 3), (142, 190, 230), dtype=np.uint8)
    image[330:, :, :] = (80, 84, 78)
    image[300:430, 230:360, :] = (130, 140, 58)
    image[339:386, 278:314, :] = (30, 220, 50)

    result = detect_traffic_light_state(image)

    assert result.state == "None"
    assert result.bbox_xyxy is None


def test_detect_traffic_light_state_rejects_status_overlay_text():
    image = np.full((360, 640, 3), (142, 190, 230), dtype=np.uint8)
    image[150:, :, :] = (82, 84, 78)
    image[0:70, 0:230, :] = (8, 12, 14)
    image[15:25, 30:70, :] = (235, 30, 28)
    image[28:38, 30:58, :] = (235, 30, 28)
    for y in (8, 45, 58):
        image[y : y + 4, 8:210, :] = (240, 240, 240)
    image[40:50, 120:200, :] = (230, 190, 35)
    image[120:260, 300:310, :] = (200, 165, 28)

    result = detect_traffic_light_state(image)

    assert result.state == "None"
    assert result.bbox_xyxy is None


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
