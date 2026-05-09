"""Unit tests for ``sim_client.http_client.SimClient``.

These tests do NOT require a running Unity backend — every HTTP interaction
is mocked at the ``requests.Session`` level. The goal is to lock in the
public contract surface and the recently-added retry/error-handling rules:

* connection error and 502/503/504 on GET are silently retried;
* POST requests are NEVER retried after the request leaves the wire (the
  simulator is not idempotent on ``/reset`` and ``/step``);
* server-side ``{"error": "..."}`` payloads are surfaced as ``HTTPError``
  with a useful message rather than raw status text;
* ``404`` responses on optional endpoints (``/api/models/active``,
  ``/api/model-bindings/current``) return ``None`` rather than raising.
"""

from __future__ import annotations

from unittest.mock import MagicMock, patch

import pytest
import requests

from sim_client.http_client import (
    SUPPORTED_CONTRACT_VERSION_MAJOR,
    ContractMismatchError,
    SimClient,
)

# ---------------------------------------------------------------------------
# helpers
# ---------------------------------------------------------------------------


def _make_response(status_code: int, payload: object = None) -> MagicMock:
    """Build a mock ``requests.Response`` with the given status + JSON body."""
    response = MagicMock(spec=requests.Response)
    response.status_code = status_code
    response.json.return_value = payload if payload is not None else {}
    if status_code >= 400:
        response.raise_for_status.side_effect = requests.HTTPError(
            f"{status_code} Server Error", response=response
        )
    else:
        response.raise_for_status.return_value = None
    return response


@pytest.fixture
def client() -> SimClient:
    return SimClient("http://localhost:8000", timeout_s=1.0, retries=2)


# ---------------------------------------------------------------------------
# construction
# ---------------------------------------------------------------------------


def test_simclient_init_attaches_retry_policy_to_adapter(client: SimClient) -> None:
    adapter = client.session.get_adapter("http://localhost:8000")
    retry = adapter.max_retries
    assert retry.total == 2
    assert retry.backoff_factor == pytest.approx(0.3)
    assert 503 in retry.status_forcelist
    # POST must NOT appear in the allowed methods — the simulator is not
    # idempotent on /reset and /step.
    assert "POST" not in retry.allowed_methods
    assert "GET" in retry.allowed_methods


def test_simclient_init_zero_retries_is_supported() -> None:
    c = SimClient("http://localhost:8000", retries=0)
    adapter = c.session.get_adapter("http://localhost:8000")
    assert adapter.max_retries.total == 0


# ---------------------------------------------------------------------------
# happy path: GET / POST round-trips
# ---------------------------------------------------------------------------


def test_health_returns_decoded_json(client: SimClient) -> None:
    expected = {"status": "ok", "uptime": 12.3}
    with patch.object(client.session, "get", return_value=_make_response(200, expected)) as mock_get:
        result = client.health()
    assert result == expected
    mock_get.assert_called_once_with("http://localhost:8000/health", timeout=1.0)


def test_get_contract_hits_correct_path(client: SimClient) -> None:
    with patch.object(client.session, "get", return_value=_make_response(200, {"version": "0.2.0"})) as mock_get:
        client.get_contract()
    assert mock_get.call_args.args[0] == "http://localhost:8000/contract"


def test_reset_posts_payload_and_returns_step_result(client: SimClient) -> None:
    payload = {"trackId": "track.basic_arena.v1", "vehicleId": "vehicle.ks0223.v1"}
    expected = {"state": {}, "info": {}}
    with patch.object(client.session, "post", return_value=_make_response(200, expected)) as mock_post:
        out = client.reset(payload)
    assert out == expected
    args, kwargs = mock_post.call_args
    assert args[0] == "http://localhost:8000/reset"
    assert kwargs["json"] == payload
    assert kwargs["timeout"] == 1.0


def test_step_posts_command(client: SimClient) -> None:
    cmd = {"throttle": 0.3, "steer": -0.1}
    expected = {"state": {"pose": {}}, "info": {"frame_id": 1}}
    with patch.object(client.session, "post", return_value=_make_response(200, expected)) as mock_post:
        out = client.step(cmd)
    assert out == expected
    assert mock_post.call_args.args[0] == "http://localhost:8000/step"
    assert mock_post.call_args.kwargs["json"] == cmd


# ---------------------------------------------------------------------------
# 404 on optional endpoints returns None
# ---------------------------------------------------------------------------


def test_get_active_model_returns_none_on_404(client: SimClient) -> None:
    response = MagicMock(spec=requests.Response)
    response.status_code = 404
    with patch.object(client.session, "get", return_value=response):
        assert client.get_active_model() is None


def test_get_model_binding_returns_none_on_404(client: SimClient) -> None:
    response = MagicMock(spec=requests.Response)
    response.status_code = 404
    with patch.object(client.session, "get", return_value=response):
        assert client.get_model_binding(client_id="op", runtime_mode="unity-sim") is None


def test_get_active_model_returns_payload_on_200(client: SimClient) -> None:
    payload = {"modelId": "ppo-v9-rev42"}
    with patch.object(client.session, "get", return_value=_make_response(200, payload)):
        assert client.get_active_model() == payload


# ---------------------------------------------------------------------------
# error decoration: server-side {"error": "..."} surfaces as HTTPError
# ---------------------------------------------------------------------------


def test_server_error_payload_is_surfaced_in_exception(client: SimClient) -> None:
    response = _make_response(400, {"error": "trackId not in catalog"})
    with patch.object(client.session, "post", return_value=response):
        with pytest.raises(requests.HTTPError, match="trackId not in catalog"):
            client.reset({"trackId": "missing"})


