# MiniAVTool Camera Vision

An OpenCV camera-vision sample built with C++20 and CMake.

## Features

- Live camera preview
- FPS, resolution, timestamp, and current mode overlay
- Grayscale, Canny edge, and binary threshold views
- Motion-region detection from frame differences
- Snapshot saving under `captures/`

## Requirements

- CMake 3.24 or newer
- A C++20 compiler
- OpenCV 5.x with CMake package files
- A camera supported by the selected OpenCV backend

The repository does not contain machine-specific OpenCV paths. Configure the local
installation in `CMakeUserPresets.json`, which is ignored by Git.

## Configure OpenCV

Copy `CMakeUserPresets.json.example` to `CMakeUserPresets.json` and update the
path to the directory containing `OpenCVConfig.cmake`:

```json
{
  "version": 6,
  "configurePresets": [
    {
      "name": "x64-debug-local",
      "inherits": "x64-debug",
      "cacheVariables": {
        "OpenCV_DIR": "E:/opencv/opencv/build/x64/vc16/lib"
      }
    }
  ]
}
```

The existing local machine can use:

```powershell
cmake --preset x64-debug-local
cmake --build --preset x64-debug-local --parallel 4
```

For Release:

```powershell
cmake --preset x64-release-local
cmake --build --preset x64-release-local --parallel 4
```

The executable is generated under:

```text
build\x64-debug-local\Debug\
build\x64-release-local\Release\
```

## Visual Studio

The recommended workflow is to configure and build through CMake. The checked-in
`.vcxproj` remains available for compatibility with the existing solution and
uses the local OpenCV path currently configured on this machine.

## Usage

```powershell
MiniAVTool.CameraVision.exe [camera-index]
```

The default camera index is `0`. On Windows, the application tries DirectShow
first and Media Foundation as a fallback.

Keyboard controls:

| Key | Action |
| --- | --- |
| `1` | Original frame |
| `2` | Grayscale |
| `3` | Canny edges |
| `4` | Binary threshold |
| `5` | Motion detection |
| `S` | Save a snapshot |
| `ESC` | Exit |

## Source Layout

```text
src/
  camera/   Camera opening and backend selection
  io/       Snapshot persistence
  ui/       OpenCV overlay drawing
  vision/   Frame processing and motion detection
  main.cpp  Application loop and input dispatch
```
