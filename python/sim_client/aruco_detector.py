"""ArUco marker detector for sim-to-real goal detection.

Works identically in simulator (from runtime camera frames) and on the real robot
(from KS0223 camera feed). Uses OpenCV ArUco module.

Usage:
    detector = ArucoGoalDetector(marker_size_m=0.12, goal_distance_m=0.40)
    result = detector.detect(frame_rgb)
    if result.goal_reached:
        print(f"Goal! Marker {result.marker_id} at {result.distance_m:.2f}m")
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field
from typing import Optional

import numpy as np

try:
    import cv2
except ImportError:
    cv2 = None


@dataclass
class ArucoDetection:
    """Result of a single ArUco detection attempt."""
    detected: bool = False
    marker_id: int = -1
    distance_m: float = float("inf")
    center_px: tuple[float, float] = (0.0, 0.0)
    corners: Optional[np.ndarray] = None
    goal_reached: bool = False


@dataclass
class ArucoGoalDetector:
    """Detects ArUco markers and determines if the robot reached the goal.

    Args:
        marker_size_m: Physical size of the marker in meters (one side).
        goal_distance_m: Distance threshold to consider goal reached.
        dictionary_id: ArUco dictionary type (default DICT_4X4_50).
        camera_fov_deg: Horizontal field of view of the camera in degrees.
        image_width_px: Expected image width for distance estimation.
            If None, uses the actual frame width.
    """
    marker_size_m: float = 0.12
    goal_distance_m: float = 0.40
    dictionary_id: int = 0  # cv2.aruco.DICT_4X4_50
    camera_fov_deg: float = 60.0
    image_width_px: Optional[int] = None

    # Internal state
    _dictionary: object = field(default=None, repr=False, init=False)
    _parameters: object = field(default=None, repr=False, init=False)
    _last_result: ArucoDetection = field(default_factory=ArucoDetection, repr=False, init=False)

    def __post_init__(self):
        if cv2 is None:
            return
        self._dictionary = cv2.aruco.getPredefinedDictionary(self.dictionary_id)
        self._parameters = cv2.aruco.DetectorParameters()

    def detect(self, frame: np.ndarray) -> ArucoDetection:
        """Detect ArUco markers in an RGB or BGR frame.

        Args:
            frame: (H, W, 3) uint8 image (RGB or BGR — ArUco works with both).

        Returns:
            ArucoDetection with marker info and goal_reached flag.
        """
        if cv2 is None:
            return ArucoDetection()

        if frame is None or frame.size == 0:
            return ArucoDetection()

        # Convert to grayscale for detection
        if len(frame.shape) == 3 and frame.shape[2] == 3:
            gray = cv2.cvtColor(frame, cv2.COLOR_RGB2GRAY)
        else:
            gray = frame

        # Detect markers
        detector = cv2.aruco.ArucoDetector(self._dictionary, self._parameters)
        corners, ids, _ = detector.detectMarkers(gray)

        if ids is None or len(ids) == 0:
            self._last_result = ArucoDetection()
            return self._last_result

        # Take the closest (largest apparent size) marker
        best_idx = 0
        best_size = 0.0
        for i, corner_set in enumerate(corners):
            pts = corner_set[0]
            side = float(np.linalg.norm(pts[0] - pts[1]))
            if side > best_size:
                best_size = side
                best_idx = i

        marker_corners = corners[best_idx][0]  # (4, 2)
        marker_id = int(ids[best_idx][0])
        center = marker_corners.mean(axis=0)

        # Estimate distance from apparent size
        img_width = self.image_width_px or frame.shape[1]
        distance_m = self._estimate_distance(best_size, img_width)

        result = ArucoDetection(
            detected=True,
            marker_id=marker_id,
            distance_m=distance_m,
            center_px=(float(center[0]), float(center[1])),
            corners=marker_corners,
            goal_reached=distance_m <= self.goal_distance_m,
        )
        self._last_result = result
        return result

    def detect_from_jpeg(self, jpeg_bytes: bytes) -> ArucoDetection:
        """Detect from raw JPEG bytes (e.g. from camera stream or base64-decoded frame)."""
        if cv2 is None:
            return ArucoDetection()

        arr = np.frombuffer(jpeg_bytes, dtype=np.uint8)
        frame = cv2.imdecode(arr, cv2.IMREAD_COLOR)
        if frame is None:
            return ArucoDetection()

        return self.detect(frame)

    def detect_from_base64(self, data_base64: str) -> ArucoDetection:
        """Detect from a base64-encoded JPEG string (from Unity /step response)."""
        import base64
        if not data_base64:
            return ArucoDetection()
        jpeg_bytes = base64.b64decode(data_base64)
        return self.detect_from_jpeg(jpeg_bytes)

    @property
    def last_result(self) -> ArucoDetection:
        """Last detection result."""
        return self._last_result

    def _estimate_distance(self, apparent_size_px: float, image_width_px: int) -> float:
        """Estimate distance to marker using pinhole camera model.

        distance = (real_size * focal_length_px) / apparent_size_px
        focal_length_px = image_width / (2 * tan(fov/2))
        """
        if apparent_size_px < 1.0:
            return float("inf")

        fov_rad = math.radians(self.camera_fov_deg)
        focal_px = image_width_px / (2.0 * math.tan(fov_rad / 2.0))
        distance = (self.marker_size_m * focal_px) / apparent_size_px
        return float(distance)
