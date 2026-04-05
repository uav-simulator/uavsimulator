# {{DISPLAY_NAME}}

Plugin ID: `{{PLUGIN_ID}}`
Type: track

## Description

{{DESCRIPTION}}

## Installation

```bash
rusim plugin install {{PLUGIN_ID}}.rusim-plugin.zip
```

## Development

1. Open the Unity project containing this plugin (requires Unity 6).
2. Ensure the Plugin SDK is installed:
   ```
   com.uav-simulator.plugin-sdk
   ```
3. Create a prefab with a `TrackBase` subclass.
4. Fill in the TrackPluginDescriptor asset.
5. Validate: **Tools > UavSimulator > Validate Plugins**
6. Export: **Tools > UavSimulator > Export Plugin (.zip)**
