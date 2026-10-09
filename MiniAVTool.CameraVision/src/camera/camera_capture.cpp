#include "camera_capture.h"

// stringstream 用于拼接包含摄像头索引的错误信息。
#include <sstream>

namespace miniav::camera {

namespace {

// 尝试设置摄像头的目标分辨率和帧率。
// 不同摄像头对参数的支持不同，因此这里不把“全部设置成功”作为打开失败条件。
bool configureCapture(cv::VideoCapture& capture) {
    // 请求宽度为 1280 像素。
    const bool widthSet = capture.set(cv::CAP_PROP_FRAME_WIDTH, 1280);

    // 请求高度为 720 像素。
    const bool heightSet = capture.set(cv::CAP_PROP_FRAME_HEIGHT, 720);

    // 请求帧率为 30 FPS。
    const bool fpsSet = capture.set(cv::CAP_PROP_FPS, 30);

    // 当前函数的返回值仅用于保留设置结果；调用方允许部分参数不被设备支持。
    return widthSet || heightSet || fpsSet;
}

} // namespace

bool open(cv::VideoCapture& capture, const int cameraIndex, std::string& error) {
    // 摄像头索引不应为负数，提前校验可以避免调用 OpenCV 后才得到模糊错误。
    if (cameraIndex < 0) {
        error = "Camera index must be non-negative.";
        return false;
    }

#ifdef _WIN32
    // Windows 上优先尝试 DirectShow，通常对传统 USB 摄像头兼容性较好。
    if (capture.open(cameraIndex, cv::CAP_DSHOW) ||
        // 如果 DirectShow 打不开，再尝试 Media Foundation。
        // 这对部分新设备和系统摄像头更合适。
        capture.open(cameraIndex, cv::CAP_MSMF)) {
        // 摄像头已连接成功，再尝试应用目标采集参数。
        configureCapture(capture);
        return true;
    }
#else
    // 非 Windows 平台交给 OpenCV 自动选择可用的视频后端。
    if (capture.open(cameraIndex)) {
        // 打开成功后再设置期望的分辨率和帧率。
        configureCapture(capture);
        return true;
    }
#endif

    // 两种后端都失败时，统一生成一条面向用户的错误信息。
    std::ostringstream message;
    message << "Cannot open camera index " << cameraIndex
            << ". Make sure the camera is connected and not in use by another app.";
    error = message.str();
    return false;
}

} // namespace miniav::camera
