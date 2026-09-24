# MiniAVTool Camera Vision

This is the first C++/OpenCV learning project in MiniAVTool.

The program opens the default camera and displays:

- Live camera frames
- FPS, resolution, and timestamp
- Grayscale processing
- Canny edge detection
- Binary thresholding
- Motion regions based on frame differences
- Screenshot saving

## OpenCV Installation

The project is currently configured for this local OpenCV installation:

```text
D:\新建文件夹\opencv\build
```

Important directories:

```text
Headers:
D:\新建文件夹\opencv\build\include

Libraries:
D:\新建文件夹\opencv\build\x64\vc16\lib

Runtime DLLs:
D:\新建文件夹\opencv\build\x64\vc16\bin
```

## Build With CMake

From this directory:

```powershell
cmake --preset x64-debug-opencv
cmake --build --preset x64-debug-opencv --parallel 4
```

Release:

```powershell
cmake --preset x64-release-opencv
cmake --build --preset x64-release-opencv --parallel 4
```

The executable is generated under:

```text
build\x64-debug-opencv\Debug\
build\x64-release-opencv\Release\
```

## Build With Visual Studio

Open the solution:

```text
E:\CodeOther\MiniAVTool\MiniAVDecoder.Wpf.sln
```

Select:

```text
Project: MiniAVTool.CameraVision
Configuration: Debug
Platform: x64
```

The `.vcxproj` already contains the OpenCV include path, library path, linker dependency, and a native MSBuild task that copies required DLLs after build.

## Keyboard Controls

| Key | Action |
| --- | --- |
| `1` | Original frame |
| `2` | Grayscale |
| `3` | Canny edges |
| `4` | Binary threshold |
| `5` | Motion detection |
| `S` | Save the current frame under `captures` |
| `ESC` | Exit |

The default camera index is `0`. To use another camera:

```powershell
MiniAVTool.CameraVision.exe 1
```

## Suggested Reading Order

Read `src/main.cpp` in this order:

1. `openCamera`
2. `detectMotion`
3. `processFrame`
4. `drawOverlay`
5. `main`