def test_server_error_without_payload_falls_through_to_raise_for_status(client: SimClient) -> None:
    response = MagicMock(spec=requests.Response)
    response.status_code = 500
    response.json.side_effect = ValueError("not JSON")
    response.raise_for_status.side_effect = requests.HTTPError("500 Internal", response=response)
    with patch.object(client.session, "post", return_value=response):
        with pytest.raises(requests.HTTPError, match="500 Internal"):
            client.step({"throttle": 0.0, "steer": 0.0})


# ---------------------------------------------------------------------------
# model binding params shaping
# ---------------------------------------------------------------------------


def test_set_model_binding_includes_agent_id_when_provided(client: SimClient) -> None:
    expected = {"clientId": "op", "runtimeMode": "unity-sim", "modelId": "m1", "agentId": "ego"}
    with patch.object(client.session, "post", return_value=_make_response(200, {})) as mock_post:
        client.set_model_binding(
            client_id="op",
            runtime_mode="unity-sim",
            model_id="m1",
            agent_id="ego",
        )
    assert mock_post.call_args.kwargs["json"] == expected


def test_set_model_binding_omits_agent_id_when_blank(client: SimClient) -> None:
    expected = {"clientId": "op", "runtimeMode": "unity-sim", "modelId": "m1"}
    with patch.object(client.session, "post", return_value=_make_response(200, {})) as mock_post:
        client.set_model_binding(
            client_id="op",
            runtime_mode="unity-sim",
            model_id="m1",
            agent_id="   ",
        )
    assert mock_post.call_args.kwargs["json"] == expected
    assert "agentId" not in mock_post.call_args.kwargs["json"]


# ---------------------------------------------------------------------------
# check_contract_version / assert_contract_compatible
# ---------------------------------------------------------------------------


def test_check_contract_version_matches_supported_major(client: SimClient) -> None:
    payload = {"contractVersion": f"{SUPPORTED_CONTRACT_VERSION_MAJOR}.1.0", "simulatorId": "x"}
    with patch.object(client.session, "get", return_value=_make_response(200, payload)):
        ok, info = client.check_contract_version()
    assert ok is True
    assert info == f"{SUPPORTED_CONTRACT_VERSION_MAJOR}.1.0"


def test_check_contract_version_rejects_major_drift(client: SimClient) -> None:
    payload = {"contractVersion": f"{SUPPORTED_CONTRACT_VERSION_MAJOR + 1}.0.0"}
    with patch.object(client.session, "get", return_value=_make_response(200, payload)):
        ok, info = client.check_contract_version()
    assert ok is False
    assert "major mismatch" in info
    assert f"{SUPPORTED_CONTRACT_VERSION_MAJOR + 1}.0.0" in info


def test_check_contract_version_rejects_missing_field(client: SimClient) -> None:
    with patch.object(client.session, "get", return_value=_make_response(200, {})):
        ok, info = client.check_contract_version()
    assert ok is False
    assert "did not advertise contractVersion" in info


def test_check_contract_version_rejects_unparseable(client: SimClient) -> None:
    payload = {"contractVersion": "not-a-version"}
    with patch.object(client.session, "get", return_value=_make_response(200, payload)):
        ok, info = client.check_contract_version()
    assert ok is False
    assert "unparseable" in info


def test_check_contract_version_accepts_bare_major(client: SimClient) -> None:
    """Server may report just the major (e.g. '0' instead of '0.1.0')."""
    payload = {"contractVersion": str(SUPPORTED_CONTRACT_VERSION_MAJOR)}
    with patch.object(client.session, "get", return_value=_make_response(200, payload)):
        ok, info = client.check_contract_version()
    assert ok is True


def test_assert_contract_compatible_returns_version_string_on_match(client: SimClient) -> None:
    payload = {"contractVersion": f"{SUPPORTED_CONTRACT_VERSION_MAJOR}.2.3"}
    with patch.object(client.session, "get", return_value=_make_response(200, payload)):
        result = client.assert_contract_compatible()
    assert result == f"{SUPPORTED_CONTRACT_VERSION_MAJOR}.2.3"


def test_assert_contract_compatible_raises_on_mismatch(client: SimClient) -> None:
    payload = {"contractVersion": f"{SUPPORTED_CONTRACT_VERSION_MAJOR + 7}.0.0"}
    with patch.object(client.session, "get", return_value=_make_response(200, payload)):
        with pytest.raises(ContractMismatchError, match="major mismatch"):
            client.assert_contract_compatible()


# ---------------------------------------------------------------------------
# wait_for_ready
# ---------------------------------------------------------------------------


def test_wait_for_ready_returns_health_payload_on_first_success(client: SimClient) -> None:
    payload = {"status": "ok", "uptime": 1.0}
    with patch.object(client.session, "get", return_value=_make_response(200, payload)):
        result = client.wait_for_ready(deadline_s=1.0, poll_interval_s=0.01)
    assert result == payload


def test_wait_for_ready_retries_then_succeeds(client: SimClient) -> None:
    """Simulator is briefly unreachable, then comes online — wait_for_ready survives."""
    payload = {"status": "ok"}
    success = _make_response(200, payload)
    side_effects = [requests.ConnectionError("boot"), requests.ConnectionError("boot"), success]
    with patch.object(client.session, "get", side_effect=side_effects):
        result = client.wait_for_ready(deadline_s=2.0, poll_interval_s=0.01)
    assert result == payload


def test_wait_for_ready_raises_timeout_when_deadline_expires(client: SimClient) -> None:
    """If the simulator never comes online, the loop exits with TimeoutError."""
    with patch.object(client.session, "get", side_effect=requests.ConnectionError("never")):
        with pytest.raises(TimeoutError, match="not ready within"):
            client.wait_for_ready(deadline_s=0.05, poll_interval_s=0.01)
