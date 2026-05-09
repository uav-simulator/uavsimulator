"""Unit tests for ``sim_client.contract.validate_contract``.

The function is small (16 LoC) but it is the only structural check
between the Python side and the simulator's `/contract` payload, so
locking its behaviour matters more than the size suggests.
"""

from __future__ import annotations

import pytest

from sim_client.contract import validate_contract

_VALID_MINIMAL = {
    "simulatorId": "sim",
    "simulatorName": "uav-simulator",
    "contractVersion": "0.1.0",
}


def test_minimal_payload_is_valid() -> None:
    ok, errors = validate_contract(_VALID_MINIMAL)
    assert ok is True
    assert errors == []


def test_full_payload_with_lists_is_valid() -> None:
    payload = {
        **_VALID_MINIMAL,
        "availableVehicles": [{"id": "vehicle.ks0223.v1"}],
        "availableTracks": [{"id": "track.basic_arena.v1"}],
    }
    ok, errors = validate_contract(payload)
    assert ok is True
    assert errors == []


def test_non_dict_root_is_invalid() -> None:
    ok, errors = validate_contract([1, 2, 3])  # type: ignore[arg-type]
    assert ok is False
    assert errors == ["contract must be an object"]


def test_missing_simulator_id_reports_specific_error() -> None:
    payload = {k: v for k, v in _VALID_MINIMAL.items() if k != "simulatorId"}
    ok, errors = validate_contract(payload)
    assert ok is False
    assert "missing field: simulatorId" in errors


def test_missing_simulator_name_reports_specific_error() -> None:
    payload = {k: v for k, v in _VALID_MINIMAL.items() if k != "simulatorName"}
    ok, errors = validate_contract(payload)
    assert ok is False
    assert "missing field: simulatorName" in errors


def test_missing_contract_version_reports_specific_error() -> None:
    payload = {k: v for k, v in _VALID_MINIMAL.items() if k != "contractVersion"}
    ok, errors = validate_contract(payload)
    assert ok is False
    assert "missing field: contractVersion" in errors


def test_missing_all_required_fields_reports_each() -> None:
    ok, errors = validate_contract({})
    assert ok is False
    assert sorted(errors) == sorted(
        [
            "missing field: simulatorId",
            "missing field: simulatorName",
            "missing field: contractVersion",
        ]
    )


@pytest.mark.parametrize("bad_value", ["not a list", 42, {"foo": "bar"}])
def test_available_vehicles_must_be_list_when_present(bad_value: object) -> None:
    payload = {**_VALID_MINIMAL, "availableVehicles": bad_value}
    ok, errors = validate_contract(payload)
    assert ok is False
    assert "availableVehicles must be a list" in errors


@pytest.mark.parametrize("bad_value", ["not a list", 42, {"foo": "bar"}])
def test_available_tracks_must_be_list_when_present(bad_value: object) -> None:
    payload = {**_VALID_MINIMAL, "availableTracks": bad_value}
    ok, errors = validate_contract(payload)
    assert ok is False
    assert "availableTracks must be a list" in errors


def test_none_for_optional_lists_is_accepted() -> None:
    """Both keys present but explicitly null — current behaviour treats
    that as 'no list provided' and accepts it. Locks the contract."""
    payload = {**_VALID_MINIMAL, "availableVehicles": None, "availableTracks": None}
    ok, errors = validate_contract(payload)
    assert ok is True
    assert errors == []
