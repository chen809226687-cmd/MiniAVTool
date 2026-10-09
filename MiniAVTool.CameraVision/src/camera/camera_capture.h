#pragma once

// OpenCV 的 VideoCapture 类型负责连接摄像头并读取视频帧。
#include <opencv2/videoio.hpp>

// std::string 用于向调用方返回可读的错误信息。
#include <string>

// 所有摄像头相关代码都放在 miniav::camera 命名空间中，
// 避免和其他模块中的 open、configure 等常见名称发生冲突。
namespace miniav::camera {

// 打开指定索引的摄像头，并尝试设置默认采集参数。
// 成功时返回 true；失败时返回 false，并通过 error 写入原因。
bool open(cv::VideoCapture& capture, int cameraIndex, std::string& error);

} // namespace miniav::camera
