# UAV Simulator Plugin SDK

SDK for developing plugins (vehicles, tracks) for the **uav-simulator** platform.

## Installation

Add to your Unity project via **Window > Package Manager > Add package from git URL**:

```
https://github.com/NMGorovenko/uav-simulator.git?path=packages/com.uav-simulator.plugin-sdk
```

Or add directly to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.uav-simulator.plugin-sdk": "https://github.com/NMGorovenko/uav-simulator.git?path=packages/com.uav-simulator.plugin-sdk"
  }
}
```

## Requirements

- Unity 6 (6000.1+)
- Target runtime version must match the SDK version

## What's included

### Runtime

- `PluginDescriptorBase` -- abstract base class for plugin descriptors
- `VehiclePluginDescriptor` -- vehicle plugin descriptor (prefab + device contract)
- `TrackPluginDescriptor` -- track plugin descriptor (prefab + parameters schema)
- `PluginRegistryAsset` -- registry ScriptableObject for cataloging plugins
- `VehicleBase` -- abstract base class for vehicle MonoBehaviours
- `TrackBase` -- abstract base class for track MonoBehaviours
- Contract types: `ContractVersion`, `DeviceContractDescriptor`, `DeviceContractDescriptorAsset`, and supporting serializable types

### Editor

- **Tools > UavSimulator > Validate Plugins** -- check all plugin descriptors for completeness
- **Tools > UavSimulator > Export Plugin (.zip)** -- package a plugin into `.rusim-plugin.zip` archive for distribution

## Quick start

1. Create a new Unity project (or use the plugin template)
2. Install this SDK package
3. Create your vehicle/track prefab
4. Create a descriptor: **Create > UavSimulator/Plugins/Vehicle Plugin** (or **Track Plugin**)
5. Fill in the descriptor fields (id, displayName, version, prefab, device contract)
6. Validate: **Tools > UavSimulator > Validate Plugins**
7. Export: **Tools > UavSimulator > Export Plugin (.zip)**
8. Install on target: `rusim plugin install my-plugin.rusim-plugin.zip`

## Plugin ID convention

- Vehicles: `vehicle.{brand}.{model}.v{major}` (e.g., `vehicle.prometeo.sport.v1`)
- Tracks: `track.{name}.v{major}` (e.g., `track.basic_arena.v1`)

## Archive format

The exported `.rusim-plugin.zip` contains:

```
manifest.json           -- plugin metadata (pluginId, type, version, compatibility)
descriptor.json         -- serialized PluginDescriptor
device-contract.json    -- serialized DeviceContractDescriptor (vehicles only)
README.md               -- auto-generated description
```
