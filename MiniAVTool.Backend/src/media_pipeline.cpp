#define NOMINMAX
#include "media_pipeline.h"

#include "media_reader.h"
#include "media_utils.h"
#include "media_writer.h"

#include <mfreadwrite.h>
#include <fstream>

using Microsoft::WRL::ComPtr;

namespace miniav
{
// 分析媒体文件的入口。
// 本函数只读取媒体源的元数据，不会把全部音视频内容解码到磁盘。
bool AnalyzeFile(const std::filesystem::path& inputPath, MediaInfo& info, std::wstring& error)
{
    // 打开媒体文件并创建 Source Reader。
    const ComPtr<IMFSourceReader> reader = CreateReader(inputPath);
    if (!reader)
    {
        // reader 为空说明文件无法打开或格式/解码器不可用。
        error = L"Failed to open media source.";
        return false;
    }

    // 保存输入路径，后续会写入 media_info.json。
    info.inputPath = inputPath.wstring();

    // 从媒体源属性读取总时长。
    info.durationSeconds = ReadDurationSeconds(reader.Get());

    // 枚举音视频流并读取它们的媒体类型。
    if (!FindStreams(reader.Get(), info))
    {
        // 没有可识别的音频或视频流时，分析没有意义。
        error = L"No audio/video stream found.";
        return false;
    }

    // 上层 app.cpp 会负责把 info 序列化成 JSON。
    return true;
}

// 抽取视频帧并保存为 BMP。
// 读取顺序是从视频开头开始，count 为 0 时持续到文件结束。
bool ExtractFrames(const std::filesystem::path& inputPath, const std::filesystem::path& outputDir, int count, MediaInfo& info, std::wstring& error)
{
    // 抽帧操作使用独立的 Source Reader，避免与分析或音频操作共享读取位置。
    const ComPtr<IMFSourceReader> reader = CreateReader(inputPath);
    if (!reader)
    {
        error = L"Failed to open media source.";
        return false;
    }

    // 先保存媒体基本信息，方便最后统一输出 JSON。
    info.inputPath = inputPath.wstring();
    info.durationSeconds = ReadDurationSeconds(reader.Get());

    // 抽帧必须存在视频流。
    if (!FindStreams(reader.Get(), info) || info.video.streamIndex == MF_SOURCE_READER_INVALID_STREAM_INDEX)
    {
        error = L"No video stream found.";
        return false;
    }

    // 请求 Source Reader 把压缩视频解码成 RGB32。
    if (!SetVideoOutputType(reader.Get(), info.video))
    {
        error = L"Failed to switch video output to RGB32.";
        return false;
    }

    // 关闭所有流，防止读取视频时同时收到音频样本。
    reader->SetStreamSelection(MF_SOURCE_READER_ALL_STREAMS, FALSE);

    // 只打开目标视频流。
    reader->SetStreamSelection(info.video.streamIndex, TRUE);

    // BMP 文件需要写入输出目录，因此先保证目录存在。
    if (!EnsureDirectory(outputDir))
    {
        error = L"Failed to create output directory.";
        return false;
    }

    // 记录用户目标数量，并清零本次任务的实际抽取数量。
    info.frameCountTarget = count;
    info.extractedFrames = 0;

    // RGB32 每个像素 4 字节；当前代码假定输出缓冲区按紧密行排列。
    const LONG stride = static_cast<LONG>(info.video.width) * 4;

    // Source Reader 的同步读取循环：一次 ReadSample 获取一个视频样本。
    while (true)
    {
        // actualStreamIndex 用于返回实际产生样本的流编号；
        // flags 用于返回结束、错误等状态；
        // timestamp 是样本时间戳，本实现只保存顺序帧，不使用时间戳。
        DWORD actualStreamIndex = 0;
        DWORD flags = 0;
        LONGLONG timestamp = 0;
        ComPtr<IMFSample> sample;

        // 读取并解码下一个视频样本。
        if (FAILED(reader->ReadSample(info.video.streamIndex, 0, &actualStreamIndex, &flags, &timestamp, &sample)))
        {
            error = L"ReadSample failed while extracting frames.";
            return false;
        }

        // Source Reader 到达文件末尾时不会再有可处理帧。
        if (flags & MF_SOURCE_READERF_ENDOFSTREAM)
        {
            break;
        }

        // 某些调用可能只返回状态而暂时没有样本，继续下一轮即可。
        if (!sample)
        {
            continue;
        }

        // 一个 sample 可能由多个 buffer 组成；
        // 合并成连续 buffer 后，才能用一段 BYTE* 统一保存。
        ComPtr<IMFMediaBuffer> buffer;
        if (FAILED(sample->ConvertToContiguousBuffer(&buffer)))
        {
            continue;
        }

        // Lock 后获得缓冲区地址、容量和当前有效字节数。
        // 当前帧的数据只允许在 Unlock 前访问。
        BYTE* data = nullptr;
        DWORD maxLen = 0;
        DWORD curLen = 0;
        if (FAILED(buffer->Lock(&data, &maxLen, &curLen)))
        {
            continue;
        }

        // 使用固定宽度编号，让文件按名称排序时仍保持帧顺序。
        const std::wstring number = (info.extractedFrames < 9 ? L"000" :
                                     info.extractedFrames < 99 ? L"00" :
                                     info.extractedFrames < 999 ? L"0" : L"");
        const std::wstring fileName = std::wstring(L"frame_") + number + std::to_wstring(info.extractedFrames + 1) + L".bmp";
        const std::filesystem::path framePath = outputDir / fileName;

        // SaveBmp32 会复制/规范化像素行并写入 BMP 文件。
        const bool saved = SaveBmp32(framePath, data, info.video.width, info.video.height, stride);

        // 无论保存是否成功，都必须释放对 Media Buffer 的锁。
        buffer->Unlock();

        if (!saved)
        {
            error = L"Failed to save frame bitmap.";
            return false;
        }

        // 只有成功写盘后才增加实际帧数。
        ++info.extractedFrames;

        // count 大于 0 表示达到目标数量后立即停止。
        if (count > 0 && info.extractedFrames >= count)
        {
            break;
        }
    }

    // app.cpp 会在此之后写出 media_info.json。
    return true;
}

// 抽取音频并保存为裸 PCM 文件。
// 输出文件不包含 WAV 头，采样率、声道数等信息保存在 media_info.json 中。
bool ExtractAudio(const std::filesystem::path& inputPath, const std::filesystem::path& outputDir, MediaInfo& info, std::wstring& error)
{
    // 音频抽取重新创建 Source Reader，从媒体起点开始读取音频。
    const ComPtr<IMFSourceReader> reader = CreateReader(inputPath);
    if (!reader)
    {
        error = L"Failed to open media source.";
        return false;
    }

    // 保存输入路径和媒体时长。
    info.inputPath = inputPath.wstring();
    info.durationSeconds = ReadDurationSeconds(reader.Get());

    // 没有音频流时无法生成 PCM 文件。
    if (!FindStreams(reader.Get(), info) || info.audio.streamIndex == MF_SOURCE_READER_INVALID_STREAM_INDEX)
    {
        error = L"No audio stream found.";
        return false;
    }

    // 请求 Source Reader 解码输出 16 位 PCM。
    if (!SetAudioOutputType(reader.Get(), info.audio))
    {
        error = L"Failed to switch audio output to PCM.";
        return false;
    }

    // 关闭其他流，只让 Source Reader 产生目标音频样本。
    reader->SetStreamSelection(MF_SOURCE_READER_ALL_STREAMS, FALSE);
    reader->SetStreamSelection(info.audio.streamIndex, TRUE);

    // 创建输出目录和裸 PCM 文件。
    if (!EnsureDirectory(outputDir))
    {
        error = L"Failed to create output directory.";
        return false;
    }

    const std::filesystem::path audioPath = outputDir / L"audio.pcm";
    std::ofstream out(audioPath, std::ios::binary);
    if (!out)
    {
        error = L"Failed to create audio output file.";
        return false;
    }

    // 持续读取音频样本，直到 Source Reader 报告文件结束。
    while (true)
    {
        // 这些输出参数的含义与视频抽帧相同；
        // 音频抽取同样使用同步 ReadSample。
        DWORD actualStreamIndex = 0;
        DWORD flags = 0;
        LONGLONG timestamp = 0;
        ComPtr<IMFSample> sample;

        // Source Reader 会先解码压缩音频，再返回 PCM sample。
        if (FAILED(reader->ReadSample(info.audio.streamIndex, 0, &actualStreamIndex, &flags, &timestamp, &sample)))
        {
            error = L"ReadSample failed while extracting audio.";
            return false;
        }

        // 读取到媒体末尾时退出循环。
        if (flags & MF_SOURCE_READERF_ENDOFSTREAM)
        {
            break;
        }

        // 没有实际样本时继续等待/读取下一次结果。
        if (!sample)
        {
            continue;
        }

        // 将 sample 中可能分散的多个 buffer 合并成连续内存。
        ComPtr<IMFMediaBuffer> buffer;
        if (FAILED(sample->ConvertToContiguousBuffer(&buffer)))
        {
            continue;
        }

        // Lock 后才能访问 PCM 字节；Unlock 必须与之成对调用。
        BYTE* data = nullptr;
        DWORD maxLen = 0;
        DWORD curLen = 0;
        if (FAILED(buffer->Lock(&data, &maxLen, &curLen)))
        {
            continue;
        }

        // 把当前 PCM 样本追加写入文件，不添加封装头或额外元数据。
        out.write(reinterpret_cast<const char*>(data), static_cast<std::streamsize>(curLen));
        buffer->Unlock();
    }

    // 能完整读到文件末尾，才标记音频已提取。
    info.audioExtracted = true;
    return true;
}
} // namespace miniav
