#pragma once

#include "media_types.h"

#include <filesystem>

#include <wrl/client.h>

struct IMFSourceReader;

namespace miniav
{
Microsoft::WRL::ComPtr<IMFSourceReader> CreateReader(const std::filesystem::path& inputPath);
bool FindStreams(IMFSourceReader* reader, MediaInfo& info);
double ReadDurationSeconds(IMFSourceReader* reader);
bool SetVideoOutputType(IMFSourceReader* reader, const VideoInfo& video);
bool SetAudioOutputType(IMFSourceReader* reader, const AudioInfo& audio);
} // namespace miniav
