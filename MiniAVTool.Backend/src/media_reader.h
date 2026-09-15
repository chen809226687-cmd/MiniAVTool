#pragma once

// media_reader 模块负责 Media Foundation Source Reader 的创建、
// 流信息读取以及解码输出格式协商。

#include "media_types.h"

#include <filesystem>

#include <wrl/client.h>

struct IMFSourceReader;

namespace miniav
{
// 根据输入路径创建一个 Media Foundation Source Reader。
// 返回值使用 ComPtr 管理 COM 对象生命周期，创建失败时为空。
Microsoft::WRL::ComPtr<IMFSourceReader> CreateReader(const std::filesystem::path& inputPath);

// 枚举 Source Reader 中的流，并把第一个视频流和第一个音频流的信息
// 写入 MediaInfo。
bool FindStreams(IMFSourceReader* reader, MediaInfo& info);

// 读取媒体源的总时长，并把 Media Foundation 的 100 纳秒单位转换成秒。
double ReadDurationSeconds(IMFSourceReader* reader);

// 请求 Source Reader 将视频解码成 RGB32，方便直接保存为 BMP。
bool SetVideoOutputType(IMFSourceReader* reader, const VideoInfo& video);

// 请求 Source Reader 将音频解码成 16 位 PCM，方便直接写入 audio.pcm。
bool SetAudioOutputType(IMFSourceReader* reader, const AudioInfo& audio);
} // namespace miniav
