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
bool AnalyzeFile(const std::filesystem::path& inputPath, MediaInfo& info, std::wstring& error)
{
    const ComPtr<IMFSourceReader> reader = CreateReader(inputPath);
    if (!reader)
    {
        error = L"Failed to open media source.";
        return false;
    }

    info.inputPath = inputPath.wstring();
    info.durationSeconds = ReadDurationSeconds(reader.Get());
    if (!FindStreams(reader.Get(), info))
    {
        error = L"No audio/video stream found.";
        return false;
    }

    return true;
}

bool ExtractFrames(const std::filesystem::path& inputPath, const std::filesystem::path& outputDir, int count, MediaInfo& info, std::wstring& error)
{
    const ComPtr<IMFSourceReader> reader = CreateReader(inputPath);
    if (!reader)
    {
        error = L"Failed to open media source.";
        return false;
    }

    info.inputPath = inputPath.wstring();
    info.durationSeconds = ReadDurationSeconds(reader.Get());
    if (!FindStreams(reader.Get(), info) || info.video.streamIndex == MF_SOURCE_READER_INVALID_STREAM_INDEX)
    {
        error = L"No video stream found.";
        return false;
    }

    if (!SetVideoOutputType(reader.Get(), info.video))
    {
        error = L"Failed to switch video output to RGB32.";
        return false;
    }

    reader->SetStreamSelection(MF_SOURCE_READER_ALL_STREAMS, FALSE);
    reader->SetStreamSelection(info.video.streamIndex, TRUE);

    if (!EnsureDirectory(outputDir))
    {
        error = L"Failed to create output directory.";
        return false;
    }

    info.frameCountTarget = count;
    info.extractedFrames = 0;
    const LONG stride = static_cast<LONG>(info.video.width) * 4;

    while (true)
    {
        DWORD actualStreamIndex = 0;
        DWORD flags = 0;
        LONGLONG timestamp = 0;
        ComPtr<IMFSample> sample;

        if (FAILED(reader->ReadSample(info.video.streamIndex, 0, &actualStreamIndex, &flags, &timestamp, &sample)))
        {
            error = L"ReadSample failed while extracting frames.";
            return false;
        }

        if (flags & MF_SOURCE_READERF_ENDOFSTREAM)
        {
            break;
        }

        if (!sample)
        {
            continue;
        }

        ComPtr<IMFMediaBuffer> buffer;
        if (FAILED(sample->ConvertToContiguousBuffer(&buffer)))
        {
            continue;
        }

        BYTE* data = nullptr;
        DWORD maxLen = 0;
        DWORD curLen = 0;
        if (FAILED(buffer->Lock(&data, &maxLen, &curLen)))
        {
            continue;
        }

        const std::wstring number = (info.extractedFrames < 9 ? L"000" :
                                     info.extractedFrames < 99 ? L"00" :
                                     info.extractedFrames < 999 ? L"0" : L"");
        const std::wstring fileName = std::wstring(L"frame_") + number + std::to_wstring(info.extractedFrames + 1) + L".bmp";
        const std::filesystem::path framePath = outputDir / fileName;
        const bool saved = SaveBmp32(framePath, data, info.video.width, info.video.height, stride);
        buffer->Unlock();

        if (!saved)
        {
            error = L"Failed to save frame bitmap.";
            return false;
        }

        ++info.extractedFrames;
        if (count > 0 && info.extractedFrames >= count)
        {
            break;
        }
    }

    return true;
}

bool ExtractAudio(const std::filesystem::path& inputPath, const std::filesystem::path& outputDir, MediaInfo& info, std::wstring& error)
{
    const ComPtr<IMFSourceReader> reader = CreateReader(inputPath);
    if (!reader)
    {
        error = L"Failed to open media source.";
        return false;
    }

    info.inputPath = inputPath.wstring();
    info.durationSeconds = ReadDurationSeconds(reader.Get());
    if (!FindStreams(reader.Get(), info) || info.audio.streamIndex == MF_SOURCE_READER_INVALID_STREAM_INDEX)
    {
        error = L"No audio stream found.";
        return false;
    }

    if (!SetAudioOutputType(reader.Get(), info.audio))
    {
        error = L"Failed to switch audio output to PCM.";
        return false;
    }

    reader->SetStreamSelection(MF_SOURCE_READER_ALL_STREAMS, FALSE);
    reader->SetStreamSelection(info.audio.streamIndex, TRUE);

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

    while (true)
    {
        DWORD actualStreamIndex = 0;
        DWORD flags = 0;
        LONGLONG timestamp = 0;
        ComPtr<IMFSample> sample;

        if (FAILED(reader->ReadSample(info.audio.streamIndex, 0, &actualStreamIndex, &flags, &timestamp, &sample)))
        {
            error = L"ReadSample failed while extracting audio.";
            return false;
        }

        if (flags & MF_SOURCE_READERF_ENDOFSTREAM)
        {
            break;
        }

        if (!sample)
        {
            continue;
        }

        ComPtr<IMFMediaBuffer> buffer;
        if (FAILED(sample->ConvertToContiguousBuffer(&buffer)))
        {
            continue;
        }

        BYTE* data = nullptr;
        DWORD maxLen = 0;
        DWORD curLen = 0;
        if (FAILED(buffer->Lock(&data, &maxLen, &curLen)))
        {
            continue;
        }

        out.write(reinterpret_cast<const char*>(data), static_cast<std::streamsize>(curLen));
        buffer->Unlock();
    }

    info.audioExtracted = true;
    return true;
}
} // namespace miniav
